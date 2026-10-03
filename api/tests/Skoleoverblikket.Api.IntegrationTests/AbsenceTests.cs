using System.Net;
using System.Net.Http.Json;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using static Skoleoverblikket.Api.IntegrationTests.Infrastructure.AbsenceTestKit;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// The fravær register: parent sick reports and leave requests, leave approval, category changes,
/// who sees which records, and tenant isolation. Fremmøde itself is in <see cref="AttendanceTests"/>.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class AbsenceTests(ApiFactory factory)
{
	private AbsenceTestKit _kit = null!;
	private HttpClient _admin = null!;
	private Guid _classId;
	private Student _student = null!;
	private HttpClient _parent = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		_kit = new AbsenceTestKit(factory);
		await _kit.InitAsync();
		_admin = await _kit.AdminAsync();
		_classId = await _kit.CreateClassAsync(_admin, "3.a");
		_student = await _kit.CreateStudentAsync(_classId);
		_parent = await _kit.ParentOfAsync(_student.Id, $"parent-{Guid.NewGuid()}");
	}

	private async Task<List<AbsenceRecordDto>> RegisterAsync(HttpClient client, string query = "") =>
		(await client.GetFromJsonAsync<List<AbsenceRecordDto>>($"/api/v1/absence{query}", JsonOpts))!;

	// ── Parent reports ───────────────────────────────────────────────────────────

	[Test]
	public async Task ParentSickReport_IsFinalAndInRegister()
	{
		var today = DanishToday();
		var response = await ReportAsync(_parent, _student.Id, AbsenceCategory.Illness, today, reason: "Feber");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);

		var record = (await RegisterAsync(_admin)).Single(r => r.StudentId == _student.Id);
		await Assert.That(record.Category).IsEqualTo(AbsenceCategory.Illness);
		await Assert.That(record.LeaveStatus).IsNull();
		await Assert.That(record.Source).IsEqualTo(AbsenceSource.Parent);

		var mine = await ParentRecordsAsync(_parent);
		await Assert.That(mine.Count).IsEqualTo(1);
		await Assert.That(mine[0].CanCancel).IsTrue();
	}

	[Test]
	public async Task ParentReport_ForSomeoneElsesChild_Returns403()
	{
		var other = await _kit.CreateStudentAsync(_classId, "Anden Elev");
		var response = await ReportAsync(_parent, other.Id, AbsenceCategory.Illness, DanishToday());
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task ParentReport_Unauthorized_Returns400()
	{
		var response = await ReportAsync(_parent, _student.Id, AbsenceCategory.Unauthorized, DanishToday());
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task ParentReport_InvalidDates_Returns400()
	{
		var today = DanishToday();
		var leaveInPast = await ReportAsync(_parent, _student.Id, AbsenceCategory.ExtraordinaryLeave, today.AddDays(-1));
		var sickTooLongAgo = await ReportAsync(_parent, _student.Id, AbsenceCategory.Illness, today.AddDays(-15));
		var endBeforeStart = await ReportAsync(_parent, _student.Id, AbsenceCategory.Illness, today, today.AddDays(-1));

		await Assert.That(leaveInPast.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(sickTooLongAgo.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(endBeforeStart.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	// ── Leave requests ───────────────────────────────────────────────────────────

	[Test]
	public async Task LeaveRequest_ApprovedByAdmin_ParentSeesApproved()
	{
		var nextWeek = DanishToday().AddDays(7);
		await ReportAsync(_parent, _student.Id, AbsenceCategory.ExtraordinaryLeave, nextWeek, nextWeek.AddDays(2), "Bryllup");

		var pending = await _admin.GetFromJsonAsync<List<AbsenceRecordDto>>("/api/v1/absence/leave-requests", JsonOpts);
		var request = pending!.Single();
		await Assert.That(request.LeaveStatus).IsEqualTo(LeaveStatus.Pending);

		var approve = await _admin.PostAsync($"/api/v1/absence/{request.Id}/approve", null);
		await Assert.That(approve.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var mine = await ParentRecordsAsync(_parent);
		await Assert.That(mine.Single().LeaveStatus).IsEqualTo(LeaveStatus.Approved);
		await Assert.That(mine.Single().Category).IsEqualTo(AbsenceCategory.ExtraordinaryLeave);

		var again = await _admin.PostAsync($"/api/v1/absence/{request.Id}/reject", null);
		await Assert.That(again.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task LeaveRequest_Rejected_ParentSeesRejectedAndCanRemoveIt()
	{
		var nextWeek = DanishToday().AddDays(7);
		await ReportAsync(_parent, _student.Id, AbsenceCategory.ExtraordinaryLeave, nextWeek);
		var id = (await ParentRecordsAsync(_parent)).Single().Id;

		var reject = await _admin.PostAsync($"/api/v1/absence/{id}/reject", null);
		await Assert.That(reject.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That((await ParentRecordsAsync(_parent)).Single().LeaveStatus).IsEqualTo(LeaveStatus.Rejected);

		var cancel = await _parent.DeleteAsync($"/api/v1/absence/{id}");
		await Assert.That(cancel.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task LeaveDecision_ByTeacher_Returns403()
	{
		await ReportAsync(_parent, _student.Id, AbsenceCategory.ExtraordinaryLeave, DanishToday().AddDays(7));
		var id = (await ParentRecordsAsync(_parent)).Single().Id;
		var (teacher, _) = await _kit.TeacherAsync("leave-teacher");

		var response = await teacher.PostAsync($"/api/v1/absence/{id}/approve", null);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task ParentCancel_PastSickDay_Returns400()
	{
		var yesterday = DanishToday().AddDays(-1);
		await ReportAsync(_parent, _student.Id, AbsenceCategory.Illness, yesterday);
		var record = (await ParentRecordsAsync(_parent)).Single();
		await Assert.That(record.CanCancel).IsFalse();

		var response = await _parent.DeleteAsync($"/api/v1/absence/{record.Id}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	// ── Staff-registered records ─────────────────────────────────────────────────

	[Test]
	public async Task StaffRegisteredAbsence_VisibleToParent_ButNotCancellable()
	{
		if (RecentSchoolDay() is not { } day)
		{
			return; // The quarter so far is only a weekend — nothing to note fremmøde for.
		}

		var save = await SaveAttendanceAsync(_admin, _classId, day, AttendanceCheckpoint.StartOfDay,
			(_student.Id, AbsenceCategory.Unauthorized));
		await Assert.That(save.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var record = (await ParentRecordsAsync(_parent)).Single();
		await Assert.That(record.Category).IsEqualTo(AbsenceCategory.Unauthorized);
		await Assert.That(record.Source).IsEqualTo(AbsenceSource.Staff);
		await Assert.That(record.CanCancel).IsFalse();

		var cancel = await _parent.DeleteAsync($"/api/v1/absence/{record.Id}");
		await Assert.That(cancel.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task ChangeCategory_StaffRecord_UnauthorizedToIllness()
	{
		if (RecentSchoolDay() is not { } day)
		{
			return;
		}

		var (teacher, _) = await _kit.TeacherAsync("category-teacher");
		await SaveAttendanceAsync(teacher, _classId, day, AttendanceCheckpoint.StartOfDay,
			(_student.Id, AbsenceCategory.Unauthorized));
		var id = (await RegisterAsync(teacher)).Single().Id;

		var toIllness = await teacher.PutAsJsonAsync($"/api/v1/absence/{id}/category",
			new AbsenceController.ChangeAbsenceCategoryRequest(AbsenceCategory.Illness), JsonOpts);
		await Assert.That(toIllness.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That((await RegisterAsync(teacher)).Single().Category).IsEqualTo(AbsenceCategory.Illness);

		var toLeave = await teacher.PutAsJsonAsync($"/api/v1/absence/{id}/category",
			new AbsenceController.ChangeAbsenceCategoryRequest(AbsenceCategory.ExtraordinaryLeave), JsonOpts);
		await Assert.That(toLeave.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task ChangeCategory_ParentRecord_Returns400()
	{
		await ReportAsync(_parent, _student.Id, AbsenceCategory.Illness, DanishToday());
		var id = (await RegisterAsync(_admin)).Single().Id;

		var response = await _admin.PutAsJsonAsync($"/api/v1/absence/{id}/category",
			new AbsenceController.ChangeAbsenceCategoryRequest(AbsenceCategory.Unauthorized), JsonOpts);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task ChangeCategory_TeacherWithoutClassPermission_Returns403()
	{
		await ReportAsync(_parent, _student.Id, AbsenceCategory.Illness, DanishToday());
		var id = (await RegisterAsync(_admin)).Single().Id;
		var (_, owner) = await _kit.TeacherAsync("class-owner", "Klassens Lærer");
		await _kit.RestrictClassToAsync(_classId, owner.Id);
		var (outsider, _) = await _kit.TeacherAsync("outsider-teacher", "Anden Lærer");

		var response = await outsider.PutAsJsonAsync($"/api/v1/absence/{id}/category",
			new AbsenceController.ChangeAbsenceCategoryRequest(AbsenceCategory.Illness), JsonOpts);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	// ── Who sees what ────────────────────────────────────────────────────────────

	[Test]
	public async Task Register_TeacherOnlySeesClassesTheyMayEdit()
	{
		var otherClassId = await _kit.CreateClassAsync(_admin, "9.b");
		var otherStudent = await _kit.CreateStudentAsync(otherClassId, "Elev i 9.b");
		var otherParent = await _kit.ParentOfAsync(otherStudent.Id, $"parent-{Guid.NewGuid()}");
		await ReportAsync(_parent, _student.Id, AbsenceCategory.Illness, DanishToday());
		await ReportAsync(otherParent, otherStudent.Id, AbsenceCategory.Illness, DanishToday());

		var (teacher, staff) = await _kit.TeacherAsync("restricted-teacher");
		var (_, otherTeacher) = await _kit.TeacherAsync("ninth-grade-teacher", "9.b Lærer");
		await _kit.RestrictClassToAsync(_classId, staff.Id);
		await _kit.RestrictClassToAsync(otherClassId, otherTeacher.Id);

		var visible = await RegisterAsync(teacher);
		await Assert.That(visible.Count).IsEqualTo(1);
		await Assert.That(visible[0].StudentId).IsEqualTo(_student.Id);
		await Assert.That((await RegisterAsync(_admin)).Count).IsEqualTo(2);
		await Assert.That((await RegisterAsync(_admin, $"?classId={otherClassId}")).Single().StudentId).IsEqualTo(otherStudent.Id);
	}

	[Test]
	public async Task ParentSeesOnlyOwnChildren()
	{
		var sibling = await _kit.CreateStudentAsync(_classId, "Klassekammerat");
		var otherParent = await _kit.ParentOfAsync(sibling.Id, $"parent-{Guid.NewGuid()}");
		await ReportAsync(otherParent, sibling.Id, AbsenceCategory.Illness, DanishToday());

		await Assert.That((await ParentRecordsAsync(_parent)).Count).IsEqualTo(0);
		var staffList = await _parent.GetFromJsonAsync<List<AbsenceRecordDto>>("/api/v1/absence", JsonOpts);
		await Assert.That(staffList!.Count).IsEqualTo(0);
	}

	[Test]
	public async Task OtherTenant_CannotSeeOrChangeRecords()
	{
		await ReportAsync(_parent, _student.Id, AbsenceCategory.ExtraordinaryLeave, DanishToday().AddDays(7));
		var id = (await RegisterAsync(_admin)).Single().Id;

		var otherKit = new AbsenceTestKit(factory);
		await otherKit.InitAsync();
		var otherAdmin = await otherKit.AdminAsync("other-tenant-admin");

		await Assert.That((await RegisterAsync(otherAdmin)).Count).IsEqualTo(0);
		var approve = await otherAdmin.PostAsync($"/api/v1/absence/{id}/approve", null);
		await Assert.That(approve.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		var category = await otherAdmin.PutAsJsonAsync($"/api/v1/absence/{id}/category",
			new AbsenceController.ChangeAbsenceCategoryRequest(AbsenceCategory.Illness), JsonOpts);
		await Assert.That(category.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}
}
