using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Every endpoint must say who may call it. A bare <c>[Authorize]</c> lets in any login: parents, board
/// members and logins with no school role at all. So an endpoint must name roles or a policy, or be listed
/// below with the reason it is safe. Adding to a list is a security decision: check the endpoint first.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class EndpointAuthorizationTests(ApiFactory factory)
{
	/// <summary>Endpoints anyone may call without logging in.</summary>
	private static readonly HashSet<string> Anonymous =
	[
		// Liveness and readiness probes.
		"* /alive",
		"* /health",
		"* /api/health",

		// Invitation links: the 256-bit token in the link is the credential.
		"GET /api/v1/board-invitations/preview",
		"POST /api/v1/board-invitations/password",
		"POST /api/v1/board-invitations/{token}/accept",
		"GET /api/v1/parent-invitations/preview",
		"POST /api/v1/parent-invitations/password",
		"GET /api/v1/staff-invitations/preview",
		"POST /api/v1/staff-invitations/password",

		// Public website forms, rate limited per IP.
		"POST /api/v1/demo-request",
		"POST /api/v1/tenants",

		// Verified with the Stripe signature.
		"POST /api/v1/stripe/webhook",
	];

	/// <summary>
	/// Endpoints any login may call, whatever its school role. Each either returns only the caller's own
	/// data, returns harmless school reference data, or checks roles in the action.
	/// </summary>
	private static readonly HashSet<string> AnyLogin =
	[
		// The caller's own data.
		"GET /api/v1/me",
		"GET /api/v1/modules",
		"GET /api/v1/notifications",
		"POST /api/v1/notifications/{id:guid}/read",
		"POST /api/v1/notifications/read-all",
		"GET /api/v1/notification-preferences",
		"PUT /api/v1/notification-preferences",
		"GET /api/v1/stats/my-dashboard",
		"POST /api/v1/staff/me/avatar/presign",
		"POST /api/v1/staff/me/avatar/confirm",

		// Accepting an invitation, before the login has a school role.
		"POST /api/v1/parent-invitations/accept",
		"POST /api/v1/staff-invitations/accept",

		// School reference data that parents' and board views show too.
		"GET /api/v1/calendar",
		"GET /api/v1/calendar/defaults",
		"GET /api/v1/calendar/export.ics",
		"GET /api/v1/courses",
		"GET /api/v1/courses/{id:guid}",
		"GET /api/v1/rooms",
		"GET /api/v1/rooms/{id:guid}",
		"GET /api/v1/time-slots",
		"GET /api/v1/time-slot-template",
		"GET /api/v1/classes/{classId:guid}/time-slots",
		"GET /api/v1/classes/{classId:guid}/schemas/{schemaId:guid}/time-slots",

		// Checked in the action: links for the role the caller acts as.
		"GET /api/v1/external-links",

		// Checked in the action: CanAccessTeacherData for board, ClassPermission for staff.
		"GET /api/v1/classes",
		"GET /api/v1/classes/{id:guid}",

		// Checked in the action: CanAccessTeacherData for board, ParentClassAccess for parents.
		"GET /api/v1/classes/{classId:guid}/schedule",

		// Checked in the action: EditClass.
		"POST /api/v1/classes/{classId:guid}/schemas",
		"DELETE /api/v1/classes/{classId:guid}/schemas/{schemaId:guid}",
		"POST /api/v1/classes/{classId:guid}/schemas/{schemaId:guid}/copy",
		"POST /api/v1/classes/{classId:guid}/schemas/{schemaId:guid}/copy-to/{targetClassId:guid}",
		"PUT /api/v1/classes/{classId:guid}/schemas/{schemaId:guid}/daterange",
		"PUT /api/v1/classes/{classId:guid}/schemas/{schemaId:guid}/rename",
		"PUT /api/v1/classes/{classId:guid}/schemas/{schemaId:guid}/slots",
		"DELETE /api/v1/classes/{classId:guid}/schemas/{schemaId:guid}/slots/{timeSlotId:guid}/{weekday:int}",

		// Checked in the action: EditWeekPlan.
		"PUT /api/v1/classes/{classId:guid}/week-plan/notes",
		"PUT /api/v1/classes/{classId:guid}/week-plan/slots",
		"POST /api/v1/classes/{classId:guid}/week-plan/slots/{slotId:guid}/files",
		"DELETE /api/v1/classes/{classId:guid}/week-plan/slots/{slotId:guid}/files/{fileId:guid}",
	];

	[Test]
	public async Task EveryEndpoint_NamesWhoMayCallIt()
	{
		var unlisted = new List<string>();
		var stale = new HashSet<string>(Anonymous.Concat(AnyLogin));

		foreach (var endpoint in factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
		{
			var access = AccessOf(endpoint);
			var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"];
			foreach (var method in methods)
			{
				var name = $"{method} /{endpoint.RoutePattern.RawText?.TrimStart('/')}";
				stale.Remove(name);
				var allowed = access switch
				{
					Access.Restricted => true,
					Access.AnyLogin => AnyLogin.Contains(name),
					_ => Anonymous.Contains(name),
				};
				if (!allowed)
				{
					unlisted.Add($"{name} ({access})");
				}
			}
		}

		await Assert.That(unlisted).IsEmpty()
			.Because("an endpoint open to anyone or to any login must name roles or a policy, or be reviewed and listed here");
		await Assert.That(stale).IsEmpty().Because("listed endpoints that no longer exist should be removed");
	}

	private enum Access { Anonymous, AnyLogin, Restricted }

	private static Access AccessOf(Endpoint endpoint)
	{
		if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
		{
			return Access.Anonymous;
		}

		var authorize = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
		return authorize.Count == 0 ? Access.Anonymous
			: authorize.Any(a => !string.IsNullOrEmpty(a.Roles) || !string.IsNullOrEmpty(a.Policy)) ? Access.Restricted
			: Access.AnyLogin;
	}
}
