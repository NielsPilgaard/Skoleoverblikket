namespace Skoleoverblikket.Api.Services;

public enum RecipientType { Parent, Staff, Board }
/// <summary>Stored as int — append new values, never reorder.</summary>
public enum NotificationType
{
	NewMessage,
	NewContactMessage,
	WeekPlanChanged,
	LeaveApproved,
	LeaveRejected,
	VacationRegistrationOpened,
	GroupMessage,
	ClassChatMessage,

	/// <summary>To parents: fremmøde registered ulovligt fravær on their child.</summary>
	UnauthorizedAbsence,

	/// <summary>To admins + class staff: a student reached 10% ulovligt fravær in the quarter.</summary>
	AbsenceThreshold,

	/// <summary>To admins: a school year's absence data is deleted on 1 August.</summary>
	AbsenceRetentionWarning,

	/// <summary>To admins: a parent asked for ekstraordinær frihed.</summary>
	LeaveRequested,

	/// <summary>To admins: a staff member reported themself absent.</summary>
	StaffAbsenceReported,

	/// <summary>To the vikar: assigned to cover a lektion.</summary>
	SubstituteAssigned,
}

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
