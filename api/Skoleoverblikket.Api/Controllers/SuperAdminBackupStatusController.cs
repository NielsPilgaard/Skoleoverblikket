using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

/// <summary>
/// Read-only backup health for the backoffice card. Every backup action lives in the backup agent's
/// console, which is only reachable over an SSH tunnel (task 60 D4/D6), so there is nothing to POST.
/// </summary>
[ApiController]
[Route("api/v1/admin/backup-status")]
[Authorize(Roles = Roles.SuperAdmin)]
public sealed class SuperAdminBackupStatusController(BackupStatusService backupStatus) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<BackupStatusDto>> Get(CancellationToken cancellationToken)
	{
		var (outcome, status) = await backupStatus.GetAsync(cancellationToken);
		return outcome switch
		{
			BackupStatusService.Outcome.Found => Ok(status),
			BackupStatusService.Outcome.NotConfigured => Problem(
				title: "Backup-status er ikke sat op",
				detail: "API'et har ingen nøgle til ops-bucket'en (BackupStatus__AccessKey).",
				statusCode: StatusCodes.Status404NotFound),
			_ => Problem(
				title: "Ingen backup-status endnu",
				detail: "Backup-agenten har ikke skrevet status.json til ops-bucket'en.",
				statusCode: StatusCodes.Status404NotFound),
		};
	}
}
