using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Storage;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// Deletes <see cref="ClassChatAttachment"/> rows that were confirmed (the presigned PUT succeeded
/// and the confirm endpoint persisted the row) but never claimed by a post — the user picked a
/// file, then closed the composer without sending. Once the confirm token has expired
/// (<see cref="FileUploadService.PresignExpiry"/>) such a row can never be attached to a message,
/// so it is pure dead weight: its bytes still count against the tenant storage quota that
/// <c>ClassChatController.PresignAttachment</c> enforces.
///
/// This is a maintenance job that spans every tenant, so it deliberately runs outside the request
/// pipeline (no <c>tenant_id</c> claim) and calls <c>IgnoreQueryFilters()</c> to reach across
/// tenants. It is the one place the global tenant filter is bypassed on purpose.
/// </summary>
public sealed class ClassChatAttachmentSweeper(
	IServiceScopeFactory scopeFactory,
	ILogger<ClassChatAttachmentSweeper> logger) : BackgroundService
{
	/// <summary>How often the sweep runs. Abandoned uploads are not urgent, so hourly is plenty.</summary>
	private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

	/// <summary>Rows uploaded within this window might still have a confirm in flight — leave them.</summary>
	public static readonly TimeSpan Grace = FileUploadService.PresignExpiry;

	/// <summary>Storage deletes are one-by-one, so cap each sweep to keep the pass bounded.</summary>
	public const int BatchSize = 100;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// Let the app finish starting before the first pass.
		try
		{
			await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
		}
		catch (OperationCanceledException)
		{
			return;
		}

		using var timer = new PeriodicTimer(SweepInterval);
		do
		{
			try
			{
				await using var scope = scopeFactory.CreateAsyncScope();
				var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
				var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();

				await SweepAsync(db, storage, logger, DateTimeOffset.UtcNow - Grace, BatchSize, stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				// A transient DB or storage failure must not kill the service — try again next tick.
				logger.LogError(ex, "Class chat attachment sweep failed");
			}
		}
		while (await timer.WaitForNextTickAsync(stoppingToken));
	}

	/// <summary>
	/// Deletes the storage object and DB row for every confirmed-but-unclaimed attachment older
	/// than <paramref name="cutoff"/>, up to <paramref name="batchSize"/> rows. A storage-delete
	/// failure keeps the row so the next pass retries it. Bypasses the tenant query filter by
	/// design — see the type summary.
	/// </summary>
	public static async Task<int> SweepAsync(
		AppDbContext db,
		IObjectStorage storage,
		ILogger logger,
		DateTimeOffset cutoff,
		int batchSize,
		CancellationToken cancellationToken)
	{
		var abandoned = await db.ClassChatAttachments
			.IgnoreQueryFilters()
			.Where(a => a.MessageId == null && a.UploadedAt < cutoff)
			.OrderBy(a => a.UploadedAt)
			.Take(batchSize)
			.ToListAsync(cancellationToken);

		if (abandoned.Count == 0)
		{
			return 0;
		}

		var removable = new List<ClassChatAttachment>(abandoned.Count);
		foreach (var attachment in abandoned)
		{
			try
			{
				await storage.DeleteAsync(attachment.StorageKey, cancellationToken);
				removable.Add(attachment);
			}
			catch (OperationCanceledException)
			{
				// A cancelled run must abort before removing DB rows for objects that still exist.
				throw;
			}
			catch (Exception ex)
			{
				// Keep the row so the next sweep retries the storage delete.
				logger.LogWarning(
					ex, "Could not delete abandoned class chat attachment {AttachmentId} from storage", attachment.Id);
			}
		}

		if (removable.Count == 0)
		{
			return 0;
		}

		db.ClassChatAttachments.RemoveRange(removable);
		await db.SaveChangesAsync(cancellationToken);

		logger.LogInformation("Swept {Count} abandoned class chat attachment(s)", removable.Count);
		return removable.Count;
	}
}
