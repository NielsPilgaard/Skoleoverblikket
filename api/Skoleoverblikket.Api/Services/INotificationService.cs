namespace Skoleoverblikket.Api.Services;

public enum RecipientType { Parent, Staff, Board }
public enum NotificationType { NewMessage, NewContactMessage, WeekPlanChanged, AbsenceConfirmed, AbsenceDismissed, VacationRegistrationOpened, GroupMessage, ClassChatMessage }

/// <summary>Per-type notification defaults applied when a user has no stored preference row.</summary>
public static class NotificationDefaults
{
	/// <summary>
	/// Class chat is high-volume — a busy klasse thread would flood inboxes — so email is opt-in
	/// there while every other type keeps the historical opt-out default.
	/// </summary>
	public static bool EmailEnabledByDefault(NotificationType type) =>
		type != NotificationType.ClassChatMessage;

	public static bool InAppEnabledByDefault(NotificationType type) => true;
}

public sealed record NotificationRequest(
	Guid RecipientId,
	RecipientType RecipientType,
	NotificationType Type,
	Guid? ReferenceId,
	string Body);

public interface INotificationService
{
	Task CreateAsync(
		Guid recipientId,
		RecipientType recipientType,
		NotificationType type,
		Guid? referenceId,
		string body,
		CancellationToken cancellationToken);

	Task CreateBatchAsync(
		IEnumerable<NotificationRequest> requests,
		CancellationToken cancellationToken);
}
