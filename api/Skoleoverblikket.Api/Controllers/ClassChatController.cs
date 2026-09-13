using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Controllers;

/// <summary>
/// Klassechat — one persistent group thread per klasse, replacing the per-klasse Facebook group.
/// Membership is derived (see <see cref="ClassMembershipService"/>), never stored: parents of
/// enrolled students, staff on the klasse's schema, and tenant admins.
/// </summary>
[ApiController]
[Route("api/v1/class-chats")]
[Authorize]
public sealed class ClassChatController(
	AppDbContext db,
	ITenantContext tenant,
	ClassMembershipService membership,
	FileUploadService uploads,
	INotificationService notifications,
	ILogger<ClassChatController> logger) : ControllerBase
{
	public record ClassChatThreadDto(
		Guid ClassId,
		string ClassName,
		int? GradeLevel,
		string? LastMessageBody,
		string? LastMessageSenderName,
		DateTimeOffset? LastMessageSentAt,
		int MessageCount);

	public record ClassChatAttachmentDto(
		Guid Id,
		string FileName,
		string ContentType,
		long SizeBytes,
		string Url);

	public record ClassChatMessageDto(
		Guid Id,
		SenderType SenderType,
		Guid SenderId,
		string SenderName,
		string? SenderAvatarUrl,
		string Body,
		DateTimeOffset SentAt,
		bool CanDelete,
		IReadOnlyList<ClassChatAttachmentDto> Attachments);

	public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

	public record PostMessageRequest(
		[Required, StringLength(ClassChatMessage.MaxBodyLength, MinimumLength = 1)] string Body,
		IReadOnlyList<Guid>? AttachmentIds = null);

	/// <summary>The caller's identity within the chat, resolved once per request.</summary>
	private sealed record Caller(SenderType SenderType, Guid Id, string Name, bool IsAdmin);

	/// <summary>
	/// Resolves the caller to a Parent or Staff row. An admin claim without a Staff row is
	/// rejected here — posting requires a real identity to attribute the message to.
	/// </summary>
	private async Task<Caller?> ResolveCallerAsync(CancellationToken cancellationToken)
	{
		var sub = User.GetKeycloakSubject();
		if (string.IsNullOrEmpty(sub))
		{
			return null;
		}

		if (User.IsInRole(Roles.Parent))
		{
			var parent = await db.Parents.AsNoTracking()
				.Where(p => p.KeycloakSubject == sub)
				.Select(p => new { p.Id, p.Name })
				.FirstOrDefaultAsync(cancellationToken);

			return parent is null ? null : new Caller(SenderType.Parent, parent.Id, parent.Name, IsAdmin: false);
		}

		var staff = await db.Staff.AsNoTracking()
			.Where(s => s.KeycloakSubject == sub)
			.Select(s => new { s.Id, s.Name, s.IsAdmin })
			.FirstOrDefaultAsync(cancellationToken);

		if (staff is null)
		{
			return null;
		}

		return new Caller(SenderType.Staff, staff.Id, staff.Name, staff.IsAdmin || User.IsInRole(Roles.Admin));
	}

	/// <summary>Class ids the caller may read and post in. Admins get every klasse in the tenant.</summary>
	private async Task<List<Guid>> GetAccessibleClassIdsAsync(Caller caller, CancellationToken cancellationToken)
	{
		if (caller.IsAdmin)
		{
			return await db.Classes.AsNoTracking().Select(c => c.Id).ToListAsync(cancellationToken);
		}

		return caller.SenderType == SenderType.Parent
			? await membership.GetClassIdsForParentAsync(caller.Id, cancellationToken)
			: await membership.GetClassIdsForStaffAsync(caller.Id, cancellationToken);
	}

	/// <summary>
	/// True when the caller can moderate any post in the klasse: tenant admins, and staff on the
	/// klasse's roster. Parents can only ever delete their own posts.
	/// </summary>
	private async Task<bool> CanModerateAsync(Caller caller, Guid classId, CancellationToken cancellationToken)
	{
		if (caller.IsAdmin)
		{
			return true;
		}

		if (caller.SenderType != SenderType.Staff)
		{
			return false;
		}

		var rosterStaffIds = await membership.GetStaffIdsAsync(classId, cancellationToken);
		return rosterStaffIds.Contains(caller.Id);
	}

	/// <summary>Lists the klasse threads the caller belongs to, most recently active first.</summary>
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<ClassChatThreadDto>>> GetThreads(CancellationToken cancellationToken)
	{
		var caller = await ResolveCallerAsync(cancellationToken);
		if (caller is null)
		{
			return Forbid();
		}

		var classIds = await GetAccessibleClassIdsAsync(caller, cancellationToken);
		if (classIds.Count == 0)
		{
			return Ok(Array.Empty<ClassChatThreadDto>());
		}

		var classes = await db.Classes.AsNoTracking()
			.Where(c => classIds.Contains(c.Id))
			.Select(c => new { c.Id, c.Name, c.GradeLevel })
			.ToListAsync(cancellationToken);

		var stats = await db.ClassChatMessages.AsNoTracking()
			.Where(m => classIds.Contains(m.ClassId) && m.DeletedAt == null)
			.GroupBy(m => m.ClassId)
			.Select(g => new
			{
				ClassId = g.Key,
				Count = g.Count(),
				LastSentAt = g.Max(m => m.SentAt),
			})
			.ToListAsync(cancellationToken);

		var statsByClass = stats.ToDictionary(s => s.ClassId);

		// Fetch only the newest surviving post per klasse for the preview line.
		var lastSentAts = stats.Select(s => s.LastSentAt).ToList();
		var lastMessages = lastSentAts.Count == 0
			? []
			: await db.ClassChatMessages.AsNoTracking()
				.Where(m => classIds.Contains(m.ClassId) && m.DeletedAt == null && lastSentAts.Contains(m.SentAt))
				.Select(m => new { m.ClassId, m.Body, m.SentAt, m.SenderType, m.SenderId })
				.ToListAsync(cancellationToken);

		var lastByClass = lastMessages
			.GroupBy(m => m.ClassId)
			.ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.SentAt).First());

		var names = await ResolveSenderNamesAsync(
			lastMessages.Select(m => (m.SenderType, m.SenderId)), cancellationToken);

		var result = classes
			.Select(c =>
			{
				statsByClass.TryGetValue(c.Id, out var stat);
				lastByClass.TryGetValue(c.Id, out var last);

				return new ClassChatThreadDto(
					c.Id,
					c.Name,
					c.GradeLevel,
					last?.Body,
					last is null ? null : names.GetValueOrDefault((last.SenderType, last.SenderId)).Name,
					stat?.LastSentAt,
					stat?.Count ?? 0);
			})
			.OrderByDescending(t => t.LastMessageSentAt ?? DateTimeOffset.MinValue)
			.ThenBy(t => t.ClassName)
			.ToList();

		return Ok(result);
	}

	/// <summary>Paginated posts in one klasse thread, oldest first.</summary>
	[HttpGet("{classId:guid}/messages")]
	public async Task<ActionResult<PagedResult<ClassChatMessageDto>>> GetMessages(
		Guid classId,
		[FromQuery] int page = 1,
		[FromQuery] int pageSize = 30,
		CancellationToken cancellationToken = default)
	{
		pageSize = Math.Clamp(pageSize, 1, 100);
		page = Math.Max(page, 1);

		var caller = await ResolveCallerAsync(cancellationToken);
		if (caller is null)
		{
			return Forbid();
		}

		var classExists = await db.Classes.AsNoTracking().AnyAsync(c => c.Id == classId, cancellationToken);
		if (!classExists)
		{
			return NotFound(new ProblemDetails { Title = "Klassen findes ikke." });
		}

		var classIds = await GetAccessibleClassIdsAsync(caller, cancellationToken);
		if (!classIds.Contains(classId))
		{
			return Forbid();
		}

		var canModerate = await CanModerateAsync(caller, classId, cancellationToken);

		var query = db.ClassChatMessages.AsNoTracking()
			.Where(m => m.ClassId == classId && m.DeletedAt == null);

		var total = await query.CountAsync(cancellationToken);

		var skip = (long)(page - 1) * pageSize;
		if (skip > total)
		{
			skip = total;
		}

		var messages = await query
			.OrderBy(m => m.SentAt)
			.Skip((int)skip)
			.Take(pageSize)
			.Select(m => new
			{
				m.Id,
				m.SenderType,
				m.SenderId,
				m.Body,
				m.SentAt,
				Attachments = m.Attachments
					.Select(a => new ClassChatAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.Url))
					.ToList(),
			})
			.ToListAsync(cancellationToken);

		var senders = await ResolveSenderNamesAsync(
			messages.Select(m => (m.SenderType, m.SenderId)), cancellationToken);

		var items = messages.Select(m =>
		{
			var sender = senders.GetValueOrDefault((m.SenderType, m.SenderId));
			var isOwn = m.SenderType == caller.SenderType && m.SenderId == caller.Id;

			return new ClassChatMessageDto(
				m.Id,
				m.SenderType,
				m.SenderId,
				sender.Name ?? "Ukendt",
				sender.AvatarUrl,
				m.Body,
				m.SentAt,
				isOwn || canModerate,
				m.Attachments);
		}).ToList();

		return Ok(new PagedResult<ClassChatMessageDto>(items, total, page, pageSize));
	}

	/// <summary>Posts a message, optionally claiming attachments already uploaded via presign/confirm.</summary>
	[HttpPost("{classId:guid}/messages")]
	public async Task<ActionResult<ClassChatMessageDto>> PostMessage(
		Guid classId,
		[FromBody] PostMessageRequest req,
		CancellationToken cancellationToken)
	{
		var caller = await ResolveCallerAsync(cancellationToken);
		if (caller is null)
		{
			return Forbid();
		}

		var @class = await db.Classes.AsNoTracking()
			.Where(c => c.Id == classId)
			.Select(c => new { c.Id, c.Name })
			.FirstOrDefaultAsync(cancellationToken);

		if (@class is null)
		{
			return NotFound(new ProblemDetails { Title = "Klassen findes ikke." });
		}

		var classIds = await GetAccessibleClassIdsAsync(caller, cancellationToken);
		if (!classIds.Contains(classId))
		{
			return Forbid();
		}

		var body = req.Body.Trim();
		if (body.Length == 0)
		{
			return ValidationProblem(new ValidationProblemDetails
			{
				Errors = { ["body"] = ["Beskeden må ikke være tom."] }
			});
		}

		// Only claim attachments uploaded for this klasse and not yet attached to a post, so a
		// caller cannot graft another klasse's file onto their message.
		var attachmentIds = req.AttachmentIds?.Distinct().ToList() ?? [];
		if (attachmentIds.Count > ClassChatAttachment.MaxPerMessage)
		{
			return ValidationProblem(new ValidationProblemDetails
			{
				Errors = { ["attachmentIds"] = [$"Der kan højst vedhæftes {ClassChatAttachment.MaxPerMessage} filer."] }
			});
		}

		List<ClassChatAttachment> attachments = [];
		if (attachmentIds.Count > 0)
		{
			attachments = await db.ClassChatAttachments
				.Where(a => attachmentIds.Contains(a.Id) && a.ClassId == classId && a.MessageId == null)
				.ToListAsync(cancellationToken);

			if (attachments.Count != attachmentIds.Count)
			{
				return ValidationProblem(new ValidationProblemDetails
				{
					Errors = { ["attachmentIds"] = ["En eller flere vedhæftede filer kunne ikke findes."] }
				});
			}
		}

		var message = new ClassChatMessage
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			ClassId = classId,
			SenderType = caller.SenderType,
			SenderId = caller.Id,
			Body = body,
			SentAt = DateTimeOffset.UtcNow,
		};

		db.ClassChatMessages.Add(message);

		foreach (var attachment in attachments)
		{
			attachment.MessageId = message.Id;
		}

		await db.SaveChangesAsync(cancellationToken);

		await NotifyMembersAsync(classId, @class.Name, message, caller, cancellationToken);

		var avatarUrl = caller.SenderType == SenderType.Parent
			? await db.Parents.AsNoTracking().Where(p => p.Id == caller.Id).Select(p => p.AvatarUrl).FirstOrDefaultAsync(cancellationToken)
			: await db.Staff.AsNoTracking().Where(s => s.Id == caller.Id).Select(s => s.AvatarUrl).FirstOrDefaultAsync(cancellationToken);

		return Created(string.Empty, new ClassChatMessageDto(
			message.Id,
			message.SenderType,
			message.SenderId,
			caller.Name,
			avatarUrl,
			message.Body,
			message.SentAt,
			CanDelete: true,
			attachments.Select(a => new ClassChatAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.Url)).ToList()));
	}

	/// <summary>Soft-deletes a post. Allowed for the sender, roster staff, and tenant admins.</summary>
	[HttpDelete("{classId:guid}/messages/{messageId:guid}")]
	public async Task<IActionResult> DeleteMessage(Guid classId, Guid messageId, CancellationToken cancellationToken)
	{
		var caller = await ResolveCallerAsync(cancellationToken);
		if (caller is null)
		{
			return Forbid();
		}

		var classIds = await GetAccessibleClassIdsAsync(caller, cancellationToken);
		if (!classIds.Contains(classId))
		{
			return Forbid();
		}

		var message = await db.ClassChatMessages
			.FirstOrDefaultAsync(m => m.Id == messageId && m.ClassId == classId, cancellationToken);

		if (message is null || message.DeletedAt is not null)
		{
			return NotFound(new ProblemDetails { Title = "Beskeden findes ikke." });
		}

		var isOwn = message.SenderType == caller.SenderType && message.SenderId == caller.Id;
		if (!isOwn && !await CanModerateAsync(caller, classId, cancellationToken))
		{
			return Forbid();
		}

		message.DeletedAt = DateTimeOffset.UtcNow;
		await db.SaveChangesAsync(cancellationToken);

		return NoContent();
	}

	public record PresignAttachmentRequest(
		[Required] string FileName,
		[Required, Range(1, ClassChatAttachment.MaxFileSizeBytes)] long FileSizeBytes);

	public record PresignAttachmentResponse(Guid AttachmentId, string UploadUrl, string ConfirmToken, string ContentType);

	public sealed record AttachmentTokenPayload(
		Guid AttachmentId,
		Guid TenantId,
		Guid ClassId,
		string FileName,
		string ContentType,
		long SizeBytes,
		string StorageKey,
		string PublicUrl,
		long ExpiresAt);

	/// <summary>Step 1 of the upload: mint a presigned PUT URL plus a signed confirm token.</summary>
	[HttpPost("{classId:guid}/attachments/presign")]
	public async Task<ActionResult<PresignAttachmentResponse>> PresignAttachment(
		Guid classId,
		[FromBody] PresignAttachmentRequest req,
		CancellationToken cancellationToken)
	{
		var caller = await ResolveCallerAsync(cancellationToken);
		if (caller is null)
		{
			return Forbid();
		}

		var classIds = await GetAccessibleClassIdsAsync(caller, cancellationToken);
		if (!classIds.Contains(classId))
		{
			return Forbid();
		}

		if (req.FileSizeBytes > ClassChatAttachment.MaxFileSizeBytes)
		{
			return ValidationProblem(new ValidationProblemDetails
			{
				Errors = { ["file"] = ["Filen må maksimalt være 25 MB."] }
			});
		}

		var contentType = FileUploadService.ResolveContentType(req.FileName);
		if (!IsAllowedAttachmentType(req.FileName, contentType))
		{
			return ValidationProblem(new ValidationProblemDetails
			{
				Errors = { ["file"] = ["Filtypen understøttes ikke. Vedhæft et dokument eller et billede."] }
			});
		}

		var usedBytes = await uploads.GetUsedBytesAsync<ClassChatAttachment>(cancellationToken)
					  + await uploads.GetUsedBytesAsync<SchoolFile>(cancellationToken);

		if (FileUploadService.WouldExceedQuota(usedBytes, req.FileSizeBytes))
		{
			return ValidationProblem(new ValidationProblemDetails
			{
				Errors = { ["file"] = ["Lagerkvoten er nået (100 GB). Slet filer for at frigøre plads."] }
			});
		}

		var attachmentId = Guid.NewGuid();
		var ext = FileUploadService.ExtensionOf(req.FileName);
		var key = $"class-chat/{tenant.TenantId}/{classId}/{attachmentId}{ext}";

		var (uploadUrl, publicUrl) = await uploads.GeneratePresignedUploadAsync(
			key, contentType, req.FileSizeBytes, cancellationToken);

		var token = uploads.SignToken(new AttachmentTokenPayload(
			attachmentId,
			tenant.TenantId,
			classId,
			Path.GetFileName(req.FileName),
			contentType,
			req.FileSizeBytes,
			key,
			publicUrl,
			FileUploadService.PresignExpiresAt()));

		return Ok(new PresignAttachmentResponse(attachmentId, uploadUrl, token, contentType));
	}

	public record ConfirmAttachmentRequest([Required] string ConfirmToken);

	/// <summary>Step 2 of the upload: after the PUT succeeds, persist the attachment row.</summary>
	[HttpPost("{classId:guid}/attachments/confirm")]
	public async Task<ActionResult<ClassChatAttachmentDto>> ConfirmAttachment(
		Guid classId,
		[FromBody] ConfirmAttachmentRequest req,
		CancellationToken cancellationToken)
	{
		var caller = await ResolveCallerAsync(cancellationToken);
		if (caller is null)
		{
			return Forbid();
		}

		if (!uploads.TryVerifyToken<AttachmentTokenPayload>(req.ConfirmToken, out var payload) || payload is null)
		{
			return BadRequest(new ProblemDetails { Title = "Ugyldigt bekræftelsestoken." });
		}

		if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > payload.ExpiresAt)
		{
			return BadRequest(new ProblemDetails { Title = "Bekræftelsestokenet er udløbet." });
		}

		if (payload.TenantId != tenant.TenantId || payload.ClassId != classId)
		{
			return Forbid();
		}

		var classIds = await GetAccessibleClassIdsAsync(caller, cancellationToken);
		if (!classIds.Contains(classId))
		{
			return Forbid();
		}

		var existing = await db.ClassChatAttachments.AsNoTracking()
			.FirstOrDefaultAsync(a => a.Id == payload.AttachmentId, cancellationToken);

		if (existing is not null)
		{
			return Ok(new ClassChatAttachmentDto(existing.Id, existing.FileName, existing.ContentType, existing.SizeBytes, existing.Url));
		}

		var uploadVerified = await uploads.VerifyUploadedObjectAsync(payload.StorageKey, payload.SizeBytes, cancellationToken);
		if (!uploadVerified)
		{
			return BadRequest(new ProblemDetails { Title = "Filen kunne ikke bekræftes i lagerplads. Prøv at uploade igen." });
		}

		var attachment = new ClassChatAttachment
		{
			Id = payload.AttachmentId,
			TenantId = payload.TenantId,
			ClassId = payload.ClassId,
			FileName = payload.FileName,
			ContentType = payload.ContentType,
			SizeBytes = payload.SizeBytes,
			StorageKey = payload.StorageKey,
			Url = payload.PublicUrl,
		};

		db.ClassChatAttachments.Add(attachment);
		await db.SaveChangesAsync(cancellationToken);

		return Ok(new ClassChatAttachmentDto(
			attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.Url));
	}

	/// <summary>Documents and images only — no archives, executables, or arbitrary binaries.</summary>
	private static bool IsAllowedAttachmentType(string fileName, string contentType)
	{
		if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		string[] documentExtensions =
		[
			".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
			".txt", ".csv", ".odt", ".ods", ".odp", ".rtf",
		];

		return documentExtensions.Contains(FileUploadService.ExtensionOf(fileName));
	}

	/// <summary>Looks up display name + avatar for a mixed set of Parent/Staff sender ids.</summary>
	private async Task<Dictionary<(SenderType, Guid), (string? Name, string? AvatarUrl)>> ResolveSenderNamesAsync(
		IEnumerable<(SenderType SenderType, Guid SenderId)> senders,
		CancellationToken cancellationToken)
	{
		var list = senders.Distinct().ToList();
		var result = new Dictionary<(SenderType, Guid), (string? Name, string? AvatarUrl)>();
		if (list.Count == 0)
		{
			return result;
		}

		var parentIds = list.Where(s => s.SenderType == SenderType.Parent).Select(s => s.SenderId).ToList();
		var staffIds = list.Where(s => s.SenderType == SenderType.Staff).Select(s => s.SenderId).ToList();

		if (parentIds.Count > 0)
		{
			var parents = await db.Parents.AsNoTracking()
				.Where(p => parentIds.Contains(p.Id))
				.Select(p => new { p.Id, p.Name, p.AvatarUrl })
				.ToListAsync(cancellationToken);

			foreach (var p in parents)
			{
				result[(SenderType.Parent, p.Id)] = (p.Name, p.AvatarUrl);
			}
		}

		if (staffIds.Count > 0)
		{
			var staff = await db.Staff.AsNoTracking()
				.Where(s => staffIds.Contains(s.Id))
				.Select(s => new { s.Id, s.Name, s.AvatarUrl })
				.ToListAsync(cancellationToken);

			foreach (var s in staff)
			{
				result[(SenderType.Staff, s.Id)] = (s.Name, s.AvatarUrl);
			}
		}

		return result;
	}

	// The message is already persisted by the time this runs, so a notification or email failure
	// must never fail the request — the caller would retry and post a duplicate. Log and swallow.
	private async Task NotifyMembersAsync(
		Guid classId,
		string className,
		ClassChatMessage message,
		Caller sender,
		CancellationToken cancellationToken)
	{
		try
		{
			var parentIds = await membership.GetParentIdsAsync(classId, cancellationToken);
			var staffIds = await membership.GetStaffIdsAsync(classId, cancellationToken);

			var body = $"Ny besked i klassechatten for {className} fra {sender.Name}";

			// The membership queries return distinct ids, and the sender is filtered out here, so
			// each person is notified exactly once — a parent with two children in the klasse, or a
			// teacher who is also the aide on another slot, must not be notified twice.
			var requests = parentIds
				.Where(id => !(sender.SenderType == SenderType.Parent && id == sender.Id))
				.Select(id => new NotificationRequest(id, RecipientType.Parent, NotificationType.ClassChatMessage, classId, body))
				.Concat(staffIds
					.Where(id => !(sender.SenderType == SenderType.Staff && id == sender.Id))
					.Select(id => new NotificationRequest(id, RecipientType.Staff, NotificationType.ClassChatMessage, classId, body)))
				.ToList();

			if (requests.Count > 0)
			{
				await notifications.CreateBatchAsync(requests, cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Kunne ikke sende notifikationer for klassechat-besked {MessageId}", message.Id);
		}
	}
}
