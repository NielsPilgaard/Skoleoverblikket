using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Skoleoverblikket.Api.Auth;

public static class AuthExtensions
{
	public static IServiceCollection AddKeycloakAuth(this IServiceCollection services)
	{
		services.AddOptions<KeycloakOptions>()
			   .BindConfiguration(KeycloakOptions.SectionName)
			   .ValidateDataAnnotations()
			   .ValidateOnStart();

		services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
			   .AddJwtBearer(options =>
			   {
				   // Resolve options at configuration time via IOptions<KeycloakOptions>
			   });

		// Configure JwtBearerOptions from KeycloakOptions after the options graph is built
		services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
				.Configure<IOptions<KeycloakOptions>>((jwt, kc) =>
				{
					jwt.Authority = kc.Value.Authority;
					jwt.Audience = kc.Value.Audience;
					jwt.RequireHttpsMetadata = kc.Value.RequireHttpsMetadata;
					jwt.MapInboundClaims = false;
					// User.Identity.Name is the Keycloak id, not a person's name, so error logs
					// (elmah.io, stored in the US) identify the user by id only.
					jwt.TokenValidationParameters.NameClaimType = "sub";

					if (!string.IsNullOrEmpty(kc.Value.MetadataAddress))
					{
						jwt.MetadataAddress = kc.Value.MetadataAddress;
					}
				});

		services.AddAuthorization(opt =>
		{
			opt.AddPolicy(Policies.EditClass, p => p.Requirements.Add(new EditClassRequirement()));
			opt.AddPolicy(Policies.EditWeekPlan, p => p.Requirements.Add(new EditWeekPlanRequirement()));
			opt.AddPolicy(Policies.ParentClassAccess, p => p.Requirements.Add(new ParentClassAccessRequirement()));
			opt.AddPolicy(Policies.SendGroupMessage, p => p.Requirements.Add(new GroupMessageRequirement()));
			opt.AddPolicy(Policies.CanAccessTeacherData, p => p.Requirements.Add(new TeacherDataAccessRequirement()));
		});
		services.AddScoped<IAuthorizationHandler, EditClassAuthorizationHandler>();
		services.AddScoped<IAuthorizationHandler, EditWeekPlanAuthorizationHandler>();
		services.AddScoped<IAuthorizationHandler, ParentClassAccessHandler>();
		services.AddScoped<IAuthorizationHandler, GroupMessageAuthorizationHandler>();
		services.AddScoped<IAuthorizationHandler, TeacherDataAccessHandler>();
		services.AddScoped<IClaimsTransformation, KeycloakRolesClaimsTransformer>();

		return services;
	}
}
