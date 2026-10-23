using System.Text.Encodings.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Email;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

public sealed class NotificationService(AppDbContext db, ITenantContext tenantContext, NotificationEmailQueue emailQueue, IOptions<ApplicationOptions> appOptions, IOptions<SmtpOptions> smtpOptions) : INotificationService
{
	public async Task CreateAsync(
		Guid recipientId,
		RecipientType recipientType,
		NotificationType type,
		Guid? referenceId,
		string body,
		CancellationToken cancellationToken)
	{
		// 1. Check preference — find by (UserId==recipientId && UserType==recipientType && Type==type)
		//    If not found, default InApp=true, Email=true
		var pref = await db.NotificationPreferences
			.AsNoTracking()
			.FirstOrDefaultAsync(p => p.UserId == recipientId && p.UserType == recipientType && p.Type == type, cancellationToken);
		bool inApp = pref?.InApp ?? NotificationDefaults.InAppEnabledByDefault(type);
		bool emailEnabled = pref?.Email ?? NotificationDefaults.EmailEnabledByDefault(type);

		// 2. If inApp: insert Notification row
		if (inApp)
		{
			db.Notifications.Add(new Notification
			{
				Id = Guid.NewGuid(),
				TenantId = tenantContext.TenantId,
				RecipientId = recipientId,
				RecipientType = recipientType,
				Type = type,
				ReferenceId = referenceId,
				Body = body,
				CreatedAt = DateTimeOffset.UtcNow,
			});
			await db.SaveChangesAsync(cancellationToken);
		}

		// 3. If email: get recipient email
		if (emailEnabled)
		{
			string? recipientEmail = recipientType == RecipientType.Parent
				? await db.Parents.AsNoTracking().Where(p => p.Id == recipientId).Select(p => p.Email).FirstOrDefaultAsync(cancellationToken)
				: await db.Staff.AsNoTracking().Where(s => s.Id == recipientId).Select(s => s.Email).FirstOrDefaultAsync(cancellationToken);

			if (!string.IsNullOrEmpty(recipientEmail))
			{
				var baseUrl = appOptions.Value.SanitizedBaseUrl;
				var settingsUrl = $"{baseUrl}/indstillinger/notifikationer";
				emailQueue.Enqueue(new EmailMessage(
					To: recipientEmail,
					Subject: body,
					HtmlBody: BuildHtmlEmail(body, settingsUrl),
					PlainTextBody: BuildPlainEmail(body, settingsUrl)
				));
			}
		}
	}

	public async Task CreateBatchAsync(
		IEnumerable<NotificationRequest> requests,
		CancellationToken cancellationToken)
	{
		var list = requests.ToList();
		if (list.Count == 0)
		{
			return;
		}

		var types = list.Select(r => r.Type).Distinct().ToList();
		var recipientIds = list.Select(r => r.RecipientId).Distinct().ToList();

		var prefs = await db.NotificationPreferences
			.AsNoTracking()
			.Where(p => recipientIds.Contains(p.UserId) && types.Contains(p.Type))
			.ToListAsync(cancellationToken);

		var prefLookup = prefs.ToDictionary(p => (p.UserId, p.UserType, p.Type));

		var now = DateTimeOffset.UtcNow;
		var emailRequests = new List<NotificationRequest>();

		foreach (var req in list)
		{
			prefLookup.TryGetValue((req.RecipientId, req.RecipientType, req.Type), out var pref);
			bool inApp = pref?.InApp ?? NotificationDefaults.InAppEnabledByDefault(req.Type);
			bool emailEnabled = pref?.Email ?? NotificationDefaults.EmailEnabledByDefault(req.Type);

			if (inApp)
			{
				db.Notifications.Add(new Notification
				{
					Id = Guid.NewGuid(),
					TenantId = tenantContext.TenantId,
					RecipientId = req.RecipientId,
					RecipientType = req.RecipientType,
					Type = req.Type,
					ReferenceId = req.ReferenceId,
					Body = req.Body,
					CreatedAt = now,
				});
			}

			if (emailEnabled)
			{
				emailRequests.Add(req);
			}
		}

		await db.SaveChangesAsync(cancellationToken);

		if (emailRequests.Count > 0)
		{
			var baseUrl = appOptions.Value.SanitizedBaseUrl;
			var settingsUrl = $"{baseUrl}/indstillinger/notifikationer";

			var parentIds = emailRequests.Where(r => r.RecipientType == RecipientType.Parent).Select(r => r.RecipientId).Distinct().ToList();
			var staffAndBoardIds = emailRequests.Where(r => r.RecipientType == RecipientType.Staff || r.RecipientType == RecipientType.Board).Select(r => r.RecipientId).Distinct().ToList();

			var parentEmails = parentIds.Count > 0
				? await db.Parents.AsNoTracking().Where(p => parentIds.Contains(p.Id) && p.Email != null)
					.ToDictionaryAsync(p => p.Id, p => p.Email!, cancellationToken)
				: new Dictionary<Guid, string>();

			var staffEmails = staffAndBoardIds.Count > 0
				? await db.Staff.AsNoTracking().Where(s => staffAndBoardIds.Contains(s.Id) && s.Email != null)
					.ToDictionaryAsync(s => s.Id, s => s.Email!, cancellationToken)
				: new Dictionary<Guid, string>();

			var recipientsByBody = emailRequests
				.Select(req => (req.Body, Email: req.RecipientType == RecipientType.Parent
					? parentEmails.GetValueOrDefault(req.RecipientId)
					: staffEmails.GetValueOrDefault(req.RecipientId)))
				.Where(r => !string.IsNullOrWhiteSpace(r.Email))
				.GroupBy(r => r.Body, r => r.Email!.Trim());

			// The e-mail has nothing per recipient, so everyone with the same text shares one message in Bcc
			// batches: a notification to every parent is a handful of SMTP sends instead of one per parent.
			foreach (var group in recipientsByBody)
			{
				foreach (var batch in group.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(smtpOptions.Value.BccBatchSize))
				{
					emailQueue.Enqueue(new EmailMessage(
						To: smtpOptions.Value.FromAddress,
						Subject: group.Key,
						HtmlBody: BuildHtmlEmail(group.Key, settingsUrl),
						PlainTextBody: BuildPlainEmail(group.Key, settingsUrl),
						Bcc: batch
					));
				}
			}
		}
	}

	private static string BuildHtmlEmail(string body, string settingsUrl)
	{
		var encodedBody = HtmlEncoder.Default.Encode(body);
		var encodedSettingsUrl = HtmlEncoder.Default.Encode(settingsUrl);

		return EmailTemplate.Wrap("Notifikation", $"""
			<p>{encodedBody}</p>
			<div class="notice">
			  <p style="margin-bottom:0;">Du modtager denne e-mail, fordi du har slået e-mailnotifikationer til.
			  <a href="{encodedSettingsUrl}" style="color:#1f6321;">Administrér notifikationer</a></p>
			</div>
			""");
	}

	private static string BuildPlainEmail(string body, string settingsUrl) =>
		$"{body}\n\n---\nDu modtager denne e-mail, fordi du har slået e-mailnotifikationer til.\nAdministrér notifikationer: {settingsUrl}\n";
}
