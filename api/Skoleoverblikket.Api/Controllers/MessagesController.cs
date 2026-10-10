using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Email;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Tenancy;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using ZiggyCreatures.Caching.Fusion;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/messages")]
[Authorize]
[RequiresModule(SubscriptionModule.ParentModule)]
public sealed class MessagesController(
	AppDbContext db,
	ITenantContext tenantContext,
	INotificationService notificationService,
	IAuthorizationService authz,
	IEmailSender emailSender,
	IOptions<SmtpOptions> smtpOptions,
	IOptions<ApplicationOptions> appOptions,
	ILogger<MessagesController> logger,
	IFusionCache cache) : ControllerBase
{
	public record InboxMessageDto(
		Guid Id,
		Guid SenderId,
		RecipientType SenderType,
		string SenderName,
		string Subject,
		string Body,
		DateTimeOffset SentAt,
		DateTimeOffset? ReadAt,
		Guid? InReplyToId,
		bool IsGroup = false);

	public record SentMessageDto(
		Guid Id,
		Guid RecipientId,
		RecipientType RecipientType,
		string RecipientName,
		string Subject,
		string Body,
		DateTimeOffset SentAt,
		DateTimeOffset? ReadAt,
		bool IsGroup = false,
		string? AudienceLabel = null,
		int? GroupRecipientCount = null,
		Guid? InReplyToId = null);

	public record SendMessageRequest(
		Guid RecipientId,
		RecipientType RecipientType,
		string Subject,
		string Body,
		Guid? InReplyToId = null);

	public record ThreadMessageDto(
		Guid Id,
		Guid SenderId,
		RecipientType SenderType,
		string SenderName,
		Guid RecipientId,
		RecipientType RecipientType,
		string RecipientName,
		string Body,
		DateTimeOffset SentAt,
		bool IsOwn,
		Guid? InReplyToId = null);

	public record RecipientDto(
		Guid Id,
		string Name,
		RecipientType Type,
		string? AvatarUrl);

	public record GroupPreviewRequest(
		BroadcastAudience Audience,
		Guid? ClassId,
		StaffRole? StaffRole);

	public record GroupPreviewDto(int RecipientCount);

	public record SendGroupMessageRequest(
		BroadcastAudience Audience,
		Guid? ClassId,
		StaffRole? StaffRole,
		[Required, MaxLength(200)] string Subject,
		[Required, MaxLength(10000)] string Body);

	private async Task<(Guid Id, string Name, RecipientType Type)?> ResolveCallerAsync(CancellationToken cancellationToken)
	{
		var sub = User.GetKeycloakSubject();

		if (User.IsInRole(Roles.Parent))
		{
			var parent = await db.Parents
				.AsNoTracking()
				.FirstOrDefaultAsync(p => p.KeycloakSubject == sub, cancellationToken);

			return parent is null
				? null
				: (parent.Id, parent.Name, RecipientType.Parent);
		}

		var staff = await db.Staff
			.AsNoTracking()
			.FirstOrDefaultAsync(s => s.KeycloakSubject == sub, cancellationToken);

		return staff is null
			? null
			: (staff.Id, staff.Name, RecipientType.Staff);
	}

	[HttpGet("inbox")]
	public async Task<ActionResult<IReadOnlyList<InboxMessageDto>>> GetInbox(CancellationToken cancellationToken)
	{
		var caller = await ResolveCallerAsync(cancellationToken);
		if (caller is null)
		{
			return Forbid();
		}

		var (callerId, _, _) = caller.Value;

		var messages = await db.Messages
			.AsNoTracking()
			.Where(m => m.RecipientId == callerId)
			.OrderByDescending(m => m.SentAt)
			.Take(50)
			.ToListAsync(cancellationToken);

		var parentIds = messages.Where(m => m.SenderType == RecipientType.Parent).Select(m => m.SenderId).Distinct().ToList();
		var staffIds = messages.Where(m => m.SenderType == RecipientType.Staff).Select(m => m.SenderId).Distinct().ToList();

		var parents = await db.Parents.AsNoTracking()
			.Where(p => parentIds.Contains(p.Id))
			.Select(p => new { p.Id, p.Name })
			.ToListAsync(cancellationToken);

		var staffMembers = await db.Staff.AsNoTracking()
			.Where(s => staffIds.Contains(s.Id))
			.Select(s => new { s.Id, s.Name })
			.ToListAsync(cancellationToken);

		var parentMap = parents.ToDictionary(p => p.Id, p => p.Name);
		var staffMap = staffMembers.ToDictionary(s => s.Id, s => s.Name);

		var dtos = messages.Select(m =>
		{
			var senderName = m.SenderType == RecipientType.Parent
				? parentMap.GetValueOrDefault(m.SenderId, "Forælder")
				: staffMap.GetValueOrDefault(m.SenderId, "Medarbejder");
			return new InboxMessageDto(m.Id, m.SenderId, m.SenderType, senderName, m.Subject, m.Body, m.SentAt, m.ReadAt, m.InReplyToId, IsGroup: m.GroupMessageId != null);
		}).ToList();

		return Ok(dtos);
	}

	[HttpGet("sent")]
	public async Task<ActionResult<IReadOnlyList<SentMessageDto>>> GetSent(CancellationToken cancellationToken)
	{
		var caller = await ResolveCallerAsync(cancellationToken);
		if (caller is null)
		{
			return Forbid();
		}

		var (callerId, _, _) = caller.Value;

		// Group fan-out writes one Message row per recipient, so dedupe by
		// GroupMessageId (falling back to the row's own Id for non-group
		// messages) before paging — otherwise a large group send fills the
		// Take(50) window with its own fan-out rows and crowds out older
		// individual sent messages. DISTINCT ON pushes the dedupe into
		// Postgres itself instead of a bounded client-side scan.
		var messages = await db.Messages
			.FromSqlInterpolated($"""
				SELECT DISTINCT ON (COALESCE("GroupMessageId", "Id")) *
				FROM "Messages"
				WHERE "SenderId" = {callerId}
				ORDER BY COALESCE("GroupMessageId", "Id"), "Id"
				""")
			.AsNoTracking()
			.OrderByDescending(m => m.SentAt)
			.Take(50)
			.ToListAsync(cancellationToken);

		// Resolve individual recipient names
		var parentIds = messages.Where(m => m.RecipientType == RecipientType.Parent && m.GroupMessageId == null).Select(m => m.RecipientId).Distinct().ToList();
		var staffIds = messages.Where(m => m.RecipientType == RecipientType.Staff && m.GroupMessageId == null).Select(m => m.RecipientId).Distinct().ToList();

		var parents = await db.Parents.AsNoTracking()
			.Where(p => parentIds.Contains(p.Id))
			.Select(p => new { p.Id, p.Name })
			.ToListAsync(cancellationToken);

		var staffMembers = await db.Staff.AsNoTracking()
			.Where(s => staffIds.Contains(s.Id))
			.Select(s => new { s.Id, s.Name })
			.ToListAsync(cancellationToken);

		var parentMap = parents.ToDictionary(p => p.Id, p => p.Name);
		var staffMap = staffMembers.ToDictionary(s => s.Id, s => s.Name);

		// Load GroupMessages for fan-out rows
		var groupMessageIds = messages
			.Where(m => m.GroupMessageId != null)
			.Select(m => m.GroupMessageId!.Value)
			.Distinct()
			.ToList();

		var groupMessages = groupMessageIds.Count > 0
			? await db.GroupMessages.AsNoTracking()
				.Where(g => groupMessageIds.Contains(g.Id))
				.ToListAsync(cancellationToken)
			: [];

		// Load class names for group messages with ClassParents audience
		var classIds = groupMessages
			.Where(g => g.ClassId.HasValue)
			.Select(g => g.ClassId!.Value)
			.Distinct()
			.ToList();

		var classNames = classIds.Count > 0
			? await db.Classes.AsNoTracking()
				.Where(c => classIds.Contains(c.Id))
				.ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
			: new Dictionary<Guid, string>();

		var groupMessageMap = groupMessages.ToDictionary(g => g.Id);

		// Collapse group messages: one entry per GroupMessageId
		var seen = new HashSet<Guid>();
		var dtos = new List<SentMessageDto>();

		foreach (var message in messages)
		{
			if (message.GroupMessageId is not null)
			{
				if (!seen.Add(message.GroupMessageId.Value))
				{
					continue;
				}

				if (!groupMessageMap.TryGetValue(message.GroupMessageId.Value, out var gm))
				{
					logger.LogWarning("GetSent: GroupMessage {GroupMessageId} not found for Message {MessageId} — dropping row from Sent list", message.GroupMessageId, message.Id);
					continue;
				}

				var audienceLabel = BuildAudienceLabel(gm, classNames);
				dtos.Add(new SentMessageDto(
					message.Id,
					message.RecipientId,
					message.RecipientType,
					audienceLabel,
					message.Subject,
					message.Body,
					message.SentAt,
					message.ReadAt,
					IsGroup: true,
					AudienceLabel: audienceLabel,
					GroupRecipientCount: gm.RecipientCount));
			}
			else
			{
				var recipientName = message.RecipientType == RecipientType.Parent
					? parentMap.GetValueOrDefault(message.RecipientId, "Forælder")
					: staffMap.GetValueOrDefault(message.RecipientId, "Medarbejder");
				dtos.Add(new SentMessageDto(message.Id, message.RecipientId, message.RecipientType, recipientName, message.Subject, message.Body, message.SentAt, message.ReadAt, InReplyToId: message.InReplyToId));
			}
		}

		return Ok(dtos);
	}

	[HttpGet("{id:guid}/thread")]
	public async Task<ActionResult<IReadOnlyList<ThreadMessageDto>>> GetThread(Guid id, CancellationToken cancellationToken)
	{
		var caller = await ResolveCallerAsync(cancellationToken);
		if (caller is null)
		{
			return Forbid();
		}

		var (callerId, _, _) = caller.Value;

		var anchor = await db.Messages
			.AsNoTracking()
			.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

		if (anchor is null)
		{
			return Problem(statusCode: StatusCodes.Status404NotFound);
		}

		if (anchor.SenderId != callerId && anchor.RecipientId != callerId)
		{
			return Problem(statusCode: StatusCodes.Status403Forbidden);
		}

		// Find the root of the reply chain, then pull every descendant of that root via a
		// recursive CTE — a plain child-walk can only ever follow one InReplyToId per level,
		// so a thread with more than one reply to the same message would silently drop branches.
		var rootId = anchor.Id;
		var current = anchor;
		while (current.InReplyToId.HasValue)
		{
			var parent = await db.Messages
				.AsNoTracking()
				.FirstOrDefaultAsync(m => m.Id == current.InReplyToId.Value, cancellationToken);

			if (parent is null)
			{
				break;
			}

			rootId = parent.Id;
			current = parent;
		}

		var tenantId = tenantContext.TenantId;
		var chain = await db.Messages
			.FromSqlInterpolated($"""
				WITH RECURSIVE thread AS (
					SELECT * FROM "Messages" WHERE "Id" = {rootId} AND "TenantId" = {tenantId}
					UNION ALL
					SELECT m.* FROM "Messages" m
					INNER JOIN thread t ON m."InReplyToId" = t."Id"
					WHERE m."TenantId" = {tenantId}
				)
				SELECT * FROM thread
				""")
			.AsNoTracking()
			.OrderBy(m => m.SentAt)
			.ThenBy(m => m.Id)
			.ToListAsync(cancellationToken);

		var parentIds = chain.Where(m => m.SenderType == RecipientType.Parent).Select(m => m.SenderId)
			.Concat(chain.Where(m => m.RecipientType == RecipientType.Parent).Select(m => m.RecipientId))
			.Distinct().ToList();
		var staffIds = chain.Where(m => m.SenderType == RecipientType.Staff).Select(m => m.SenderId)
			.Concat(chain.Where(m => m.RecipientType == RecipientType.Staff).Select(m => m.RecipientId))
			.Distinct().ToList();

		var parents = await db.Parents.AsNoTracking()
			.Where(p => parentIds.Contains(p.Id))
			.Select(p => new { p.Id, p.Name })
			.ToListAsync(cancellationToken);

		var staffMembers = await db.Staff.AsNoTracking()
			.Where(s => staffIds.Contains(s.Id))
			.Select(s => new { s.Id, s.Name })
			.ToListAsync(cancellationToken);

		var parentMap = parents.ToDictionary(p => p.Id, p => p.Name);
		var staffMap = staffMembers.ToDictionary(s => s.Id, s => s.Name);

		string NameOf(Guid personId, RecipientType type) =>
			type == RecipientType.Parent
				? parentMap.GetValueOrDefault(personId, "Forælder")
				: staffMap.GetValueOrDefault(personId, "Medarbejder");

		var dtos = chain.Select(m => new ThreadMessageDto(
			m.Id,
			m.SenderId,
			m.SenderType,
			NameOf(m.SenderId, m.SenderType),
			m.RecipientId,
			m.RecipientType,
			NameOf(m.RecipientId, m.RecipientType),
			m.Body,
			m.SentAt,
			IsOwn: m.SenderId == callerId,
			InReplyToId: m.InReplyToId)).ToList();

		return Ok(dtos);
	}

	private static string BuildAudienceLabel(GroupMessage gm, Dictionary<Guid, string> classNames)
	{
		return gm.Audience switch
		{
			BroadcastAudience.AllParents => "Alle forældre",
			BroadcastAudience.SfoParents => "SFO-forældre",
			BroadcastAudience.AllStaff => "Alt personale",
			BroadcastAudience.StaffByRole => gm.StaffRole switch
			{
				StaffRole.Teacher => "Lærere",
				StaffRole.Aide => "Pædagoger",
				StaffRole.Substitute => "Vikarer",
				_ => "Personale",
			},
			BroadcastAudience.ClassParents when gm.ClassId.HasValue =>
				$"Forældre i {classNames.GetValueOrDefault(gm.ClassId.Value, "klassen")}",
			_ => "Gruppe",
		};
	}

	[HttpPost]
	public async Task<IActionResult> SendMessage(
		[FromBody] SendMessageRequest req,
		CancellationToken cancellationToken)
	{
		var caller = await ResolveCallerAsync(cancellationToken);
		if (caller is null)
		{
			return Forbid();
		}

		var (callerId, callerName, callerType) = caller.Value;

		if (callerType == RecipientType.Parent && req.RecipientType == RecipientType.Parent)
		{
			var recipientConsents = await db.Parents
				.AnyAsync(p => p.Id == req.RecipientId && p.ShareContactInfo, cancellationToken);

			if (!recipientConsents)
			{
				return Forbid();
			}
		}

		if (req.InReplyToId.HasValue)
		{
			var parentMessage = await db.Messages
				.AsNoTracking()
				.FirstOrDefaultAsync(m => m.Id == req.InReplyToId.Value, cancellationToken);

			if (parentMessage is null || parentMessage.GroupMessageId is not null)
			{
				return Problem(statusCode: StatusCodes.Status404NotFound);
			}

			var callerIsParticipant = parentMessage.SenderId == callerId || parentMessage.RecipientId == callerId;
			if (!callerIsParticipant)
			{
				return Problem(statusCode: StatusCodes.Status403Forbidden);
			}

			var (expectedRecipientId, expectedRecipientType) = parentMessage.SenderId == callerId
				? (parentMessage.RecipientId, parentMessage.RecipientType)
				: (parentMessage.SenderId, parentMessage.SenderType);

			if (req.RecipientId != expectedRecipientId || req.RecipientType != expectedRecipientType)
			{
				return Problem(statusCode: StatusCodes.Status403Forbidden);
			}
		}

		var message = new Message
		{
			Id = Guid.NewGuid(),
			TenantId = tenantContext.TenantId,
			SenderId = callerId,
			SenderType = callerType,
			RecipientId = req.RecipientId,
			RecipientType = req.RecipientType,
			Subject = req.Subject,
			Body = req.Body,
			SentAt = DateTimeOffset.UtcNow,
			InReplyToId = req.InReplyToId,
		};

		db.Messages.Add(message);
		await db.SaveChangesAsync(cancellationToken);

		await notificationService.CreateAsync(
			req.RecipientId,
			req.RecipientType,
			NotificationType.NewMessage,
			message.Id,
			$"{callerName} har sendt dig en besked: {req.Subject}",
			cancellationToken);

		return Created(string.Empty, new { message.Id });
	}

	[HttpPost("{id:guid}/read")]
	public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
	{
		var caller = await ResolveCallerAsync(cancellationToken);
		if (caller is null)
		{
			return Forbid();
		}

		var (callerId, _, _) = caller.Value;

		var message = await db.Messages
			.FirstOrDefaultAsync(m => m.Id == id && m.RecipientId == callerId, cancellationToken);

		if (message is null)
		{
			return NotFound();
		}

		if (message.ReadAt is null)
		{
			message.ReadAt = DateTimeOffset.UtcNow;
			await db.SaveChangesAsync(cancellationToken);
		}

		return NoContent();
	}

	[HttpGet("recipients")]
	public async Task<ActionResult<IReadOnlyList<RecipientDto>>> GetRecipients(
		[FromQuery] string q = "",
		CancellationToken cancellationToken = default)
	{
		var caller = await ResolveCallerAsync(cancellationToken);
		if (caller is null)
		{
			return Forbid();
		}

		var (callerId, _, callerType) = caller.Value;

		var cacheKey = $"recipients:{tenantContext.TenantId}:{callerId}";
		var all = await cache.GetOrSetAsync(
			cacheKey,
			async token => await BuildAllRecipientsAsync(callerId, callerType, token),
			options => options.SetDuration(TimeSpan.FromSeconds(30)),
			cancellationToken);

		var filtered = string.IsNullOrEmpty(q)
			? all
			: all.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

		return Ok(filtered.Take(50).ToList());
	}

	[HttpPost("group/preview")]
	public async Task<ActionResult<GroupPreviewDto>> GroupPreview(
		[FromBody] GroupPreviewRequest req,
		CancellationToken cancellationToken)
	{
		var authReq = new GroupMessageRequest(req.Audience, req.ClassId, req.StaffRole);
		var authResult = await authz.AuthorizeAsync(User, authReq, Policies.SendGroupMessage);
		if (!authResult.Succeeded)
		{
			return Forbid();
		}

		var count = await ResolveRecipientCount(req.Audience, req.ClassId, req.StaffRole, cancellationToken);
		return Ok(new GroupPreviewDto(count));
	}

	[HttpPost("group")]
	public async Task<IActionResult> SendGroupMessage(
		[FromBody] SendGroupMessageRequest req,
		CancellationToken cancellationToken)
	{
		var caller = await ResolveCallerAsync(cancellationToken);
		if (caller is null)
		{
			return Forbid();
		}

		var (callerId, callerName, callerType) = caller.Value;

		var authReq = new GroupMessageRequest(req.Audience, req.ClassId, req.StaffRole);
		var authResult = await authz.AuthorizeAsync(User, authReq, Policies.SendGroupMessage);
		if (!authResult.Succeeded)
		{
			return Forbid();
		}

		var recipients = await ResolveRecipients(req.Audience, req.ClassId, req.StaffRole, cancellationToken);

		Guid? senderStaffId = callerType == RecipientType.Staff ? callerId : null;
		Guid? senderParentId = callerType == RecipientType.Parent ? callerId : null;

		var groupMessage = new GroupMessage
		{
			Id = Guid.NewGuid(),
			TenantId = tenantContext.TenantId,
			SenderStaffId = senderStaffId,
			SenderParentId = senderParentId,
			SenderName = callerName,
			Audience = req.Audience,
			ClassId = req.ClassId,
			StaffRole = req.StaffRole,
			Subject = req.Subject,
			Body = req.Body,
			RecipientCount = recipients.Count,
		};

		db.GroupMessages.Add(groupMessage);

		var now = DateTimeOffset.UtcNow;
		foreach (var recipient in recipients)
		{
			db.Messages.Add(new Message
			{
				Id = Guid.NewGuid(),
				TenantId = tenantContext.TenantId,
				SenderId = callerId,
				SenderType = callerType,
				RecipientId = recipient.Id,
				RecipientType = recipient.RecipientType,
				Subject = req.Subject,
				Body = req.Body,
				SentAt = now,
				GroupMessageId = groupMessage.Id,
			});
		}

		await db.SaveChangesAsync(cancellationToken);

		// Send BCC email batches
		var school = await db.Schools.AsNoTracking().IgnoreQueryFilters()
			.FirstOrDefaultAsync(s => s.Id == tenantContext.TenantId, cancellationToken);
		var schoolName = school?.Name ?? "Skoleoverblikket";
		var inboxUrl = $"{appOptions.Value.SanitizedBaseUrl}/beskeder";
		var settingsUrl = $"{appOptions.Value.SanitizedBaseUrl}/indstillinger/notifikationer";

		var emails = recipients.Where(r => !string.IsNullOrWhiteSpace(r.Email)).Select(r => r.Email!).ToList();
		if (emails.Count > 0)
		{
			try
			{
				var html = BuildHtml(callerName, req.Subject, req.Body, schoolName, inboxUrl, settingsUrl);
				const int batchSize = 50;
				for (int i = 0; i < emails.Count; i += batchSize)
				{
					var batch = emails.Skip(i).Take(batchSize).ToList();
					await emailSender.SendAsync(new EmailMessage(smtpOptions.Value.FromAddress, req.Subject, html, Bcc: batch), cancellationToken);
				}
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Failed to send group message emails");
			}
		}

		// In-app notifications
		var notificationRequests = recipients.Select(recipient => new NotificationRequest(
			recipient.Id,
			recipient.RecipientType,
			NotificationType.GroupMessage,
			groupMessage.Id,
			$"{callerName} har sendt dig en gruppebesked: {req.Subject}")).ToList();
		await notificationService.CreateBatchAsync(notificationRequests, cancellationToken);

		return Created(string.Empty, new { groupMessageId = groupMessage.Id, recipientCount = recipients.Count });
	}

	private sealed record RecipientInfo(Guid Id, RecipientType RecipientType, string Name, string? Email);

	private async Task<int> ResolveRecipientCount(
		BroadcastAudience audience,
		Guid? classId,
		StaffRole? staffRole,
		CancellationToken cancellationToken)
	{
		var recipients = await ResolveRecipients(audience, classId, staffRole, cancellationToken);
		return recipients.Count;
	}

	private async Task<List<RecipientInfo>> ResolveRecipients(
		BroadcastAudience audience,
		Guid? classId,
		StaffRole? staffRole,
		CancellationToken cancellationToken)
	{
		switch (audience)
		{
			case BroadcastAudience.AllParents:
				{
					var rows = await db.Parents.AsNoTracking()
						.Where(p => p.Email != null)
						.Select(p => new { p.Id, p.Name, p.Email })
						.ToListAsync(cancellationToken);
					return rows.Select(p => new RecipientInfo(p.Id, RecipientType.Parent, p.Name, p.Email)).ToList();
				}
			case BroadcastAudience.ClassParents when classId.HasValue:
				{
					var rows = await db.Students.AsNoTracking()
						.Where(s => s.ClassId == classId.Value)
						.SelectMany(s => s.Parents)
						.Where(p => p.Email != null)
						.Distinct()
						.Select(p => new { p.Id, p.Name, p.Email })
						.ToListAsync(cancellationToken);
					return rows.Select(p => new RecipientInfo(p.Id, RecipientType.Parent, p.Name, p.Email)).ToList();
				}
			case BroadcastAudience.SfoParents:
				{
					var rows = await db.Students.AsNoTracking()
						.Where(s => s.IsEnrolledInSfo)
						.SelectMany(s => s.Parents)
						.Where(p => p.Email != null)
						.Distinct()
						.Select(p => new { p.Id, p.Name, p.Email })
						.ToListAsync(cancellationToken);
					return rows.Select(p => new RecipientInfo(p.Id, RecipientType.Parent, p.Name, p.Email)).ToList();
				}
			case BroadcastAudience.AllStaff:
				{
					var rows = await db.Staff.AsNoTracking()
						.Where(s => s.Email != null)
						.Select(s => new { s.Id, s.Name, s.Email })
						.ToListAsync(cancellationToken);
					return rows.Select(s => new RecipientInfo(s.Id, RecipientType.Staff, s.Name, s.Email)).ToList();
				}
			case BroadcastAudience.StaffByRole when staffRole.HasValue:
				{
					var rows = await db.Staff.AsNoTracking()
						.Where(s => s.Role == staffRole.Value && s.Email != null)
						.Select(s => new { s.Id, s.Name, s.Email })
						.ToListAsync(cancellationToken);
					return rows.Select(s => new RecipientInfo(s.Id, RecipientType.Staff, s.Name, s.Email)).ToList();
				}
			default:
				return [];
		}
	}

	private static string BuildHtml(string senderName, string subject, string body, string schoolName, string inboxUrl, string settingsUrl)
	{
		var encodedSender = HtmlEncoder.Default.Encode(senderName);
		var encodedSubject = HtmlEncoder.Default.Encode(subject);
		var encodedSchoolName = HtmlEncoder.Default.Encode(schoolName);
		var encodedInboxUrl = HtmlEncoder.Default.Encode(inboxUrl);
		var encodedSettingsUrl = HtmlEncoder.Default.Encode(settingsUrl);

		var bodyParagraphs = new StringBuilder();
		foreach (var line in body.Split('\n'))
		{
			bodyParagraphs.Append("<p>");
			bodyParagraphs.Append(HtmlEncoder.Default.Encode(line));
			bodyParagraphs.Append("</p>");
		}

		return EmailTemplate.Wrap(encodedSubject, $"""
			<h1>{encodedSubject}</h1>
			<p style="margin-bottom:8px;">{encodedSender} har sendt en besked via {encodedSchoolName}:</p>
			{bodyParagraphs}
			<div class="btn-wrapper">
			  <a href="{encodedInboxUrl}" class="btn">Åbn besked</a>
			</div>
			<div class="notice">
			  <p style="margin-bottom:0;">Du modtager denne e-mail, fordi du har slået e-mailnotifikationer til.
			  <a href="{encodedSettingsUrl}" style="color:#1f6321;">Administrér notifikationer</a></p>
			</div>
			""");
	}

	private async Task<List<RecipientDto>> BuildAllRecipientsAsync(
		Guid callerId,
		RecipientType callerType,
		CancellationToken cancellationToken)
	{
		var results = new List<RecipientDto>();

		if (callerType == RecipientType.Parent)
		{
			var staff = await db.Staff.AsNoTracking().ToListAsync(cancellationToken);
			results.AddRange(staff.Select(s => new RecipientDto(s.Id, s.Name, RecipientType.Staff, s.AvatarUrl)));

			var parents = await db.Parents.AsNoTracking()
				.Where(p => p.ShareContactInfo && p.Id != callerId)
				.ToListAsync(cancellationToken);
			results.AddRange(parents.Select(p => new RecipientDto(p.Id, p.Name, RecipientType.Parent, p.AvatarUrl)));
		}
		else
		{
			var parents = await db.Parents.AsNoTracking().ToListAsync(cancellationToken);
			results.AddRange(parents.Select(p => new RecipientDto(p.Id, p.Name, RecipientType.Parent, p.AvatarUrl)));

			var staff = await db.Staff.AsNoTracking().ToListAsync(cancellationToken);
			results.AddRange(staff.Select(s => new RecipientDto(s.Id, s.Name, RecipientType.Staff, s.AvatarUrl)));
		}

		return results;
	}
}
