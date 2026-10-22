using System.Text.Json.Serialization;
using Skoleoverblikket.Api.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace Skoleoverblikket.Api.Services;

public static class ServicesExtensions
{
	public static IServiceCollection AddDomainServices(this IServiceCollection services)
	{
		services.AddScoped<ConflictDetectionService>();
		services.AddScoped<StaffInvitationService>();
		services.AddScoped<ParentInvitationService>();
		services.AddScoped<BoardMemberInvitationService>();
		services.AddScoped<ExcelReportBuilder>();
		services.AddScoped<SubscriptionService>();
		services.AddScoped<FileUploadService>();
		services.AddScoped<ClassMembershipService>();
		services.AddScoped<ComplianceCoverageService>();
		services.AddScoped<AbsenceService>();
		services.AddScoped<AbsenceStatsService>();
		services.AddScoped<StaffAbsenceService>();
		services.AddScoped<SubstituteService>();
		services.AddScoped<WeekPlanService>();
		services.AddScoped<RoomService>();
		services.AddScoped<CourseService>();
		services.AddScoped<VacationRegistrationService>();
		services.AddScoped<SchoolDeletionService>();
		services.AddScoped<SchoolExportService>();
		services.AddScoped<DataProcessingAgreementService>();
		services.AddScoped<SchoolSignupService>();
		services.AddScoped<INotificationService, NotificationService>();
		services.AddSingleton<NotificationEmailQueue>();
		services.AddHostedService(sp => sp.GetRequiredService<NotificationEmailQueue>());
		services.AddSingleton<UvmTimetableService>();
		services.AddBackupStatus();

		services.AddHostedService<ClassChatAttachmentSweeper>();
		services.AddHostedService<AbsenceRetentionJob>();
		services.AddHostedService<SchoolRetentionJob>();
		services.AddHostedService<WelcomeEmailJob>();

		services.AddOptions<ApplicationOptions>()
			.BindConfiguration(ApplicationOptions.SectionName)
			.ValidateDataAnnotations()
			.ValidateOnStart();

		services.AddProblemDetails();

		services.AddControllers(options =>
			{
				// Order matters: an expired subscription blocks writes before module checks run.
				options.Filters.AddService<SubscriptionAccessFilter>();
				options.Filters.AddService<ModuleAccessFilter>();
			})
			.AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

		return services;
	}
}
