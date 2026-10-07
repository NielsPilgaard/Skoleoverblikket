using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Email;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Storage;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// Enforces the privacy policy's promise: a school's data is kept for
/// <see cref="RetentionPeriod"/> after its subscription is canceled, then permanently deleted.
/// Admins are emailed <see cref="WarningNotice"/> before, and deletion never happens until that
/// email has gone out at least <see cref="WarningNotice"/> ago.
///
/// This is the one writer that deletes every feature's rows. That breaks the "owning service is
/// the only writer" rule on purpose: deleting a whole school is not a feature operation, and going
/// through 20 services would leave the wipe half done whenever one of them is skipped.
///
/// Callers pin the scope to the school with <see cref="HttpTenantContext.UseBackgroundTenant"/>
/// (see <see cref="SchoolRetentionJob"/>), so all reads go through the tenant query filter.
/// </summary>
public sealed class SchoolDeletionService(
	AppDbContext db,
	ITenantContext tenant,
	IObjectStorage storage,
	KeycloakAdminService keycloak,
	IEmailSender email,
	IOptions<ApplicationOptions> appOptions,
	ILogger<SchoolDeletionService> logger)
{
	/// <summary>Matches "Opbevaring og sletning" in the privacy policy (PrivacyPolicyPage.tsx).</summary>
	public static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(90);

	public static readonly TimeSpan WarningNotice = TimeSpan.FromDays(7);

	private static readonly CultureInfo Danish = CultureInfo.GetCultureInfo("da-DK");

	public enum Outcome
	{
		NotDue,
		WarningSent,
		StorageFailed,
		AccountsFailed,
		Deleted,
	}

	/// <summary>
	/// Schools whose cancellation is old enough that they need a warning or deletion now.
	/// Reads <see cref="Subscription"/>, which is not tenant-scoped, so this spans every school.
	/// </summary>
	public async Task<IReadOnlyList<Guid>> ListSchoolsDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
	{
		var warnFrom = (now - (RetentionPeriod - WarningNotice)).ToUniversalTime();
		return await db.Subscriptions
			.Where(s => s.CanceledAt != null && s.CanceledAt <= warnFrom)
			.Select(s => s.SchoolId)
			.ToListAsync(cancellationToken);
	}

	/// <summary>Sends the deletion warning or deletes the current school, whichever is due.</summary>
	public async Task<Outcome> ApplyRetentionAsync(DateTimeOffset now, CancellationToken cancellationToken)
	{
		var schoolId = tenant.TenantId;
		var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken);
		if (sub?.CanceledAt is not { } canceledAt || canceledAt > now - (RetentionPeriod - WarningNotice))
		{
			return Outcome.NotDue;
		}

		if (sub.DeletionWarningSentAt is not { } warnedAt)
		{
			var deletionDate = Max(canceledAt + RetentionPeriod, now + WarningNotice);

			// Send before saving: if the email fails, nothing is recorded and the next pass retries.
			// A failed save after a sent email only means a duplicate warning, never a silent deletion.
			await SendWarningAsync(deletionDate, cancellationToken);
			sub.DeletionWarningSentAt = now.ToUniversalTime();
			await db.SaveChangesAsync(cancellationToken);
			logger.LogInformation("Sent deletion warning for school {SchoolId}, deletion on {DeletionDate:yyyy-MM-dd}", schoolId, deletionDate);
			return Outcome.WarningSent;
		}

		if (!IsDeletionDue(canceledAt, warnedAt, now))
		{
			return Outcome.NotDue;
		}

		return await DeleteSchoolAsync(now, cancellationToken);
	}

	/// <summary>
	/// Deletes <see cref="SchoolDeletionRecord"/> rows for schools that can no longer be in any
	/// backup. Not tenant-scoped, so this spans every school.
	/// </summary>
	public Task<int> PruneDeletionRecordsAsync(DateTimeOffset now, CancellationToken cancellationToken)
	{
		var cutoff = (now - SchoolDeletionRecord.BackupRetention).ToUniversalTime();
		return db.SchoolDeletionRecords.Where(r => r.DeletedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
	}

	private static bool IsDeletionDue(DateTimeOffset canceledAt, DateTimeOffset warnedAt, DateTimeOffset now) =>
		canceledAt <= now - RetentionPeriod && warnedAt <= now - WarningNotice;

	/// <summary>
	/// Every object key this school can own. Keep in sync with the upload endpoints — each one
	/// builds its key from the tenant id, and all of them are listed here.
	/// </summary>
	public static IReadOnlyList<string> StoragePrefixes(Guid schoolId) =>
	[
		$"files/{schoolId}/",
		$"board-files/{schoolId}/",
		$"class-chat/{schoolId}/",
		$"avatars/{schoolId}/",
		$"backups/{schoolId}/",
		$"logos/{schoolId}", // Logos are "logos/{schoolId}.png" — no folder.
	];

	private async Task<Outcome> DeleteSchoolAsync(DateTimeOffset now, CancellationToken cancellationToken)
	{
		var schoolId = tenant.TenantId;

		// Lock the subscription row for the whole deletion and check again under the lock. A school
		// that resubscribes while this runs then either wins (no deletion) or waits for the lock
		// (Stripe webhooks touch this row) — files and logins are never deleted for a paying school.
		// The transaction is disposed without commit on every early return, which releases the lock.
		await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
		var locked = await db.Subscriptions
			.FromSqlInterpolated($"SELECT * FROM \"Subscriptions\" WHERE \"SchoolId\" = {schoolId} FOR UPDATE")
			.AsNoTracking()
			.FirstOrDefaultAsync(cancellationToken);
		if (locked is not { CanceledAt: { } canceledAt, DeletionWarningSentAt: { } warnedAt }
			|| !IsDeletionDue(canceledAt, warnedAt, now))
		{
			logger.LogInformation("School {SchoolId} is no longer due for deletion; skipped", schoolId);
			return Outcome.NotDue;
		}

		// 1. Files. A failure stops here so the database still knows what to clean up next pass.
		try
		{
			foreach (var prefix in StoragePrefixes(schoolId))
			{
				await storage.DeleteByPrefixAsync(prefix, cancellationToken);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogError(ex, "Could not delete files for school {SchoolId}; data kept until next pass", schoolId);
			return Outcome.StorageFailed;
		}

		// 2. Login accounts. Same rule: the subjects are only known while the rows exist.
		try
		{
			foreach (var subject in await LoginSubjectsAsync(cancellationToken))
			{
				await keycloak.DeleteUserIfExistsAsync(subject, cancellationToken);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogError(ex, "Could not delete login accounts for school {SchoolId}; data kept until next pass", schoolId);
			return Outcome.AccountsFailed;
		}

		// 3. Database, all or nothing, in the transaction that holds the lock.
		foreach (var entityType in DeletionOrder(db.Model))
		{
			await (Task)DeleteRowsMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [schoolId, cancellationToken])!;
		}

		await db.SubscriptionModuleItems
			.Where(m => db.Subscriptions.Any(s => s.Id == m.SubscriptionId && s.SchoolId == schoolId))
			.ExecuteDeleteAsync(cancellationToken);
		await db.Subscriptions.Where(s => s.SchoolId == schoolId).ExecuteDeleteAsync(cancellationToken);
		var schoolName = await db.Schools.IgnoreQueryFilters().Where(s => s.Id == schoolId).Select(s => s.Name).SingleAsync(cancellationToken);
		await db.Schools.IgnoreQueryFilters().Where(s => s.Id == schoolId).ExecuteDeleteAsync(cancellationToken);

		// Same transaction as the delete: the backup console must know about every school a restore
		// would bring back, and never about one that wasn't deleted.
		db.SchoolDeletionRecords.Add(new SchoolDeletionRecord
		{
			Id = Guid.NewGuid(),
			SchoolId = schoolId,
			SchoolName = schoolName,
			DeletedAt = now.ToUniversalTime(),
		});
		await db.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);

		logger.LogInformation("Deleted school {SchoolId} and all its data at {DeletedAt:O}", schoolId, DateTimeOffset.UtcNow);
		return Outcome.Deleted;
	}

	private async Task<List<string>> LoginSubjectsAsync(CancellationToken cancellationToken)
	{
		// IgnoreQueryFilters only to include archived rows; the explicit TenantId keeps it to this school.
		var schoolId = tenant.TenantId;
		var staff = await db.Staff.IgnoreQueryFilters()
			.Where(s => s.TenantId == schoolId && s.KeycloakSubject != null)
			.Select(s => s.KeycloakSubject!).ToListAsync(cancellationToken);
		var parents = await db.Parents.IgnoreQueryFilters()
			.Where(p => p.TenantId == schoolId && p.KeycloakSubject != null)
			.Select(p => p.KeycloakSubject!).ToListAsync(cancellationToken);
		var board = await db.BoardMembers.IgnoreQueryFilters()
			.Where(b => b.TenantId == schoolId && b.KeycloakSubject != null)
			.Select(b => b.KeycloakSubject!).ToListAsync(cancellationToken);
		return [.. staff.Concat(parents).Concat(board).Distinct()];
	}

	private static readonly System.Reflection.MethodInfo DeleteRowsMethod =
		typeof(SchoolDeletionService).GetMethod(nameof(DeleteRowsAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

	// IgnoreQueryFilters so archived rows go too; the explicit TenantId predicate keeps every
	// statement inside this one school. Isolation is covered by SchoolRetentionTests.
	private Task<int> DeleteRowsAsync<TEntity>(Guid schoolId, CancellationToken cancellationToken)
		where TEntity : class, ITenantScoped =>
		db.Set<TEntity>().IgnoreQueryFilters().Where(e => e.TenantId == schoolId).ExecuteDeleteAsync(cancellationToken);

	/// <summary>
	/// Every tenant-scoped entity except <see cref="School"/>, ordered so rows that reference another
	/// table are deleted before the rows they reference. Entities without a TenantId must be removed
	/// by a cascading foreign key to a tenant-scoped entity, or the school would leave rows behind —
	/// that is checked here so a new entity fails the retention tests instead of leaking data.
	/// </summary>
	public static List<IEntityType> DeletionOrder(IModel model)
	{
		static bool IsTenantScoped(IEntityType t) => typeof(ITenantScoped).IsAssignableFrom(t.ClrType);

		foreach (var entityType in model.GetEntityTypes().Where(t => !IsTenantScoped(t)))
		{
			// SchoolDeletionRecord outlives the school on purpose; PruneDeletionRecordsAsync removes it.
			var handledExplicitly = entityType.ClrType == typeof(Subscription)
				|| entityType.ClrType == typeof(SubscriptionModuleItem)
				|| entityType.ClrType == typeof(SchoolDeletionRecord);
			var cascades = entityType.GetForeignKeys().Any(fk =>
				fk.IsRequired && fk.DeleteBehavior == DeleteBehavior.Cascade && IsTenantScoped(fk.PrincipalEntityType));
			if (!handledExplicitly && !cascades)
			{
				throw new InvalidOperationException(
					$"{entityType.DisplayName()} has no TenantId and no cascading foreign key to a tenant-scoped entity, so school deletion would leave it behind.");
			}
		}

		var order = new List<IEntityType>();
		var done = new HashSet<IEntityType>();
		var visiting = new HashSet<IEntityType>();

		void Visit(IEntityType entityType)
		{
			if (done.Contains(entityType))
			{
				return;
			}

			if (!visiting.Add(entityType))
			{
				throw new InvalidOperationException($"Foreign key cycle through {entityType.DisplayName()}; give school deletion an explicit order.");
			}

			foreach (var dependent in entityType.GetReferencingForeignKeys().Select(fk => fk.DeclaringEntityType))
			{
				if (dependent != entityType && IsTenantScoped(dependent))
				{
					Visit(dependent);
				}
			}

			visiting.Remove(entityType);
			done.Add(entityType);
			order.Add(entityType);
		}

		foreach (var entityType in model.GetEntityTypes().Where(t => IsTenantScoped(t) && t.ClrType != typeof(School)))
		{
			Visit(entityType);
		}

		return order;
	}

	private async Task SendWarningAsync(DateTimeOffset deletionDate, CancellationToken cancellationToken)
	{
		// School's query filter cannot be translated (its TenantId is not mapped), so match on Id.
		var school = await db.Schools.AsNoTracking().IgnoreQueryFilters().FirstAsync(s => s.Id == tenant.TenantId, cancellationToken);
		var adminEmails = await db.Staff.AsNoTracking()
			.Where(s => s.IsAdmin && s.Email != null)
			.Select(s => s.Email!)
			.ToListAsync(cancellationToken);
		var recipients = adminEmails.Prepend(school.ContactEmail)
			.Where(e => !string.IsNullOrWhiteSpace(e))
			.Select(e => e!.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (recipients.Count == 0)
		{
			// Nobody to warn. Still counts as sent: the alternative is keeping the data forever.
			// Logged as an error so someone can reach the school by other means before deletion.
			logger.LogError("School {SchoolId} has no admin email; deletion warning not delivered", school.Id);
			return;
		}

		var (subject, html, text) = DeletionWarningEmail(school.Name, deletionDate, appOptions.Value.SanitizedBaseUrl);
		foreach (var recipient in recipients)
		{
			await email.SendAsync(new EmailMessage(recipient, subject, html, text), cancellationToken);
		}
	}

	public static (string Subject, string Html, string Text) DeletionWarningEmail(string schoolName, DateTimeOffset deletionDate, string baseUrl)
	{
		var date = SchoolDayCalendar.DanishDate(deletionDate).ToString("d. MMMM yyyy", Danish);
		var name = WebUtility.HtmlEncode(schoolName);
		var exportUrl = $"{baseUrl}/eksporter";
		const string Subject = "Skolens data slettes om 7 dage";

		var html = EmailTemplate.Wrap(Subject, $"""
			<h1>{name}s data slettes {date}</h1>
			<p>Skolens abonnement på Skoleoverblikket er opsagt. Som beskrevet i vores privatlivspolitik
			sletter vi derfor alle skolens data permanent {date}: skemaer, klasser, medarbejdere, elever,
			forældre, beskeder, fravær og uploadede filer. Sletningen kan ikke fortrydes.</p>
			<p>Log ind og hent det, I vil gemme, inden da.</p>
			<div class="btn-wrapper"><a class="btn" href="{exportUrl}">Log ind og eksporter data</a></div>
			<p>Vil I fortsætte med Skoleoverblikket, kan I forny abonnementet under Abonnement, så slettes intet.</p>
			<p class="notice">Spørgsmål? Skriv til <a href="mailto:kontakt@skoleoverblikket.dk">kontakt@skoleoverblikket.dk</a>.</p>
			""");

		var text = $"""
			{schoolName}s data slettes {date}

			Skolens abonnement på Skoleoverblikket er opsagt. Som beskrevet i vores privatlivspolitik sletter vi derfor alle skolens data permanent {date}. Sletningen kan ikke fortrydes.

			Log ind og hent det, I vil gemme, inden da: {exportUrl}

			Vil I fortsætte, kan I forny abonnementet under Abonnement, så slettes intet.

			Spørgsmål? Skriv til kontakt@skoleoverblikket.dk.
			""";

		return (Subject, html, text);
	}

	private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
