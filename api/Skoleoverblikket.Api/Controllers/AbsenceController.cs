using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Controllers;

/// <summary>
/// The student absence register (fravær): parent reports, leave approval, category changes,
/// quarterly stats and downloads. Fremmøde lives in <see cref="AttendanceController"/>.
/// </summary>
[ApiController]
[Route("api/v1/absence")]
[Authorize]
public sealed class AbsenceController(
	AbsenceService absence,
	AbsenceStatsService absenceStats,
	IAuthorizationService authorization) : ControllerBase
{
	public record ChangeAbsenceCategoryRequest(AbsenceCategory Category);

	public record MarkParentsInformedRequest(Guid StudentId, int Year, int Quarter);

	// ── Parents ──────────────────────────────────────────────────────────────────

	[HttpPost]
	[Authorize(Roles = Roles.Parent)]
	public async Task<IActionResult> ReportAbsence([FromBody] ReportAbsenceRequest req, CancellationToken cancellationToken)
	{
		var (result, _) = await absence.ReportByParentAsync(User.GetKeycloakSubject(), req, cancellationToken);
		return result switch
		{
			ParentReportResult.Created => CreatedAtAction(nameof(GetMine), new { }, null),
			ParentReportResult.NotYourChild => Forbid(),
			ParentReportResult.InvalidCategory => Problem("Vælg syg eller fri", statusCode: 400),
			ParentReportResult.NoSchoolDays => Problem("Der er ingen skoledage i den valgte periode. Vælg en hverdag uden ferie eller lukkedag.", statusCode: 400),
			_ => Problem("Ugyldige datoer. Fri skal søges før den første dag, og sygdom kan meldes op til 14 dage tilbage.", statusCode: 400),
		};
	}

	[HttpGet("mine")]
	[Authorize(Roles = Roles.Parent)]
	public async Task<ActionResult<IReadOnlyList<AbsenceRecordDto>>> GetMine(CancellationToken cancellationToken) =>
		await absence.GetForParentAsync(User.GetKeycloakSubject(), cancellationToken);

	[HttpDelete("{id:guid}")]
	[Authorize(Roles = Roles.Parent)]
	public async Task<IActionResult> CancelAbsence(Guid id, CancellationToken cancellationToken) =>
		await absence.CancelByParentAsync(User.GetKeycloakSubject(), id, cancellationToken) switch
		{
			ParentCancelResult.Cancelled => NoContent(),
			ParentCancelResult.NotFound => NotFound(),
			_ => Problem("Fraværet er allerede begyndt og kan ikke annulleres. Kontakt skolen.", statusCode: 400),
		};

	// ── Staff ────────────────────────────────────────────────────────────────────

	/// <summary>The register, limited to the classes the caller may register fravær for.</summary>
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<AbsenceRecordDto>>> GetAbsences(
		[FromQuery] Guid? classId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] AbsenceCategory? category,
		CancellationToken cancellationToken)
	{
		var allowed = await absence.GetEditableClassIdsAsync(User.GetKeycloakSubject(), User.IsInRole(Roles.Admin), cancellationToken);
		return await absence.ListAsync(classId, from, to, category, allowed, cancellationToken);
	}

	[HttpPut("{id:guid}/category")]
	[RequiresModule(SubscriptionModule.ParentModule)]
	public async Task<IActionResult> ChangeCategory(Guid id, [FromBody] ChangeAbsenceCategoryRequest req, CancellationToken cancellationToken)
	{
		var classId = await absence.GetRecordClassIdAsync(id, cancellationToken);
		if (classId is null)
		{
			return NotFound();
		}

		if (!(await authorization.AuthorizeAsync(User, classId.Value, new EditClassRequirement())).Succeeded)
		{
			return Forbid();
		}

		return await absence.ChangeCategoryAsync(id, req.Category, cancellationToken) switch
		{
			CategoryChangeResult.Changed => NoContent(),
			CategoryChangeResult.NotFound => NotFound(),
			CategoryChangeResult.NotStaffRegistered => Problem("Fravær meldt af forældre kan ikke ændres her", statusCode: 400),
			CategoryChangeResult.QuarterClosed => Problem("Kvartalet er afsluttet, så fraværet kan ikke længere rettes", statusCode: 400),
			_ => Problem("Vælg sygdom eller ulovligt fravær", statusCode: 400),
		};
	}

	[HttpGet("leave-requests")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<IReadOnlyList<AbsenceRecordDto>>> GetLeaveRequests(CancellationToken cancellationToken) =>
		await absence.GetLeaveRequestsAsync(cancellationToken);

	[HttpPost("{id:guid}/approve")]
	[RequiresModule(SubscriptionModule.ParentModule)]
	[Authorize(Roles = Roles.Admin)]
	public Task<IActionResult> ApproveLeave(Guid id, CancellationToken cancellationToken) =>
		DecideLeave(id, approve: true, cancellationToken);

	[HttpPost("{id:guid}/reject")]
	[RequiresModule(SubscriptionModule.ParentModule)]
	[Authorize(Roles = Roles.Admin)]
	public Task<IActionResult> RejectLeave(Guid id, CancellationToken cancellationToken) =>
		DecideLeave(id, approve: false, cancellationToken);

	[HttpGet("stats")]
	public async Task<ActionResult<QuarterStatsDto>> GetStats(
		[FromQuery] int year, [FromQuery] int quarter, [FromQuery] Guid? classId, CancellationToken cancellationToken)
	{
		if (quarter is < 1 or > 4 || year is < 2000 or > 2100)
		{
			return Problem("Ugyldigt kvartal", statusCode: 400);
		}

		var allowed = await absence.GetEditableClassIdsAsync(User.GetKeycloakSubject(), User.IsInRole(Roles.Admin), cancellationToken);
		return await absenceStats.GetQuarterStatsAsync(year, quarter, classId, allowed, cancellationToken);
	}

	[HttpPost("follow-ups")]
	[RequiresModule(SubscriptionModule.ParentModule)]
	public async Task<IActionResult> MarkParentsInformed([FromBody] MarkParentsInformedRequest req, CancellationToken cancellationToken)
	{
		if (req.Quarter is < 1 or > 4 || req.Year is < 2000 or > 2100)
		{
			return Problem("Ugyldigt kvartal", statusCode: 400);
		}

		var classId = await absence.GetStudentClassIdAsync(req.StudentId, cancellationToken);
		if (classId is null)
		{
			return NotFound();
		}

		if (!(await authorization.AuthorizeAsync(User, classId.Value, new EditClassRequirement())).Succeeded)
		{
			return Forbid();
		}

		return await absence.MarkParentsInformedAsync(req.StudentId, req.Year, req.Quarter, User.GetKeycloakSubject(), cancellationToken)
			? NoContent()
			: NotFound();
	}

	/// <summary>The full register for one school year (2025 = skoleåret 2025/26) as Excel.</summary>
	[HttpGet("export")]
	[Authorize(Roles = Roles.Admin)]
	[Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
	public async Task<IActionResult> ExportSchoolYear([FromQuery] int schoolYear, CancellationToken cancellationToken)
	{
		var current = SchoolDayCalendar.SchoolYearStart(SchoolDayCalendar.Today());
		if (schoolYear < current - 1 || schoolYear > current)
		{
			return Problem("Kun indeværende og forrige skoleår gemmes", statusCode: 400);
		}

		using var wb = await absenceStats.BuildSchoolYearWorkbookAsync(schoolYear, cancellationToken);
		return ExcelReportBuilder.ToXlsx(wb, $"fravaer-{schoolYear}-{(schoolYear + 1) % 100:00}.xlsx");
	}

	/// <summary>Students at or above 15% ulovligt fravær in a quarter, as CSV.</summary>
	[HttpGet("flagged-export")]
	[Authorize(Roles = Roles.Admin)]
	[Produces("text/csv")]
	public async Task<IActionResult> ExportFlagged([FromQuery] int year, [FromQuery] int quarter, CancellationToken cancellationToken)
	{
		if (quarter is < 1 or > 4 || year is < 2000 or > 2100)
		{
			return Problem("Ugyldigt kvartal", statusCode: 400);
		}

		var csv = await absenceStats.BuildFlaggedCsvAsync(year, quarter, cancellationToken);
		return File(csv, "text/csv; charset=utf-8", $"ulovligt-fravaer-15-procent-{year}-k{quarter}.csv");
	}

	private async Task<IActionResult> DecideLeave(Guid id, bool approve, CancellationToken cancellationToken) =>
		await absence.DecideLeaveAsync(id, approve, User.GetKeycloakSubject(), cancellationToken) switch
		{
			LeaveDecisionResult.Decided => NoContent(),
			LeaveDecisionResult.NotFound => NotFound(),
			_ => Problem("Anmodningen er allerede behandlet", statusCode: 400),
		};
}
