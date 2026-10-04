using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;
using static Skoleoverblikket.Api.Services.DataProcessingAgreementService;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/admin/sub-processor-notice")]
[Authorize(Roles = Roles.SuperAdmin)]
public sealed class SuperAdminSubProcessorNoticeController(DataProcessingAgreementService agreements) : ControllerBase
{
	/// <summary>Emails every school's admins about a sub-processor change at least 30 days ahead.</summary>
	[HttpPost]
	public async Task<ActionResult<SubProcessorNoticeResultDto>> Send(SubProcessorNoticeRequest request, CancellationToken cancellationToken)
	{
		var (outcome, result) = await agreements.SendSubProcessorNoticeAsync(request, DateTimeOffset.UtcNow, cancellationToken);
		return outcome == NoticeOutcome.Sent
			? Ok(result)
			: Problem(
				title: "For kort varsel",
				detail: "Databehandleraftalen lover skolerne mindst 30 dages varsel. Vælg en senere dato.",
				statusCode: StatusCodes.Status400BadRequest);
	}
}
