using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/compliance-coverage")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Board}")]
public sealed class ComplianceCoverageController(AppDbContext db, UvmTimetableService timetable, ITenantContext tenant) : ControllerBase
{
	public record SubjectCoverageDto(string Category, double WeeklyHours, double VejledendeWeeklyHours, double AnnualHours, double VejledendeAnnualHours, string Status);
	public record ClassCoverageDto(Guid ClassId, string ClassName, int GradeLevel, List<SubjectCoverageDto> Subjects, List<string> UnexpectedGradeCategories);
	public record CoverageResponseDto(List<ClassCoverageDto> Classes, int ClassesMissingGradeLevel, int ActiveSchemaCount);
	public record CreateSnapshotRequest([property: System.ComponentModel.DataAnnotations.MaxLength(500)] string? Reason);
	public record SnapshotSummaryDto(Guid Id, string SchoolYear, DateTimeOffset CreatedAt, string CreatedByStaffName, string? Reason);
	public record SnapshotDetailDto(Guid Id, string SchoolYear, DateTimeOffset CreatedAt, string CreatedByStaffName, string? Reason, CoverageResponseDto Data);

	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	// School year runs Aug 1 – Jul 31 in Danish local time. Using UTC directly would mislabel
	// snapshots taken in the UTC-early-morning window around the Aug 1 boundary (CEST is UTC+2).
	private static readonly TimeZoneInfo DanishTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");

	[HttpGet("coverage")]
	public async Task<ActionResult<CoverageResponseDto>> GetCoverage(CancellationToken cancellationToken)
	{
		var coverage = await ComputeCoverageAsync(cancellationToken);
		return Ok(coverage);
	}

	[HttpPost("snapshots")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<SnapshotSummaryDto>> CreateSnapshot([FromBody] CreateSnapshotRequest req, CancellationToken cancellationToken)
	{
		if (req.Reason is not null && req.Reason.Length > 500)
		{
			return Problem(title: "Ugyldig begrundelse", detail: "Begrundelse må højst være 500 tegn.", statusCode: StatusCodes.Status400BadRequest);
		}

		var subject = User.GetKeycloakSubject();
		var staff = await db.Staff.FirstOrDefaultAsync(s => s.KeycloakSubject == subject, cancellationToken);
		if (staff is null)
		{
			return Forbid();
		}

		var coverage = await ComputeCoverageAsync(cancellationToken);

		var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, DanishTimeZone);
		var schoolYearStart = now.Month >= 8 ? now.Year : now.Year - 1;
		var schoolYear = $"{schoolYearStart}-{schoolYearStart + 1}";

		var snapshot = new StaaMaalMedSnapshot
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			SchoolYear = schoolYear,
			CreatedByStaffId = staff.Id,
			Reason = req.Reason,
			DataVersion = 1,
			Data = JsonSerializer.Serialize(coverage),
		};

		db.StaaMaalMedSnapshots.Add(snapshot);
		await db.SaveChangesAsync(cancellationToken);

