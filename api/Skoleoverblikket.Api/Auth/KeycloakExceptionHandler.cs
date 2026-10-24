using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Skoleoverblikket.Api.Auth;

/// <summary>
/// A <see cref="KeycloakException"/> no service caught means Keycloak is down or refused a call we
/// cannot work around. Report it as 503 so pages can ask the person to try again, not as a broken link.
/// </summary>
public sealed class KeycloakExceptionHandler : IExceptionHandler
{
	public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken cancellationToken)
	{
		if (ex is not KeycloakException)
		{
			return false;
		}

		var problem = new ProblemDetails
		{
			Type = "https://tools.ietf.org/html/rfc9110#section-15.6.4",
			Title = "Tjenesten er midlertidigt utilgængelig",
			Status = StatusCodes.Status503ServiceUnavailable,
			Detail = "Prøv igen om lidt.",
		};

		ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
		await ctx.Response.WriteAsJsonAsync(problem, cancellationToken);
		return true;
	}
}
