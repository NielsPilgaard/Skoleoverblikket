using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using static Skoleoverblikket.Api.Controllers.ContactThreadsController;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// Parent–school contact threads (Kontaktbog). Only the thread list lives here so far; the other
/// <see cref="ContactThreadsController"/> endpoints move in when their logic changes.
/// The response records stay nested in the controller so their OpenAPI schema names,
/// and with them the generated web client, are unchanged.
/// </summary>
public sealed class ContactThreadService(AppDbContext db)
{
	/// <summary>
	/// Every thread the caller can see, with its latest message and the number of messages from
	/// the other side the caller hasn't read. Staff see all threads; a parent sees only threads
	/// about their own children.
	/// </summary>
	public async Task<List<ContactThreadDto>> GetThreadsAsync(
		string? keycloakSubject, bool isParent, CancellationToken cancellationToken)
	{
		IQueryable<ContactThread> query = db.ContactThreads.AsNoTracking();

		if (isParent)
		{
			var parentId = await db.Parents
				.AsNoTracking()
				.Where(p => p.KeycloakSubject == keycloakSubject)
				.Select(p => (Guid?)p.Id)
				.FirstOrDefaultAsync(cancellationToken);

			if (parentId is null)
			{
				return [];
			}

			query = query.Where(t => t.Student != null && t.Student.Parents.Any(p => p.Id == parentId.Value));
		}

		var callerSenderType = isParent ? SenderType.Parent : SenderType.Staff;

		// Only the latest message and an unread count are shown, so compute both in the database
		// instead of loading every message body of every thread.
		var threads = await query
			.Select(t => new
			{
				t.Id,
				t.StudentId,
				t.CreatedAt,
				StudentName = t.Student != null ? t.Student.Name : string.Empty,
				LastMessage = t.Messages
					.OrderByDescending(m => m.SentAt)
					.ThenByDescending(m => m.Id)
					.Select(m => new { m.Body, m.SentAt, m.SenderType })
					.FirstOrDefault(),
				UnreadCount = t.Messages.Count(m => m.ReadAt == null && m.SenderType != callerSenderType),
			})
			.ToListAsync(cancellationToken);

		// Newest activity first, so the thread with the latest reply is on top.
		return threads
			.OrderByDescending(t => t.LastMessage?.SentAt ?? t.CreatedAt)
			.ThenByDescending(t => t.Id)
			.Select(t => new ContactThreadDto(
				t.Id,
				t.StudentId,
				t.StudentName,
				t.LastMessage?.Body,
				t.LastMessage?.SentAt,
				t.LastMessage?.SenderType,
				t.UnreadCount))
			.ToList();
	}
}
