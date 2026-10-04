using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using static Skoleoverblikket.Api.IntegrationTests.Infrastructure.AbsenceTestKit;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Quarterly fravær stats: school days from the calendar (incl. recurrence), the ulovligt share
/// with half days, the 10%/15% flags, "forældre orienteret", the 15% CSV and the school-year
/// Excel download. Each test shapes the current quarter's calendar so it has an exact number of
/// school days, which makes the percentages independent of when the tests run.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class AbsenceStatsTests(ApiFactory factory)
{
	private AbsenceTestKit _kit = null!;
	private HttpClient _admin = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		_kit = new AbsenceTestKit(factory);
		await _kit.InitAsync();
		_admin = await _kit.AdminAsync();
	}

	private async Task CreateCalendarAsync(string title, CalendarEntryType type, DateOnly start, DateOnly end,
		string? recurrence = null, DateOnly? recurrenceEnd = null)
	{
		var response = await _admin.PostAsJsonAsync("/api/v1/calendar",
			new CalendarController.CreateCalendarEntryRequest(title, type, start, end, recurrence, recurrenceEnd), JsonOpts);
		response.EnsureSuccessStatusCode();
	}

	[Test]
	public async Task SchoolDays_ExcludeFerieLukkedagArbejdsdag_IncludingRecurrence()
	{
		var today = DanishToday();
		var weekdays = WeekdaysInQuarter(today).ToList();
		var baseline = await StatsAsync(_admin, today);
		await Assert.That(baseline.SchoolDaysInQuarter).IsEqualTo(weekdays.Count);

		var wednesdays = weekdays.Where(d => d.DayOfWeek == DayOfWeek.Wednesday).ToList();
		var mondays = weekdays.Where(d => d.DayOfWeek == DayOfWeek.Monday).ToList();
		var ferieStart = mondays[1];
		var ferieEnd = ferieStart.AddDays(4);
		var arbejdsdag = mondays[^1];
		var begivenhed = mondays[^2];

		await CreateCalendarAsync("Efterårsferie", CalendarEntryType.Ferie, ferieStart, ferieEnd);
		await CreateCalendarAsync("Planlægningsdag", CalendarEntryType.Arbejdsdag, arbejdsdag, arbejdsdag);
		await CreateCalendarAsync("Motionsdag", CalendarEntryType.Begivenhed, begivenhed, begivenhed);
		await CreateCalendarAsync("Lukket onsdag", CalendarEntryType.Lukkedag, wednesdays[0], wednesdays[0],
			"FREQ=WEEKLY", QuarterEnd(today));

		var closed = weekdays
			.Where(d => (d >= ferieStart && d <= ferieEnd) || d == arbejdsdag || d.DayOfWeek == DayOfWeek.Wednesday)
			.ToHashSet();

		var stats = await StatsAsync(_admin, today);
		await Assert.That(stats.SchoolDaysInQuarter).IsEqualTo(weekdays.Count - closed.Count);
		await Assert.That(stats.SchoolDaysSoFar).IsEqualTo(weekdays.Count(d => d <= today && !closed.Contains(d)));
	}

	[Test]
	public async Task TenPercentUnauthorized_FlagsStudent_AndCanBeMarkedInformed()
	{
		if (RecentSchoolDay() is not { } day)
		{
			return;
		}

		await _kit.ShapeQuarterAsync(_admin, day, schoolDays: 10);
		var classId = await _kit.CreateClassAsync(_admin, "6.b", 6);
		var flagged = await _kit.CreateStudentAsync(classId, "Ulovlig Elev");
		var fine = await _kit.CreateStudentAsync(classId, "Syg Elev");
		var parent = await _kit.ParentOfAsync(fine.Id, $"parent-{Guid.NewGuid()}");
		await ReportAsync(parent, fine.Id, AbsenceCategory.Illness, day);
		await SaveAttendanceAsync(_admin, classId, day, AttendanceCheckpoint.StartOfDay, (flagged.Id, AbsenceCategory.Unauthorized));

		var stats = await StatsAsync(_admin, day);
		await Assert.That(stats.SchoolDaysInQuarter).IsEqualTo(10);

		var flaggedRow = stats.Students.Single(s => s.StudentId == flagged.Id);
		await Assert.That(flaggedRow.UnauthorizedDays).IsEqualTo(1m);
		await Assert.That(flaggedRow.UnauthorizedPercent).IsEqualTo(10m);
		await Assert.That(flaggedRow.Flag).IsEqualTo(AbsenceFlag.TenPercent);
		await Assert.That(flaggedRow.ParentsInformedAt).IsNull();

		// Sickness counts as absence but never towards the ulovligt share.
		var fineRow = stats.Students.Single(s => s.StudentId == fine.Id);
		await Assert.That(fineRow.IllnessDays).IsEqualTo(1m);
		await Assert.That(fineRow.Flag).IsEqualTo(AbsenceFlag.None);
		await Assert.That(stats.Classes.Single().FlaggedStudents).IsEqualTo(1);

		var (year, quarter) = QuarterOf(day);
		var mark = await _admin.PostAsJsonAsync("/api/v1/absence/follow-ups",
			new AbsenceController.MarkParentsInformedRequest(flagged.Id, year, quarter), JsonOpts);
		await Assert.That(mark.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var after = await StatsAsync(_admin, day);
		await Assert.That(after.Students.Single(s => s.StudentId == flagged.Id).ParentsInformedAt).IsNotNull();
	}

	[Test]
	public async Task FifteenPercentUnauthorized_FlagsCritical_AndIsInCsv()
	{
		if (RecentSchoolDay() is not { } day)
		{
			return;
		}

		await _kit.ShapeQuarterAsync(_admin, day, schoolDays: 6);
		var classId = await _kit.CreateClassAsync(_admin, "8.c", 8);
		var student = await _kit.CreateStudentAsync(classId, "Kritisk Elev");
		await SaveAttendanceAsync(_admin, classId, day, AttendanceCheckpoint.StartOfDay, (student.Id, AbsenceCategory.Unauthorized));

		var row = (await StatsAsync(_admin, day)).Students.Single(s => s.StudentId == student.Id);
		await Assert.That(row.Flag).IsEqualTo(AbsenceFlag.FifteenPercent);
		await Assert.That(row.UnauthorizedPercent).IsEqualTo(16.7m);

		var (year, quarter) = QuarterOf(day);
		var csv = await _admin.GetAsync($"/api/v1/absence/flagged-export?year={year}&quarter={quarter}");
		await Assert.That(csv.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(csv.Content.Headers.ContentType!.MediaType).IsEqualTo("text/csv");
		await Assert.That(await csv.Content.ReadAsStringAsync()).Contains("Kritisk Elev");

		var (teacher, _) = await _kit.TeacherAsync("csv-teacher");
		var forbidden = await teacher.GetAsync($"/api/v1/absence/flagged-export?year={year}&quarter={quarter}");
		await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task HalfDay_CountsAsHalf()
	{
		if (RecentSchoolDay() is not { } day)
		{
			return;
		}

		await _kit.ShapeQuarterAsync(_admin, day, schoolDays: 4);
		var classId = await _kit.CreateClassAsync(_admin, "9.c", 9);
		var student = await _kit.CreateStudentAsync(classId, "Halv Dag");
		await SaveAttendanceAsync(_admin, classId, day, AttendanceCheckpoint.StartOfDay);
		await SaveAttendanceAsync(_admin, classId, day, AttendanceCheckpoint.EndOfDay, (student.Id, AbsenceCategory.Unauthorized));

		var row = (await StatsAsync(_admin, day)).Students.Single(s => s.StudentId == student.Id);
		await Assert.That(row.UnauthorizedDays).IsEqualTo(0.5m);
		await Assert.That(row.UnauthorizedPercent).IsEqualTo(12.5m);
		await Assert.That(row.Flag).IsEqualTo(AbsenceFlag.TenPercent);
	}

	[Test]
	public async Task Stats_TeacherSeesOnlyOwnClasses()
	{
		var mine = await _kit.CreateClassAsync(_admin, "2.c", 2);
		var theirs = await _kit.CreateClassAsync(_admin, "2.d", 2);
		var myStudent = await _kit.CreateStudentAsync(mine, "Min Elev");
		var theirStudent = await _kit.CreateStudentAsync(theirs, "Anden Elev");
		var (teacher, me) = await _kit.TeacherAsync("stats-teacher");
		var (_, other) = await _kit.TeacherAsync("stats-other", "Anden Lærer");
		await _kit.RestrictClassToAsync(mine, me.Id);
		await _kit.RestrictClassToAsync(theirs, other.Id);

		var stats = await StatsAsync(teacher, DanishToday());
		await Assert.That(stats.Students.Any(s => s.StudentId == myStudent.Id)).IsTrue();
		await Assert.That(stats.Students.Any(s => s.StudentId == theirStudent.Id)).IsFalse();
	}

	[Test]
	public async Task SchoolYearExcel_AdminOnly_RetainedYearsOnly_OwnTenantOnly()
	{
		var today = SickDay();
		var schoolYear = today.Month >= 8 ? today.Year : today.Year - 1;
		var classId = await _kit.CreateClassAsync(_admin, "4.b", 4);
		var student = await _kit.CreateStudentAsync(classId, "Egen Elev");
		var parent = await _kit.ParentOfAsync(student.Id, $"parent-{Guid.NewGuid()}");
		await ReportAsync(parent, student.Id, AbsenceCategory.Illness, today);

		var otherKit = new AbsenceTestKit(factory);
		await otherKit.InitAsync();
		var otherAdmin = await otherKit.AdminAsync("excel-other-admin");
		var otherClass = await otherKit.CreateClassAsync(otherAdmin, "4.b", 4);
		var otherStudent = await otherKit.CreateStudentAsync(otherClass, "Fremmed Elev");
		var otherParent = await otherKit.ParentOfAsync(otherStudent.Id, $"parent-{Guid.NewGuid()}");
		await ReportAsync(otherParent, otherStudent.Id, AbsenceCategory.Illness, today);

		var response = await _admin.GetAsync($"/api/v1/absence/export?schoolYear={schoolYear}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
		var names = workbook.Worksheet("Fravær").CellsUsed().Select(c => c.GetString()).ToList();
		await Assert.That(names).Contains("Egen Elev");
		await Assert.That(names).DoesNotContain("Fremmed Elev");
		await Assert.That(workbook.Worksheets.Any(w => w.Name == "Opsummering")).IsTrue();

		var expired = await _admin.GetAsync($"/api/v1/absence/export?schoolYear={schoolYear - 2}");
		await Assert.That(expired.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

		var (teacher, _) = await _kit.TeacherAsync("excel-teacher");
		var forbidden = await teacher.GetAsync($"/api/v1/absence/export?schoolYear={schoolYear}");
		await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}
}
