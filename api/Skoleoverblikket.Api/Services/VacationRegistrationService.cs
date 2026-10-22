using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;
using static Skoleoverblikket.Api.Controllers.VacationRegistrationController;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// The one writer of <see cref="VacationRegistrationWindow"/> (ferieindmelding) rows. The request and response
/// records stay nested in <see cref="VacationRegistrationController"/> so their OpenAPI schema names, and with
/// them the generated web client, are unchanged.
/// </summary>
public sealed class VacationRegistrationService(AppDbContext db, ITenantContext tenant, INotificationService notifications)
{
	/// <summary>Creates a window and, if it starts open, notifies every parent at the school.</summary>
	public async Task<WindowDto> CreateWindowAsync(CreateWindowRequest req, CancellationToken cancellationToken)
	{
		var window = new VacationRegistrationWindow
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			Title = req.Title,
			RegistrationDeadline = req.RegistrationDeadline,
			CareStartDate = req.CareStartDate,
			CareEndDate = req.CareEndDate,
			Granularity = req.Granularity,
			IsOpen = req.IsOpen,
		};

		db.VacationRegistrationWindows.Add(window);
		await db.SaveChangesAsync(cancellationToken);

		if (window.IsOpen)
		{
			await NotifyAllParentsAsync(window);
		}

		return new WindowDto(
			window.Id, window.Title, window.RegistrationDeadline, window.CareStartDate, window.CareEndDate,
			window.Granularity, window.IsOpen, 0, window.CreatedAt);
	}

	/// <summary>Updates a window and notifies every parent when it goes from closed to open. False if not found.</summary>
	public async Task<bool> UpdateWindowAsync(Guid id, UpdateWindowRequest req, CancellationToken cancellationToken)
	{
		var window = await db.VacationRegistrationWindows
			.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

		if (window is null)
		{
			return false;
		}

		var wasOpen = window.IsOpen;

		window.Title = req.Title;
		window.RegistrationDeadline = req.RegistrationDeadline;
		window.CareStartDate = req.CareStartDate;
		window.CareEndDate = req.CareEndDate;
		window.Granularity = req.Granularity;
		window.IsOpen = req.IsOpen;

		await db.SaveChangesAsync(cancellationToken);

		if (!wasOpen && window.IsOpen)
		{
			await NotifyAllParentsAsync(window);
		}

		return true;
	}

	/// <summary>Deletes a window. False if not found.</summary>
	public async Task<bool> DeleteWindowAsync(Guid id, CancellationToken cancellationToken)
	{
		var window = await db.VacationRegistrationWindows
			.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

		if (window is null)
		{
			return false;
		}

		db.VacationRegistrationWindows.Remove(window);
		await db.SaveChangesAsync(cancellationToken);
		return true;
	}

	// One batch for the whole school: preferences, inserts and e-mail lookups are a handful of queries
	// instead of several per parent. Not cancellable: the window is already saved as open, so a closed tab
	// must not leave parents unnotified, and a retry would not notify them again.
	private async Task NotifyAllParentsAsync(VacationRegistrationWindow window)
	{
		var parentIds = await db.Parents
			.AsNoTracking()
			.Select(p => p.Id)
			.ToListAsync(CancellationToken.None);

		var body = $"Ferietilmelding åben: {window.Title}. Frist: {window.RegistrationDeadline:d. MMMM}.";

		await notifications.CreateBatchAsync(
			parentIds.Select(id => new NotificationRequest(
				id, RecipientType.Parent, NotificationType.VacationRegistrationOpened, window.Id, body)),
			CancellationToken.None);
	}
}
