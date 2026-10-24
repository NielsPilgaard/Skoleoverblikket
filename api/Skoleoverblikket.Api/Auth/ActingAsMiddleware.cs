using Microsoft.AspNetCore.Mvc;

namespace Skoleoverblikket.Api.Auth;

/// <summary>
/// Rejects a request whose X-Acting-As header <see cref="SchoolRolesClaimsTransformer"/> couldn't honour.
/// A claims transformation can't write a response, so it records the error and this middleware,
/// right after authentication, turns it into a ProblemDetails.
/// </summary>
public static class ActingAsMiddleware
{
	public static IApplicationBuilder UseActingAs(this IApplicationBuilder app) =>
		app.Use(async (context, next) =>
		{
			if (SchoolRolesClaimsTransformer.For(context)?.Error is not { } error)
			{
				await next(context);
				return;
			}

			var (status, title, detail) = error switch
			{
				ActingAsError.InvalidValue => (StatusCodes.Status400BadRequest, "Ugyldig rolle",
					$"{SchoolRolesClaimsTransformer.ActingAsHeader} skal være staff, parent eller board."),
				_ => (StatusCodes.Status403Forbidden, "Du har ikke denne rolle",
					"Du har ikke denne rolle på skolen. Vælg en anden under \"Vis som\"."),
			};

			context.Response.StatusCode = status;
			await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
			{
				HttpContext = context,
				ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
			});
		});
}
