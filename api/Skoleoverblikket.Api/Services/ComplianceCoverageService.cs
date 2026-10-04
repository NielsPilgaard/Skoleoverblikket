using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

/// <param name="Status">"green", "yellow", "red" or "missing" against UVM's vejledende timetal, or
/// "extra" for a subject the klasse is taught that UVM has no timetal for at this grade
/// (VejledendeWeeklyHours/VejledendeAnnualHours are then 0).</param>
public sealed record SubjectCoverageDto(string Category, double WeeklyHours, double VejledendeWeeklyHours, double AnnualHours, double VejledendeAnnualHours, string Status);

/// <param name="UnexpectedGradeCategories">Subjects taught beyond UVM's fagrække for this grade.
/// Always empty for børnehaveklassen, which has no fagrække to compare against.</param>
public sealed record ClassCoverageDto(Guid ClassId, string ClassName, int GradeLevel, List<SubjectCoverageDto> Subjects, List<string> UnexpectedGradeCategories);

public sealed record CoverageResponseDto(List<ClassCoverageDto> Classes, int ClassesMissingGradeLevel, int ActiveSchemaCount);

public sealed record CreateCoverageSnapshotRequest([property: MaxLength(500)] string? Reason);

public sealed record CoverageSnapshotSummaryDto(Guid Id, string SchoolYear, DateTimeOffset CreatedAt, string CreatedByStaffName, string? Reason);

public sealed record CoverageSnapshotDetailDto(Guid Id, string SchoolYear, DateTimeOffset CreatedAt, string CreatedByStaffName, string? Reason, CoverageResponseDto Data);

public enum CoverageSnapshotLookup { Found, NotFound, UnsupportedVersion }

/// <summary>
/// Stå mål med: measures each graded klasse's active schema against UVM's vejledende timetal, and
/// owns the saved <see cref="ComplianceCoverageSnapshot"/> versions of that measurement.
/// </summary>
public sealed class ComplianceCoverageService(AppDbContext db, UvmTimetableService timetable, ITenantContext tenant)
{
	private const int CurrentDataVersion = 1;

	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	// School year runs Aug 1 – Jul 31 in Danish local time. Using UTC directly would mislabel
	// snapshots taken in the UTC-early-morning window around the Aug 1 boundary (CEST is UTC+2).
	private static readonly TimeZoneInfo DanishTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");

	public async Task<CoverageResponseDto> GetCoverageAsync(CancellationToken cancellationToken)
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
				// (e.g. Tysk scheduled in 3. klasse — Tysk only starts 6. klasse). Teaching beyond
				// the fagrække is allowed, so these are shown with their hours rather than dropped.
				var extraCategories = hoursPerCategory.Keys
					.Where(category => !timetal.TryGetValue(category.ToString(), out var gradeMap)
						|| !gradeMap.TryGetValue(gradeLevel, out var vejledende)
						|| vejledende <= 0)
					.ToList();

				foreach (var category in extraCategories)
				{
					var actual = hoursPerCategory[category];
					subjects.Add(new SubjectCoverageDto(
						category.ToString(),
						Math.Round(actual, 2),
						0,
						Math.Round(actual * weekCount, 0),
						0,
						"extra"));
				}

				// Børnehaveklassen has no fagrække (it works in kompetenceområder), so nearly
				// everything taught there would be flagged — noise on every school's 0. klasse.
				var unexpectedGradeCategories = gradeLevel == 0
					? []
					: extraCategories.Select(category => category.ToString()).OrderBy(name => name).ToList();

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

	/// <summary>Saves the current coverage as a version. Null when the caller isn't a staff member.</summary>
	public async Task<CoverageSnapshotSummaryDto?> CreateSnapshotAsync(string keycloakSubject, string? reason, CancellationToken cancellationToken)
	{
		var staff = await db.Staff
			.AsNoTracking()
			.Where(s => s.KeycloakSubject == keycloakSubject)
			.Select(s => new { s.Id, s.Name })
			.FirstOrDefaultAsync(cancellationToken);
		if (staff is null)
		{
			return null;
		}

		var coverage = await GetCoverageAsync(cancellationToken);

		var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, DanishTimeZone);
		var schoolYearStart = now.Month >= 8 ? now.Year : now.Year - 1;

		var snapshot = new ComplianceCoverageSnapshot
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			SchoolYear = $"{schoolYearStart}-{schoolYearStart + 1}",
			CreatedByStaffId = staff.Id,
			Reason = reason,
			DataVersion = CurrentDataVersion,
			Data = JsonSerializer.Serialize(coverage),
		};

		db.ComplianceCoverageSnapshots.Add(snapshot);
		await db.SaveChangesAsync(cancellationToken);

		return new CoverageSnapshotSummaryDto(snapshot.Id, snapshot.SchoolYear, snapshot.CreatedAt, staff.Name, snapshot.Reason);
	}

	public Task<List<CoverageSnapshotSummaryDto>> GetSnapshotsAsync(CancellationToken cancellationToken) =>
		db.ComplianceCoverageSnapshots
			.AsNoTracking()
			.OrderByDescending(s => s.CreatedAt)
			.Select(s => new CoverageSnapshotSummaryDto(s.Id, s.SchoolYear, s.CreatedAt, s.CreatedByStaff.Name, s.Reason))
			.ToListAsync(cancellationToken);

	public async Task<(CoverageSnapshotLookup Result, CoverageSnapshotDetailDto? Snapshot, int DataVersion)> GetSnapshotAsync(Guid id, CancellationToken cancellationToken)
	{
		var snapshot = await db.ComplianceCoverageSnapshots
			.AsNoTracking()
			.Where(s => s.Id == id)
			.Select(s => new { s.Id, s.SchoolYear, s.CreatedAt, CreatedByStaffName = s.CreatedByStaff.Name, s.Reason, s.DataVersion, s.Data })
			.FirstOrDefaultAsync(cancellationToken);

		if (snapshot is null)
		{
			return (CoverageSnapshotLookup.NotFound, null, 0);
		}

		// A snapshot saved in a format this version can't read (e.g. by a newer app version)
		if (snapshot.DataVersion != CurrentDataVersion)
		{
			return (CoverageSnapshotLookup.UnsupportedVersion, null, snapshot.DataVersion);
		}

		var data = JsonSerializer.Deserialize<CoverageResponseDto>(snapshot.Data, JsonOptions)
			?? new CoverageResponseDto([], 0, 0);

		return (CoverageSnapshotLookup.Found,
			new CoverageSnapshotDetailDto(snapshot.Id, snapshot.SchoolYear, snapshot.CreatedAt, snapshot.CreatedByStaffName, snapshot.Reason, data),
			snapshot.DataVersion);
	}

	public async Task<bool> DeleteSnapshotAsync(Guid id, CancellationToken cancellationToken)
	{
		var snapshot = await db.ComplianceCoverageSnapshots.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
		if (snapshot is null)
		{
			return false;
		}

		db.ComplianceCoverageSnapshots.Remove(snapshot);
		await db.SaveChangesAsync(cancellationToken);
		return true;
	}
}
