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
		services.AddScoped<AbsenceService>();
		services.AddScoped<AbsenceStatsService>();
		services.AddScoped<StaffAbsenceService>();
		services.AddScoped<SubstituteService>();
		services.AddScoped<WeekPlanService>();
		services.AddScoped<SchoolDeletionService>();
		services.AddScoped<DataProcessingAgreementService>();
		services.AddScoped<SchoolSignupService>();
		services.AddScoped<INotificationService, NotificationService>();
		services.AddSingleton<UvmTimetableService>();

		services.AddHostedService<ClassChatAttachmentSweeper>();
		services.AddHostedService<AbsenceRetentionJob>();
		services.AddHostedService<SchoolRetentionJob>();

		services.AddOptions<ApplicationOptions>()
			.BindConfiguration(ApplicationOptions.SectionName)
			.ValidateDataAnnotations()
			.ValidateOnStart();

		services.AddProblemDetails();

		services.AddControllers(options => options.Filters.AddService<SubscriptionAccessFilter>())
			.AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

		return services;
	}
}
