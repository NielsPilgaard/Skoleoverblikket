using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Skoleoverblikket.Api.OpenApi;

/// <summary>
/// Marks every operation as requiring the bearer token, except [AllowAnonymous] ones. The generated
/// web client calls its auth callback (which redirects to Keycloak login) only for secured operations,
/// so anonymous pages like invitation links must not be marked.
/// </summary>
public sealed class BearerSecurityOperationFilter : IOperationFilter
{
	public const string SchemeId = "Bearer";

	public void Apply(OpenApiOperation operation, OperationFilterContext context)
	{
		if (context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
		{
			return;
		}

		operation.Security ??= [];
		operation.Security.Add(new OpenApiSecurityRequirement
		{
			[new OpenApiSecuritySchemeReference(SchemeId, context.Document)] = [],
		});
	}
}
