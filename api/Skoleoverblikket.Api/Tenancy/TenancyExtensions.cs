namespace Skoleoverblikket.Api.Tenancy;

public static class TenancyExtensions
{
	public static IServiceCollection AddTenancy(this IServiceCollection services)
	{
		services.AddHttpContextAccessor();
		services.AddScoped<HttpTenantContext>();
		services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HttpTenantContext>());
		services.AddScoped<SubscriptionAccessFilter>();
		services.AddScoped<ModuleAccessFilter>();
		services.AddExceptionHandler<MissingTenantClaimExceptionHandler>();

		return services;
	}
}
