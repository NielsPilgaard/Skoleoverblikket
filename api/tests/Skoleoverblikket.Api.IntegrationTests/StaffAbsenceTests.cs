using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using static Skoleoverblikket.Api.IntegrationTests.Infrastructure.AbsenceTestKit;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Staff fravær and vikardækning: self-report and report-on-behalf, the affected lektioner with
/// ranked candidates (vikarer first; busy, already-covering and absent staff left out), assigning
/// and clearing a vikar, the race between two admins booking the same vikar, and who may do what.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class StaffAbsenceTests(ApiFactory factory)
{
	private sealed record CreatedDto(Guid Id);

	private AbsenceTestKit _kit = null!;
	private HttpClient _admin = null!;
	private DateOnly _monday;
	private TimeSlot _first = null!;
	private TimeSlot _second = null!;
	private Course _course = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		_kit = new AbsenceTestKit(factory);
		await _kit.InitAsync();
		_admin = await _kit.AdminAsync();
		var today = DanishToday();
		_monday = Enumerable.Range(1, 7).Select(today.AddDays).First(d => d.DayOfWeek == DayOfWeek.Monday);
		_first = await TestDataBuilder.CreateTimeSlotAsync(factory.Services, _kit.TenantId, new TimeOnly(8, 0), new TimeOnly(8, 45), 1);
		_second = await TestDataBuilder.CreateTimeSlotAsync(factory.Services, _kit.TenantId, new TimeOnly(9, 0), new TimeOnly(9, 45), 2);
		_course = await TestDataBuilder.CreateCourseAsync(factory.Services, _kit.TenantId, "Dansk");
	}

	/// <summary>A klasse whose Monday lektioner at the given time slots are taught by <paramref name="teacher"/>.</summary>
	private async Task<(Guid ClassId, List<SchemaSlot> Slots)> TeachesOnMondayAsync(Staff teacher, string className, params TimeSlot[] timeSlots)
	{
		var (klass, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(factory.Services, _kit.TenantId, className);
		var slots = new List<SchemaSlot>();
		foreach (var ts in timeSlots)
		{
			slots.Add(await TestDataBuilder.CreateSchemaSlotAsync(
				factory.Services, _kit.TenantId, schema.Id, ts.Id, _course.Id, teacher.Id, DayOfWeek.Monday));
		}

		return (klass.Id, slots);
	}

	private static async Task<Guid> ReportAsync(HttpClient client, Guid? staffId, DateOnly date)
	{
		var response = await client.PostAsJsonAsync("/api/v1/staff-absences",
			new ReportStaffAbsenceRequest(staffId, date, null, "Syg"), JsonOpts);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		return (await response.Content.ReadFromJsonAsync<CreatedDto>(JsonOpts))!.Id;
	}

	private async Task<StaffAbsenceDetailDto> DetailAsync(Guid id) =>
		(await _admin.GetFromJsonAsync<StaffAbsenceDetailDto>($"/api/v1/staff-absences/{id}", JsonOpts))!;

	private Task<HttpResponseMessage> AssignAsync(Guid absenceId, Guid schemaSlotId, Guid? staffId) =>
		_admin.PutAsJsonAsync($"/api/v1/staff-absences/{absenceId}/substitute",
			new AssignAbsenceSubstituteRequest(schemaSlotId, _monday, staffId), JsonOpts);

	[Test]
	public async Task SelfReport_AffectedLessons_RankedCandidates()
	{
		var (absentClient, absent) = await _kit.TeacherAsync("absent-teacher", "Syg Lærer");
		var vikar = await TestDataBuilder.CreateStaffAsync(factory.Services, _kit.TenantId, "Øvrig Vikar", StaffRole.Substitute);
		var busy = await TestDataBuilder.CreateStaffAsync(factory.Services, _kit.TenantId, "Optaget Lærer");
		var free = await TestDataBuilder.CreateStaffAsync(factory.Services, _kit.TenantId, "Ledig Lærer");
		var alsoAbsent = await TestDataBuilder.CreateStaffAsync(factory.Services, _kit.TenantId, "Også Syg");
		await TeachesOnMondayAsync(absent, "3.a", _first, _second);
		await TeachesOnMondayAsync(busy, "4.a", _first);
		await ReportAsync(_admin, alsoAbsent.Id, _monday);

		var id = await ReportAsync(absentClient, null, _monday);

		var mine = await absentClient.GetFromJsonAsync<List<StaffAbsenceDto>>("/api/v1/staff-absences/mine", JsonOpts);
		await Assert.That(mine!.Single().AffectedLessonCount).IsEqualTo(2);
		await Assert.That(mine.Single().CoveredLessonCount).IsEqualTo(0);
		await Assert.That(mine.Single().CanDelete).IsTrue();

		var detail = await DetailAsync(id);
		await Assert.That(detail.Lessons.Count).IsEqualTo(2);
		var firstLesson = detail.Lessons.Single(l => l.StartTime == _first.StartTime);
		var candidates = firstLesson.Candidates.Select(c => c.Id).ToList();
		await Assert.That(candidates[0]).IsEqualTo(vikar.Id);
		await Assert.That(candidates).Contains(free.Id);
		await Assert.That(candidates).DoesNotContain(absent.Id);
		await Assert.That(candidates).DoesNotContain(busy.Id);
		await Assert.That(candidates).DoesNotContain(alsoAbsent.Id);

		// Busy at 8:00 only — free for the 9:00 lektion.
		var secondLesson = detail.Lessons.Single(l => l.StartTime == _second.StartTime);
		await Assert.That(secondLesson.Candidates.Select(c => c.Id)).Contains(busy.Id);
	}

	[Test]
	public async Task AssignVikar_ShowsInUgeplanAndForVikar_ThenClear()
	{
		var (_, absent) = await _kit.TeacherAsync("assign-absent");
		var (vikarClient, vikar) = await _kit.TeacherAsync("assign-vikar", "Vikar Hansen");
		var (classId, slots) = await TeachesOnMondayAsync(absent, "5.a", _first);
		var id = await ReportAsync(_admin, absent.Id, _monday);

		var assign = await AssignAsync(id, slots[0].Id, vikar.Id);
		await Assert.That(assign.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var lesson = (await DetailAsync(id)).Lessons.Single();
		await Assert.That(lesson.SubstituteName).IsEqualTo("Vikar Hansen");
		var list = await _admin.GetFromJsonAsync<List<StaffAbsenceDto>>(
			$"/api/v1/staff-absences?from={_monday:yyyy-MM-dd}&to={_monday:yyyy-MM-dd}", JsonOpts);
		await Assert.That(list!.Single(a => a.Id == id).CoveredLessonCount).IsEqualTo(1);

		var isoDate = _monday.ToDateTime(TimeOnly.MinValue);
		var plan = await _admin.GetFromJsonAsync<WeekPlanController.WeekPlanDto>(
			$"/api/v1/classes/{classId}/week-plan?isoYear={ISOWeek.GetYear(isoDate)}&isoWeek={ISOWeek.GetWeekOfYear(isoDate)}", JsonOpts);
		await Assert.That(plan!.Slots.Single(s => s.SchemaSlotId == slots[0].Id).SubstituteTeacherName).IsEqualTo("Vikar Hansen");

		var mySubs = await vikarClient.GetFromJsonAsync<List<MySubstitutionDto>>(
			$"/api/v1/substitutions/mine?from={_monday:yyyy-MM-dd}&to={_monday:yyyy-MM-dd}", JsonOpts);
		await Assert.That(mySubs!.Single().ClassName).IsEqualTo("5.a");

		var clear = await AssignAsync(id, slots[0].Id, null);
		await Assert.That(clear.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That((await DetailAsync(id)).Lessons.Single().SubstituteId).IsNull();
	}

	[Test]
	public async Task AssignVikar_AlreadyCoveringSameTime_Returns409()
	{
		var (_, absentA) = await _kit.TeacherAsync("busy-absent-a", "Syg A");
		var (_, absentB) = await _kit.TeacherAsync("busy-absent-b", "Syg B");
		var vikar = await TestDataBuilder.CreateStaffAsync(factory.Services, _kit.TenantId, "Eneste Vikar", StaffRole.Substitute);
		var (_, slotsA) = await TeachesOnMondayAsync(absentA, "6.a", _first);
		var (_, slotsB) = await TeachesOnMondayAsync(absentB, "6.b", _first);
		var idA = await ReportAsync(_admin, absentA.Id, _monday);
		var idB = await ReportAsync(_admin, absentB.Id, _monday);

		await Assert.That((await AssignAsync(idA, slotsA[0].Id, vikar.Id)).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That((await DetailAsync(idB)).Lessons.Single().Candidates.Any(c => c.Id == vikar.Id)).IsFalse();

		var second = await AssignAsync(idB, slotsB[0].Id, vikar.Id);
		await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
	}

	[Test]
	public async Task AssignVikar_ConcurrentAdmins_OnlyOneWins()
	{
		var (_, absentA) = await _kit.TeacherAsync("race-absent-a", "Syg A");
		var (_, absentB) = await _kit.TeacherAsync("race-absent-b", "Syg B");
		var vikar = await TestDataBuilder.CreateStaffAsync(factory.Services, _kit.TenantId, "Eftertragtet Vikar", StaffRole.Substitute);
		var (_, slotsA) = await TeachesOnMondayAsync(absentA, "7.a", _first);
		var (_, slotsB) = await TeachesOnMondayAsync(absentB, "7.b", _first);
		var idA = await ReportAsync(_admin, absentA.Id, _monday);
		var idB = await ReportAsync(_admin, absentB.Id, _monday);
		using var otherAdmin = _kit.Client("admin", "absence-admin");

		var results = await Task.WhenAll(
			AssignAsync(idA, slotsA[0].Id, vikar.Id),
			otherAdmin.PutAsJsonAsync($"/api/v1/staff-absences/{idB}/substitute",
				new AssignAbsenceSubstituteRequest(slotsB[0].Id, _monday, vikar.Id), JsonOpts));

		var codes = results.Select(r => r.StatusCode).ToList();
		await Assert.That(codes.Count(c => c == HttpStatusCode.NoContent)).IsEqualTo(1);
		await Assert.That(codes.Count(c => c == HttpStatusCode.Conflict)).IsEqualTo(1);
	}

	[Test]
	public async Task Admin_ReportsOnBehalf_AndDeleteClearsCover()
	{
		var (absentClient, absent) = await _kit.TeacherAsync("phoned-in-sick");
		var vikar = await TestDataBuilder.CreateStaffAsync(factory.Services, _kit.TenantId, "Vikar Jensen", StaffRole.Substitute);
		var (classId, slots) = await TeachesOnMondayAsync(absent, "2.a", _first);

		var id = await ReportAsync(_admin, absent.Id, _monday);
		var mine = await absentClient.GetFromJsonAsync<List<StaffAbsenceDto>>("/api/v1/staff-absences/mine", JsonOpts);
		await Assert.That(mine!.Single().ReportedByName).IsEqualTo("Hanne Kontor");

		await AssignAsync(id, slots[0].Id, vikar.Id);
		var delete = await _admin.DeleteAsync($"/api/v1/staff-absences/{id}");
		await Assert.That(delete.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var isoDate = _monday.ToDateTime(TimeOnly.MinValue);
		var plan = await _admin.GetFromJsonAsync<WeekPlanController.WeekPlanDto>(
			$"/api/v1/classes/{classId}/week-plan?isoYear={ISOWeek.GetYear(isoDate)}&isoWeek={ISOWeek.GetWeekOfYear(isoDate)}", JsonOpts);
		await Assert.That(plan!.Slots.Single(s => s.SchemaSlotId == slots[0].Id).SubstituteTeacherId).IsNull();
	}

	[Test]
	public async Task Authorization_StaffOnlyOwnAbsence_AdminOnlyCover()
	{
		var (teacherClient, teacher) = await _kit.TeacherAsync("auth-teacher");
		var (_, colleague) = await _kit.TeacherAsync("auth-colleague", "Kollega");
		var colleagueAbsence = await ReportAsync(_admin, colleague.Id, _monday);

		var forOther = await teacherClient.PostAsJsonAsync("/api/v1/staff-absences",
			new ReportStaffAbsenceRequest(colleague.Id, _monday, null, null), JsonOpts);
		await Assert.That(forOther.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await teacherClient.GetAsync("/api/v1/staff-absences")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await teacherClient.GetAsync($"/api/v1/staff-absences/{colleagueAbsence}")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await teacherClient.DeleteAsync($"/api/v1/staff-absences/{colleagueAbsence}")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

		var own = await ReportAsync(teacherClient, null, _monday);
		await Assert.That((await teacherClient.DeleteAsync($"/api/v1/staff-absences/{own}")).StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var parent = _kit.Client("parent", "staff-absence-parent");
		var parentPost = await parent.PostAsJsonAsync("/api/v1/staff-absences",
			new ReportStaffAbsenceRequest(teacher.Id, _monday, null, null), JsonOpts);
		await Assert.That(parentPost.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task OtherTenant_CannotSeeAbsence()
	{
		var (_, absent) = await _kit.TeacherAsync("tenant-absent");
		var id = await ReportAsync(_admin, absent.Id, _monday);

		var otherKit = new AbsenceTestKit(factory);
		await otherKit.InitAsync();
		var otherAdmin = await otherKit.AdminAsync("staff-absence-other-admin");

		await Assert.That((await otherAdmin.GetAsync($"/api/v1/staff-absences/{id}")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That((await otherAdmin.DeleteAsync($"/api/v1/staff-absences/{id}")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}
}
