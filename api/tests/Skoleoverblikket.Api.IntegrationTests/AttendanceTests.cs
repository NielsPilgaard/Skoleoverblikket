using System.Net;
using System.Net.Http.Json;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using static Skoleoverblikket.Api.IntegrationTests.Infrastructure.AbsenceTestKit;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Daily fremmøde: start of day for every klasse, end of day (half days) for 7.–10. klasse, edits
/// after the fact, parent reports pre-filled and locked, the office overview, and authorization.
/// Each test notes fremmøde for the latest weekday so far in the quarter; on the rare run where the
/// quarter so far is only a weekend there is nothing to note, and the test returns early.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class AttendanceTests(ApiFactory factory)
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

	private static async Task<ClassAttendanceDto> GetAsync(HttpClient client, Guid classId, DateOnly date) =>
		(await client.GetFromJsonAsync<ClassAttendanceDto>(
			$"/api/v1/attendance/classes/{classId}?date={date:yyyy-MM-dd}", JsonOpts))!;

	private static async Task<List<AbsenceRecordDto>> RegisterAsync(HttpClient client) =>
		(await client.GetFromJsonAsync<List<AbsenceRecordDto>>("/api/v1/absence", JsonOpts))!;

	[Test]
	public async Task StartOfDay_AbsentStudentGetsUnauthorized_PresentGetsNothing()
	{
		if (RecentSchoolDay() is not { } day)
		{
			return;
		}

		var classId = await _kit.CreateClassAsync(_admin, "2.a", 2);
		var absent = await _kit.CreateStudentAsync(classId, "Fraværende Elev");
		var present = await _kit.CreateStudentAsync(classId, "Mødt Elev");
		var (teacher, _) = await _kit.TeacherAsync("start-teacher");

		var save = await SaveAttendanceAsync(teacher, classId, day, AttendanceCheckpoint.StartOfDay,
			(absent.Id, AbsenceCategory.Unauthorized));
		await Assert.That(save.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var attendance = await GetAsync(teacher, classId, day);
		await Assert.That(attendance.StartOfDay).IsNotNull();
		await Assert.That(attendance.StartOfDay!.TakenByName).IsEqualTo("Thomas Lærer");
		var absentRow = attendance.Students.Single(s => s.StudentId == absent.Id);
		await Assert.That(absentRow.Morning!.Category).IsEqualTo(AbsenceCategory.Unauthorized);
		await Assert.That(absentRow.Morning.Source).IsEqualTo(AbsenceSource.Staff);
		await Assert.That(attendance.Students.Single(s => s.StudentId == present.Id).Morning).IsNull();

		var register = await RegisterAsync(_admin);
		await Assert.That(register.Count).IsEqualTo(1);
		await Assert.That(register[0].RegisteredByName).IsEqualTo("Thomas Lærer");
	}

	[Test]
	public async Task EditAfterTheFact_LateArrivalRemovesAbsence()
	{
		if (RecentSchoolDay() is not { } day)
		{
			return;
		}

		var classId = await _kit.CreateClassAsync(_admin, "4.a", 4);
		var student = await _kit.CreateStudentAsync(classId);

		await SaveAttendanceAsync(_admin, classId, day, AttendanceCheckpoint.StartOfDay, (student.Id, AbsenceCategory.Unauthorized));
		await SaveAttendanceAsync(_admin, classId, day, AttendanceCheckpoint.StartOfDay, (student.Id, AbsenceCategory.Illness));
		var afterPhoneCall = await RegisterAsync(_admin);
		await Assert.That(afterPhoneCall.Single().Category).IsEqualTo(AbsenceCategory.Illness);

		await SaveAttendanceAsync(_admin, classId, day, AttendanceCheckpoint.StartOfDay);
		await Assert.That((await RegisterAsync(_admin)).Count).IsEqualTo(0);
		await Assert.That((await GetAsync(_admin, classId, day)).StartOfDay).IsNotNull();
	}

	[Test]
	public async Task ParentSickReport_IsPrefilledAndNotOverwrittenByStaff()
	{
		if (RecentSchoolDay() is not { } day)
		{
			return;
		}

		var classId = await _kit.CreateClassAsync(_admin, "5.a", 5);
		var student = await _kit.CreateStudentAsync(classId);
		var parent = await _kit.ParentOfAsync(student.Id, $"parent-{Guid.NewGuid()}");
		await ReportAsync(parent, student.Id, AbsenceCategory.Illness, day);

		var before = (await GetAsync(_admin, classId, day)).Students.Single();
		await Assert.That(before.Morning!.Source).IsEqualTo(AbsenceSource.Parent);
		await Assert.That(before.Morning.Category).IsEqualTo(AbsenceCategory.Illness);

		// The teacher taps the child absent anyway — the parent's report stays the record.
		await SaveAttendanceAsync(_admin, classId, day, AttendanceCheckpoint.StartOfDay, (student.Id, AbsenceCategory.Unauthorized));
		var register = await RegisterAsync(_admin);
		await Assert.That(register.Count).IsEqualTo(1);
		await Assert.That(register[0].Source).IsEqualTo(AbsenceSource.Parent);
		await Assert.That(register[0].Category).IsEqualTo(AbsenceCategory.Illness);

		// Noting everyone present leaves the parent's report alone too.
		await SaveAttendanceAsync(_admin, classId, day, AttendanceCheckpoint.StartOfDay);
		await Assert.That((await RegisterAsync(_admin)).Count).IsEqualTo(1);
	}

	[Test]
	public async Task RejectedLeave_IsOnlyAHint_TeacherRecordsUnauthorized()
	{
		var today = DanishToday();
		if (RecentSchoolDay() != today)
		{
			return; // Leave can only be requested from today on, so this needs a school day today.
		}

		var classId = await _kit.CreateClassAsync(_admin, "6.a", 6);
		var student = await _kit.CreateStudentAsync(classId);
		var parent = await _kit.ParentOfAsync(student.Id, $"parent-{Guid.NewGuid()}");
		await ReportAsync(parent, student.Id, AbsenceCategory.ExtraordinaryLeave, today);
		var leaveId = (await ParentRecordsAsync(parent)).Single().Id;
		await _admin.PostAsync($"/api/v1/absence/{leaveId}/reject", null);

		var row = (await GetAsync(_admin, classId, today)).Students.Single();
		await Assert.That(row.Morning).IsNull();
		await Assert.That(row.ParentReport!.LeaveStatus).IsEqualTo(LeaveStatus.Rejected);

		await SaveAttendanceAsync(_admin, classId, today, AttendanceCheckpoint.StartOfDay, (student.Id, AbsenceCategory.Unauthorized));
		var records = await ParentRecordsAsync(parent);
		await Assert.That(records.Any(r => r.Category == AbsenceCategory.Unauthorized && r.Source == AbsenceSource.Staff)).IsTrue();
	}

	[Test]
	public async Task EndOfDay_Grade7Plus_RecordsHalfDay()
	{
		if (RecentSchoolDay() is not { } day)
		{
			return;
		}

		var classId = await _kit.CreateClassAsync(_admin, "8.a", 8);
		var leftEarly = await _kit.CreateStudentAsync(classId, "Gik Tidligt");
		var absentAllDay = await _kit.CreateStudentAsync(classId, "Syg Hele Dagen");

		await SaveAttendanceAsync(_admin, classId, day, AttendanceCheckpoint.StartOfDay, (absentAllDay.Id, AbsenceCategory.Unauthorized));
		var end = await SaveAttendanceAsync(_admin, classId, day, AttendanceCheckpoint.EndOfDay,
			(leftEarly.Id, AbsenceCategory.Unauthorized), (absentAllDay.Id, AbsenceCategory.Unauthorized));
		await Assert.That(end.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var attendance = await GetAsync(_admin, classId, day);
		await Assert.That(attendance.RequiresEndOfDay).IsTrue();
		await Assert.That(attendance.EndOfDay).IsNotNull();
		await Assert.That(attendance.Students.Single(s => s.StudentId == leftEarly.Id).EndOfDay).IsNotNull();

		var register = await RegisterAsync(_admin);
		await Assert.That(register.Single(r => r.StudentId == leftEarly.Id).HalfDay).IsTrue();
		// Already absent all day, so leaving early adds nothing.
		await Assert.That(register.Single(r => r.StudentId == absentAllDay.Id).HalfDay).IsFalse();
		await Assert.That(register.Count).IsEqualTo(2);
	}

	[Test]
	public async Task EndOfDay_LowerGrade_Returns400()
	{
		if (RecentSchoolDay() is not { } day)
		{
			return;
		}

		var lower = await _kit.CreateClassAsync(_admin, "3.b", 3);
		var noGrade = await _kit.CreateClassAsync(_admin, "Specialklasse");

		var lowerResponse = await SaveAttendanceAsync(_admin, lower, day, AttendanceCheckpoint.EndOfDay);
		var noGradeResponse = await SaveAttendanceAsync(_admin, noGrade, day, AttendanceCheckpoint.EndOfDay);
		await Assert.That(lowerResponse.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(noGradeResponse.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task Overview_Grade7PlusIncompleteUntilBothChecks()
	{
		if (RecentSchoolDay() is not { } day)
		{
			return;
		}

		var lower = await _kit.CreateClassAsync(_admin, "1.a", 1);
		var upper = await _kit.CreateClassAsync(_admin, "9.a", 9);
		// The overview leaves out klasser without students.
		await _kit.CreateStudentAsync(lower);
		await _kit.CreateStudentAsync(upper);

		await SaveAttendanceAsync(_admin, lower, day, AttendanceCheckpoint.StartOfDay);
		await SaveAttendanceAsync(_admin, upper, day, AttendanceCheckpoint.StartOfDay);

		async Task<AttendanceOverviewDto> Overview() =>
			(await _admin.GetFromJsonAsync<AttendanceOverviewDto>($"/api/v1/attendance/overview?date={day:yyyy-MM-dd}", JsonOpts))!;

		var first = await Overview();
		await Assert.That(first.Classes.Single(c => c.ClassId == lower).Complete).IsTrue();
		await Assert.That(first.Classes.Single(c => c.ClassId == upper).Complete).IsFalse();

		await SaveAttendanceAsync(_admin, upper, day, AttendanceCheckpoint.EndOfDay);
		await Assert.That((await Overview()).Classes.Single(c => c.ClassId == upper).Complete).IsTrue();
	}

	[Test]
	public async Task Save_InvalidRequests_Return400()
	{
		if (RecentSchoolDay() is not { } day)
		{
			return;
		}

		var classId = await _kit.CreateClassAsync(_admin, "7.c", 7);
		var otherClassId = await _kit.CreateClassAsync(_admin, "7.d", 7);
		var student = await _kit.CreateStudentAsync(classId);
		var stranger = await _kit.CreateStudentAsync(otherClassId);

		var future = await SaveAttendanceAsync(_admin, classId, DanishToday().AddDays(1), AttendanceCheckpoint.StartOfDay);
		var leave = await SaveAttendanceAsync(_admin, classId, day, AttendanceCheckpoint.StartOfDay, (student.Id, AbsenceCategory.ExtraordinaryLeave));
		var wrongClass = await SaveAttendanceAsync(_admin, classId, day, AttendanceCheckpoint.StartOfDay, (stranger.Id, AbsenceCategory.Unauthorized));

		await Assert.That(future.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(leave.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(wrongClass.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

		await _admin.PostAsJsonAsync("/api/v1/calendar",
			new CalendarController.CreateCalendarEntryRequest("Lukket", CalendarEntryType.Lukkedag, day, day), JsonOpts);
		var closed = await SaveAttendanceAsync(_admin, classId, day, AttendanceCheckpoint.StartOfDay);
		await Assert.That(closed.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That((await GetAsync(_admin, classId, day)).IsSchoolDay).IsFalse();
	}

	[Test]
	public async Task TeacherWithoutClassPermission_ParentAndOverview_AreForbidden()
	{
		var day = RecentSchoolDay() ?? DanishToday();
		var classId = await _kit.CreateClassAsync(_admin, "5.c", 5);
		var student = await _kit.CreateStudentAsync(classId);
		var (_, owner) = await _kit.TeacherAsync("owner-teacher", "Ejer Lærer");
		await _kit.RestrictClassToAsync(classId, owner.Id);
		var (outsider, _) = await _kit.TeacherAsync("outsider-attendance", "Udenforstående");
		var parent = await _kit.ParentOfAsync(student.Id, $"parent-{Guid.NewGuid()}");

		var get = await outsider.GetAsync($"/api/v1/attendance/classes/{classId}?date={day:yyyy-MM-dd}");
		var put = await SaveAttendanceAsync(outsider, classId, day, AttendanceCheckpoint.StartOfDay);
		var parentGet = await parent.GetAsync($"/api/v1/attendance/classes/{classId}?date={day:yyyy-MM-dd}");
		var overview = await outsider.GetAsync("/api/v1/attendance/overview");

		await Assert.That(get.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That(parentGet.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That(overview.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}
}