		return CreatedAtAction(nameof(GetSnapshot), new { id = snapshot.Id },
			new SnapshotSummaryDto(snapshot.Id, snapshot.SchoolYear, snapshot.CreatedAt, staff.Name, snapshot.Reason));
	}

	[HttpGet("snapshots")]
	public async Task<ActionResult<List<SnapshotSummaryDto>>> GetSnapshots(CancellationToken cancellationToken)
	{
		var snapshots = await db.StaaMaalMedSnapshots
			.AsNoTracking()
			.OrderByDescending(s => s.CreatedAt)
			.Select(s => new SnapshotSummaryDto(s.Id, s.SchoolYear, s.CreatedAt, s.CreatedByStaff.Name, s.Reason))
			.ToListAsync(cancellationToken);

		return Ok(snapshots);
	}

	[HttpGet("snapshots/{id:guid}")]
	public async Task<ActionResult<SnapshotDetailDto>> GetSnapshot(Guid id, CancellationToken cancellationToken)
	{
		var snapshot = await db.StaaMaalMedSnapshots
			.AsNoTracking()
			.Include(s => s.CreatedByStaff)
			.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

		if (snapshot is null)
		{
			return NotFound();
		}

		if (snapshot.DataVersion != 1)
		{
			// Not a server error: the snapshot itself is in an unsupported format (e.g. saved
			// by a newer app version). A 500 here would be indistinguishable from a transient
			// failure to the frontend, hiding a compliance record right when someone needs it.
			return Problem(
				title: "Snapshot-version understøttes ikke",
				detail: $"Denne version blev gemt i et format, der ikke længere understøttes (version {snapshot.DataVersion}).",
				statusCode: StatusCodes.Status409Conflict);
		}

		var data = JsonSerializer.Deserialize<CoverageResponseDto>(snapshot.Data, JsonOptions)
			?? new CoverageResponseDto([], 0, 0);

		return Ok(new SnapshotDetailDto(snapshot.Id, snapshot.SchoolYear, snapshot.CreatedAt, snapshot.CreatedByStaff.Name, snapshot.Reason, data));
	}

	[HttpDelete("snapshots/{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<IActionResult> DeleteSnapshot(Guid id, CancellationToken cancellationToken)
	{
		var snapshot = await db.StaaMaalMedSnapshots.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
		if (snapshot is null)
		{
			return NotFound();
		}

		db.StaaMaalMedSnapshots.Remove(snapshot);
		await db.SaveChangesAsync(cancellationToken);

		return NoContent();
	}

	private async Task<CoverageResponseDto> ComputeCoverageAsync(CancellationToken cancellationToken)
	{
		var timetal = timetable.Load();

		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var activeSlots = await db.SchemaSlots
			.AsNoTrackingWithIdentityResolution()
			.Where(s => s.Schema.StartDate <= today && s.Schema.EndDate >= today)
			.Include(s => s.Course)
			.Include(s => s.Schema).ThenInclude(sc => sc.Class)
			.Include(s => s.TimeSlot)
			.ToListAsync(cancellationToken);

		var holidays = await db.CalendarEntries
			.AsNoTracking()
			.Where(e => e.Type == CalendarEntryType.Ferie || e.Type == CalendarEntryType.Lukkedag)
			.ToListAsync(cancellationToken);

		var classes = activeSlots
			.Where(s => s.Schema.Class.GradeLevel.HasValue && s.Course.Category.HasValue && s.Course.Category != SubjectCategory.Fri)
			.GroupBy(s => (s.Schema.ClassId, s.Schema.Class.Name, GradeLevel: s.Schema.Class.GradeLevel!.Value, s.Schema.StartDate, s.Schema.EndDate))
			.Select(classGroup =>
			{
				var gradeLevel = classGroup.Key.GradeLevel;
				var weekCount = SchoolWeekCalculator.CountSchoolWeeks(classGroup.Key.StartDate, classGroup.Key.EndDate, holidays);
				var hoursPerCategory = classGroup
					.GroupBy(s => s.Course.Category!.Value)
					.ToDictionary(g => g.Key, g => g.Sum(s => (s.TimeSlot.EndTime - s.TimeSlot.StartTime).TotalHours));

				var subjects = new List<SubjectCoverageDto>();
				foreach (var (categoryName, gradeMap) in timetal)
				{
					if (!gradeMap.TryGetValue(gradeLevel, out var vejledende) || vejledende <= 0)
					{
						continue;
					}

					if (!Enum.TryParse<SubjectCategory>(categoryName, out var category))
					{
						continue;
					}

					var actual = hoursPerCategory.GetValueOrDefault(category, 0.0);
					var status = actual == 0 ? "missing"
						: actual < vejledende * 0.75 ? "red"
						: actual < vejledende ? "yellow"
						: "green";

					subjects.Add(new SubjectCoverageDto(
						categoryName,
						Math.Round(actual, 2),
						Math.Round(vejledende, 2),
						Math.Round(actual * weekCount, 0),
						Math.Round(vejledende * weekCount, 0),
						status));
				}

				// Categories taught at this grade that UVM doesn't define for it at all
				// (e.g. Tysk scheduled in 3. klasse — Tysk only starts 6. klasse).
				var unexpectedGradeCategories = hoursPerCategory.Keys
					.Where(category => !timetal.TryGetValue(category.ToString(), out var gradeMap)
						|| !gradeMap.TryGetValue(gradeLevel, out var vejledende)
						|| vejledende <= 0)
					.Select(category => category.ToString())
					.OrderBy(name => name)
					.ToList();

				return new ClassCoverageDto(
					classGroup.Key.ClassId,
					classGroup.Key.Name,
					gradeLevel,
					subjects.OrderBy(s => s.Category).ToList(),
					unexpectedGradeCategories);
			})
			.ToList();

		// Also include classes with a grade but no active schema slots (all missing)
		var classesWithSlots = classes.Select(c => c.ClassId).ToHashSet();
		var allGradedClasses = await db.Classes
			.AsNoTracking()
			.Where(c => c.GradeLevel.HasValue && !classesWithSlots.Contains(c.Id))
			.ToListAsync(cancellationToken);

		// No schema means no StartDate/EndDate to compute real school weeks from — fall back to
		// SchoolWeekCalculator's own null-input default so this stays a single source of truth
		// instead of a second hardcoded week count that can drift from the real one.
		var fallbackWeekCount = SchoolWeekCalculator.CountSchoolWeeks(null, null, holidays);

		foreach (var cls in allGradedClasses)
		{
			var gradeLevel = cls.GradeLevel!.Value;
			var subjects = new List<SubjectCoverageDto>();
			foreach (var (categoryName, gradeMap) in timetal)
			{
				if (!gradeMap.TryGetValue(gradeLevel, out var vejledende) || vejledende <= 0)
				{
					continue;
				}

				subjects.Add(new SubjectCoverageDto(categoryName, 0.0, Math.Round(vejledende, 2), 0.0, Math.Round(vejledende * fallbackWeekCount, 0), "missing"));
			}

			if (subjects.Count > 0)
			{
				classes.Add(new ClassCoverageDto(cls.Id, cls.Name, gradeLevel, subjects.OrderBy(s => s.Category).ToList(), []));
			}
		}

		classes.Sort((a, b) =>
		{
			var g = a.GradeLevel.CompareTo(b.GradeLevel);
			return g != 0 ? g : string.Compare(a.ClassName, b.ClassName, StringComparison.Ordinal);
		});

		// Diagnostics for the empty state: a class with no klassetrin can't be checked against
		// UVM's fagrække, so it silently drops out above. Surface that so admins know to fix it.
		var classesMissingGradeLevel = await db.Classes
			.AsNoTracking()
			.CountAsync(c => c.GradeLevel == null, cancellationToken);
		var activeSchemaCount = await db.Schemas
			.AsNoTracking()
			.CountAsync(s => s.StartDate <= today && s.EndDate >= today, cancellationToken);

		return new CoverageResponseDto(classes, classesMissingGradeLevel, activeSchemaCount);
	}
}
