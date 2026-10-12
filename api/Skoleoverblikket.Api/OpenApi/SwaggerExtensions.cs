using Microsoft.OpenApi;

namespace Skoleoverblikket.Api.OpenApi;

public static class SwaggerExtensions
{
	public static IServiceCollection AddOpenApi(this IServiceCollection services)
	{
		const string schemeId = BearerSecurityOperationFilter.SchemeId;

		services.AddEndpointsApiExplorer();
		services.AddSwaggerGen(options =>
		{
			options.SwaggerDoc("v1", new OpenApiInfo { Title = "Skoleoverblikket API", Version = "v1" });
			options.CustomSchemaIds(type =>
			{
				if (type.IsNested && type.DeclaringType is not null)
				{
					return $"{type.DeclaringType.Name}_{type.Name}";
				}

				return type.Name;
			});
			options.AddSecurityDefinition(schemeId,
										  new OpenApiSecurityScheme
										  {
											  Name = "Authorization",
											  Type = SecuritySchemeType.Http,
											  Scheme = schemeId,
											  BearerFormat = "JWT",
											  In = ParameterLocation.Header,
										  });

			options.OperationFilter<BearerSecurityOperationFilter>();
			options.SchemaFilter<GroupMessageSchemaFilter>();
			options.SchemaFilter<RequireNonNullableSchemaFilter>();
			options.SupportNonNullableReferenceTypes();
		});

		return services;
	}

	public static IApplicationBuilder UseSwaggerInDevelopment(this WebApplication app)
	{
		if (!app.Environment.IsDevelopment())
		{
			return app;
		}

		app.UseSwagger(c => c.RouteTemplate = "api/v1/openapi/{documentName}/openapi.json");
		app.UseSwaggerUI(c =>
		{
			c.SwaggerEndpoint("/api/v1/openapi/v1/openapi.json", "Skoleoverblikket API v1");
			c.RoutePrefix = "api/v1/openapi";
		});

		return app;
	}
}
