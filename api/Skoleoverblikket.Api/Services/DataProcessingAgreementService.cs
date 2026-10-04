using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Email;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

public sealed record DataProcessingAgreementStatusDto(
	string CurrentVersion,
	bool AcceptedCurrentVersion,
	string? AcceptedVersion,
	DateTimeOffset? AcceptedAt,
	string? AcceptedByName);

public sealed record AcceptDataProcessingAgreementRequest([Required] string Version);

public sealed record SubProcessorNoticeRequest(
	[Required][StringLength(2000, MinimumLength = 1)] string Change,
	DateOnly EffectiveFrom);

public sealed record SubProcessorNoticeResultDto(int SchoolCount, int RecipientCount);

/// <summary>
/// The databehandleraftale (GDPR Art. 28 processor agreement) every school accepts, and the notice
/// admins get before the sub-processor list changes. The agreement text itself lives on the public
/// page /databehandleraftale; bump <see cref="CurrentVersion"/> together with that page whenever
/// the text changes, and every school is asked to accept again.
/// </summary>
public sealed class DataProcessingAgreementService(
	AppDbContext db,
	ITenantContext tenant,
	IEmailSender email,
	IOptions<SmtpOptions> smtpOptions,
	IOptions<ApplicationOptions> appOptions,
	ILogger<DataProcessingAgreementService> logger)
{
	/// <summary>Must match the version shown on DataProcessingAgreementPage.tsx.</summary>
	public const string CurrentVersion = "1.0";

	/// <summary>The agreement promises schools this much notice before a new sub-processor is used.</summary>
	public static readonly TimeSpan SubProcessorNotice = TimeSpan.FromDays(30);

	public enum AcceptOutcome
	{
		Accepted,
		WrongVersion,
		NotStaff,
	}

	public enum NoticeOutcome
	{
		Sent,
		TooSoon,
	}

	public async Task<DataProcessingAgreementStatusDto> GetStatusAsync(CancellationToken cancellationToken)
	{
		var latest = await db.DataProcessingAgreementAcceptances.AsNoTracking()
			.OrderByDescending(a => a.AcceptedAt)
			.Select(a => new { a.Version, a.AcceptedAt, a.AcceptedByName })
			.FirstOrDefaultAsync(cancellationToken);

		return new DataProcessingAgreementStatusDto(
			CurrentVersion,
			await db.DataProcessingAgreementAcceptances.AnyAsync(a => a.Version == CurrentVersion, cancellationToken),
			latest?.Version,
			latest?.AcceptedAt,
			latest?.AcceptedByName);
	}

	/// <summary>
	/// Records that the calling admin accepted <paramref name="version"/> on behalf of the school.
	/// The client sends the version it showed, so nobody accepts text they did not see.
	/// </summary>
	public async Task<AcceptOutcome> AcceptAsync(string keycloakSubject, string version, CancellationToken cancellationToken)
	{
		if (version != CurrentVersion)
		{
			return AcceptOutcome.WrongVersion;
		}

		var staff = await db.Staff.AsNoTracking()
			.Where(s => s.KeycloakSubject == keycloakSubject)
			.Select(s => new { s.Name, s.Email })
			.FirstOrDefaultAsync(cancellationToken);
		if (staff is null)
		{
			return AcceptOutcome.NotStaff;
		}

		if (await db.DataProcessingAgreementAcceptances.AnyAsync(a => a.Version == version, cancellationToken))
		{
			return AcceptOutcome.Accepted;
		}

		db.DataProcessingAgreementAcceptances.Add(NewAcceptance(tenant.TenantId, keycloakSubject, staff.Name, staff.Email));
		try
		{
			await db.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateException)
		{
			// Two admins (or a double click) accepted at the same time; one row is enough.
			db.ChangeTracker.Clear();
			if (!await db.DataProcessingAgreementAcceptances.AnyAsync(a => a.Version == version, cancellationToken))
			{
				throw;
			}
		}

		return AcceptOutcome.Accepted;
	}

	/// <summary>
	/// Adds the acceptance given on the signup form to the school being created. Takes the school id
	/// because the school does not exist yet, so there is no tenant context; the caller saves.
	/// </summary>
	public void AddSignupAcceptance(Guid schoolId, string keycloakSubject, string name, string email) =>
		db.DataProcessingAgreementAcceptances.Add(NewAcceptance(schoolId, keycloakSubject, name, email));

	/// <summary>
	/// Emails the admins of every school about a change to the sub-processor list. Superadmin only.
	/// </summary>
	public async Task<(NoticeOutcome Outcome, SubProcessorNoticeResultDto? Result)> SendSubProcessorNoticeAsync(
		SubProcessorNoticeRequest request, DateTimeOffset now, CancellationToken cancellationToken)
	{
		if (request.EffectiveFrom < SchoolDayCalendar.DanishDate(now + SubProcessorNotice))
		{
			return (NoticeOutcome.TooSoon, null);
		}

		// Cross-tenant on purpose: the notice goes to every school. Only addresses are read.
		var schoolEmails = await db.Schools.IgnoreQueryFilters().AsNoTracking()
			.Select(s => new { SchoolId = s.Id, Email = s.ContactEmail })
			.ToListAsync(cancellationToken);
		var adminEmails = await db.Staff.IgnoreQueryFilters().AsNoTracking()
			.Where(s => s.IsAdmin && s.Email != null)
			.Select(s => new { SchoolId = s.TenantId, s.Email })
			.ToListAsync(cancellationToken);

		var recipients = schoolEmails.Concat(adminEmails)
			.Where(r => !string.IsNullOrWhiteSpace(r.Email))
			.Select(r => r.Email!.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		var (subject, html) = SubProcessorNoticeEmail(request, appOptions.Value.SanitizedBaseUrl);

		// Bcc in batches so schools never see each other's addresses.
		// A failed batch aborts the run; resending then repeats the earlier batches. Acceptable for a
		// rare manual notice, so the log says how far it got instead of tracking delivery per batch.
		const int BatchSize = 50;
		var sent = 0;
		foreach (var batch in recipients.Chunk(BatchSize))
		{
			try
			{
				await email.SendAsync(new EmailMessage(smtpOptions.Value.FromAddress, subject, html, Bcc: batch), cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				logger.LogError(ex, "Sub-processor notice stopped after {Sent} of {Total} recipient(s)", sent, recipients.Count);
				throw;
			}

			sent += batch.Length;
		}

		logger.LogInformation("Sent sub-processor notice to {RecipientCount} admin(s) at {SchoolCount} school(s)", recipients.Count, schoolEmails.Count);
		return (NoticeOutcome.Sent, new SubProcessorNoticeResultDto(schoolEmails.Count, recipients.Count));
	}

	public static (string Subject, string Html) SubProcessorNoticeEmail(SubProcessorNoticeRequest request, string baseUrl)
	{
		var date = request.EffectiveFrom.ToString("d. MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("da-DK"));
		var change = WebUtility.HtmlEncode(request.Change).Replace("\n", "<br>", StringComparison.Ordinal);
		const string Subject = "Ændring af underdatabehandlere";

		var html = EmailTemplate.Wrap(Subject, $"""
			<h1>Vi ændrer vores underdatabehandlere {date}</h1>
			<p>Som aftalt i databehandleraftalen giver vi skolen besked mindst 30 dage før, vi tager en ny
			underdatabehandler i brug eller udskifter en eksisterende.</p>
			<p><strong>Ændringen:</strong><br>{change}</p>
			<p>Listen over underdatabehandlere på <a href="{baseUrl}/underdatabehandlere">{baseUrl}/underdatabehandlere</a>
			bliver opdateret {date}, når ændringen træder i kraft.</p>
			<p>Har skolen indsigelser mod ændringen, så skriv til os inden {date}.</p>
			<p class="notice">Skriv til <a href="mailto:kontakt@skoleoverblikket.dk">kontakt@skoleoverblikket.dk</a>.</p>
			""");

		return (Subject, html);
	}

	private static DataProcessingAgreementAcceptance NewAcceptance(Guid schoolId, string keycloakSubject, string name, string? email) => new()
	{
		Id = Guid.NewGuid(),
		TenantId = schoolId,
		Version = CurrentVersion,
		AcceptedBySubject = keycloakSubject,
		AcceptedByName = name,
		AcceptedByEmail = email,
		AcceptedAt = DateTimeOffset.UtcNow,
	};
}
