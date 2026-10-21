using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public sealed class NotificationsController(AppDbContext db) : ControllerBase
{
	public record NotificationDto(
		Guid Id,
		NotificationType Type,
		string Body,
		DateTimeOffset CreatedAt,
		DateTimeOffset? ReadAt,
		Guid? ReferenceId);

	/// <summary>Returns the last 50 notifications for the authenticated caller, newest first.</summary>
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<NotificationDto>>> GetNotifications(CancellationToken cancellationToken)
	{
		var (recipientId, recipientType) = await ResolveCallerAsync(cancellationToken);
		if (recipientId is null)
		{
			return Ok(Array.Empty<NotificationDto>());
		}

		var notifications = await db.Notifications
			.AsNoTracking()
			.Where(n => n.RecipientId == recipientId.Value && n.RecipientType == recipientType)
			.OrderByDescending(n => n.CreatedAt)
			.Take(50)
			.Select(n => new NotificationDto(n.Id, n.Type, n.Body, n.CreatedAt, n.ReadAt, n.ReferenceId))
			.ToListAsync(cancellationToken);

		return Ok(notifications);
	}

	/// <summary>Marks a single notification as read.</summary>
	[HttpPost("{id:guid}/read")]
	public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
	{
		var (recipientId, recipientType) = await ResolveCallerAsync(cancellationToken);
		if (recipientId is null)
		{
			return Forbid();
		}

		var notification = await db.Notifications
			.FirstOrDefaultAsync(n => n.Id == id && n.RecipientId == recipientId.Value && n.RecipientType == recipientType, cancellationToken);

		if (notification is null)
		{
			return NotFound();
		}

		if (notification.ReadAt is null)
		{
			notification.ReadAt = DateTimeOffset.UtcNow;
			await db.SaveChangesAsync(cancellationToken);
		}

		return NoContent();
	}

	/// <summary>Marks all unread notifications for the caller as read.</summary>
	[HttpPost("read-all")]
	public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
	{
		var (recipientId, recipientType) = await ResolveCallerAsync(cancellationToken);
		if (recipientId is null)
		{
			return Forbid();
		}

		var unread = await db.Notifications
			.Where(n => n.RecipientId == recipientId.Value && n.RecipientType == recipientType && n.ReadAt == null)
			.ToListAsync(cancellationToken);

		var now = DateTimeOffset.UtcNow;
		foreach (var n in unread)
		{
			n.ReadAt = now;
		}

		if (unread.Count > 0)
		{
			await db.SaveChangesAsync(cancellationToken);
		}

		return NoContent();
	}

	private async Task<(Guid? recipientId, RecipientType recipientType)> ResolveCallerAsync(CancellationToken cancellationToken)
	{
		var subject = User.GetKeycloakSubject();
		if (subject is null)
		{
			return (null, default);
		}

		if (User.IsInRole(Roles.Parent))
		{
			var parentId = await db.Parents
				.AsNoTracking()
				.Where(p => p.KeycloakSubject == subject)
				.Select(p => (Guid?)p.Id)
				.FirstOrDefaultAsync(cancellationToken);
			return (parentId, RecipientType.Parent);
		}
		else if (User.IsInRole(Roles.Staff))
		{
			var staffId = await db.Staff
				.AsNoTracking()
				.Where(s => s.KeycloakSubject == subject)
				.Select(s => (Guid?)s.Id)
				.FirstOrDefaultAsync(cancellationToken);
			return (staffId, RecipientType.Staff);
		}

		return (null, default);
	}
}

[ApiController]
[Route("api/v1/notification-preferences")]
[Authorize]
public sealed class NotificationPreferencesController(AppDbContext db, ITenantContext tenantContext) : ControllerBase
{
	public record NotificationPreferenceDto(NotificationType Type, bool InApp, bool Email);
	public record UpsertPreferenceItem(NotificationType Type, bool InApp, bool Email);

	/// <summary>Returns all notification preferences for the caller.</summary>
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<NotificationPreferenceDto>>> GetPreferences(CancellationToken cancellationToken)
	{
		var (userId, userType) = await ResolveCallerAsync(cancellationToken);
		if (userId is null)
		{
			return Ok(Array.Empty<NotificationPreferenceDto>());
		}

		var prefs = await db.NotificationPreferences
			.AsNoTracking()
			.Where(p => p.UserId == userId.Value && p.UserType == userType)
			.Select(p => new NotificationPreferenceDto(p.Type, p.InApp, p.Email))
			.ToListAsync(cancellationToken);

		return Ok(prefs);
	}

	/// <summary>Replaces all notification preferences for the caller with the provided list.</summary>
	[HttpPut]
	public async Task<IActionResult> UpsertPreferences(
		[FromBody] IReadOnlyList<UpsertPreferenceItem> items, CancellationToken cancellationToken)
	{
		if (items.GroupBy(i => i.Type).Any(g => g.Count() > 1))
		{
			return BadRequest(new { detail = "Duplicate notification types are not allowed." });
		}

		var (userId, userType) = await ResolveCallerAsync(cancellationToken);
		if (userId is null)
		{
			return Forbid();
		}

		// Delete existing, insert new batch
		var existing = await db.NotificationPreferences
			.Where(p => p.UserId == userId.Value && p.UserType == userType)
			.ToListAsync(cancellationToken);
		db.NotificationPreferences.RemoveRange(existing);

		foreach (var item in items)
		{
			db.NotificationPreferences.Add(new NotificationPreference
			{
				Id = Guid.NewGuid(),
				TenantId = tenantContext.TenantId,
				UserId = userId.Value,
				UserType = userType,
				Type = item.Type,
				InApp = item.InApp,
				Email = item.Email,
			});
		}

		await db.SaveChangesAsync(cancellationToken);
		return NoContent();
	}

	private async Task<(Guid? userId, RecipientType userType)> ResolveCallerAsync(CancellationToken cancellationToken)
	{
		var subject = User.GetKeycloakSubject();
		if (subject is null)
		{
			return (null, default);
		}

		if (User.IsInRole(Roles.Parent))
		{
			var parentId = await db.Parents
				.AsNoTracking()
				.Where(p => p.KeycloakSubject == subject)
				.Select(p => (Guid?)p.Id)
				.FirstOrDefaultAsync(cancellationToken);
			return (parentId, RecipientType.Parent);
		}
		else if (User.IsInRole(Roles.Staff))
		{
			var staffId = await db.Staff
				.AsNoTracking()
				.Where(s => s.KeycloakSubject == subject)
				.Select(s => (Guid?)s.Id)
				.FirstOrDefaultAsync(cancellationToken);
			return (staffId, RecipientType.Staff);
		}

		return (null, default);
	}
}
