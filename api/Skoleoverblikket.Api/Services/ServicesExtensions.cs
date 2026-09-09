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
		services.AddScoped<INotificationService, NotificationService>();
		services.AddSingleton<UvmTimetableService>();

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
