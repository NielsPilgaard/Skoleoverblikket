using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Shared arrangement for the fravær tests: one tenant with clients per role, students and
/// parents, and a calendar shaped so the current quarter has an exact number of school days.
/// Absence rows themselves are always created through the API.
/// </summary>
public sealed class AbsenceTestKit(ApiFactory factory)
{
	public static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private static readonly TimeZoneInfo Copenhagen = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");

	public Guid TenantId { get; } = Guid.NewGuid();

	public ApiFactory Factory { get; } = factory;

	public async Task InitAsync() => await TestDataBuilder.CreateSchoolAsync(Factory.Services, TenantId);

	public HttpClient Client(string roles, string subject)
	{
		var client = Factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", TenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", roles);
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	/// <summary>Admin with a staff row, so fremmøde (which records who took it) works.</summary>
	public async Task<HttpClient> AdminAsync(string subject = "absence-admin")
	{
		await TestDataBuilder.CreateStaffAsync(Factory.Services, TenantId, "Hanne Kontor", isAdmin: true, keycloakSubject: subject);
		return Client("admin", subject);
	}

	public async Task<(HttpClient Client, Staff Staff)> TeacherAsync(string subject, string name = "Thomas Lærer")
	{
		var staff = await TestDataBuilder.CreateStaffAsync(Factory.Services, TenantId, name, keycloakSubject: subject);
		return (Client("user", subject), staff);
	}

	/// <summary>Creates a klasse through the API so its klassetrin can be set.</summary>
	public async Task<Guid> CreateClassAsync(HttpClient admin, string name, int? gradeLevel = null)
	{
		var response = await admin.PostAsJsonAsync("/api/v1/classes",
			new ClassesController.UpsertClassRequest(name, null, gradeLevel), JsonOpts);
		response.EnsureSuccessStatusCode();
		var dto = await response.Content.ReadFromJsonAsync<ClassesController.ClassDto>(JsonOpts);
		return dto!.Id;
	}

	public async Task<Student> CreateStudentAsync(Guid classId, string name = "Mikkel Elev")
	{
		using var scope = Factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var student = new Student { Id = Guid.NewGuid(), TenantId = TenantId, Name = name, ClassId = classId };
		db.Students.Add(student);
		await db.SaveChangesAsync();
		return student;
	}

	public async Task<HttpClient> ParentOfAsync(Guid studentId, string subject)
	{
		using var scope = Factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var parent = new Parent
		{
			Id = Guid.NewGuid(),
			TenantId = TenantId,
			Name = "Birgitte Forælder",
			Email = $"{subject}@test.dk",
			KeycloakSubject = subject,
		};
		parent.Students.Add(await db.Students.IgnoreQueryFilters().FirstAsync(s => s.Id == studentId));
		db.Parents.Add(parent);
		await db.SaveChangesAsync();
		return Client("parent", subject);
	}

	public async Task RestrictClassToAsync(Guid classId, Guid staffId)
	{
		using var scope = Factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		db.ClassPermissions.Add(new ClassPermission { Id = Guid.NewGuid(), TenantId = TenantId, ClassId = classId, StaffId = staffId });
		await db.SaveChangesAsync();
	}

	// ── Dates ────────────────────────────────────────────────────────────────────

	public static DateOnly DanishToday() =>
		DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Copenhagen).DateTime);

	public static DateOnly QuarterStart(DateOnly date) => new(date.Year, (date.Month - 1) / 3 * 3 + 1, 1);

	public static DateOnly QuarterEnd(DateOnly date) => QuarterStart(date).AddMonths(3).AddDays(-1);

	public static (int Year, int Quarter) QuarterOf(DateOnly date) => (date.Year, (date.Month - 1) / 3 + 1);

	private static bool IsWeekday(DateOnly d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

	/// <summary>
	/// The latest weekday up to today in the current quarter — a day fremmøde can be noted for — or
	/// null in the rare case the quarter so far is only a weekend.
	/// </summary>
	public static DateOnly? RecentSchoolDay()
	{
		var today = DanishToday();
		for (var d = today; d >= QuarterStart(today); d = d.AddDays(-1))
		{
			if (IsWeekday(d))
			{
				return d;
			}
		}

		return null;
	}

	public static IEnumerable<DateOnly> WeekdaysInQuarter(DateOnly date)
	{
		for (var d = QuarterStart(date); d <= QuarterEnd(date); d = d.AddDays(1))
		{
			if (IsWeekday(d))
			{
				yield return d;
			}
		}
	}

	/// <summary>
	/// Closes every weekday of <paramref name="anchor"/>'s quarter with Ferie except
	/// <paramref name="schoolDays"/> weekdays starting at the anchor (then the ones before it),
	/// so the quarter has exactly that many school days.
	/// </summary>
	public async Task ShapeQuarterAsync(HttpClient admin, DateOnly anchor, int schoolDays)
	{
		var weekdays = WeekdaysInQuarter(anchor).ToList();
		var open = weekdays.Where(d => d >= anchor).Take(schoolDays).ToList();
		open.AddRange(weekdays.Where(d => d < anchor).Reverse().Take(schoolDays - open.Count));
		var openSet = open.ToHashSet();

		DateOnly? runStart = null;
		DateOnly runEnd = default;
		foreach (var d in weekdays.Append(DateOnly.MaxValue))
		{
			if (d != DateOnly.MaxValue && !openSet.Contains(d))
			{
				runStart ??= d;
				runEnd = d;
				continue;
			}

			if (runStart is { } start)
			{
				var response = await admin.PostAsJsonAsync("/api/v1/calendar",
					new CalendarController.CreateCalendarEntryRequest("Ferie", CalendarEntryType.Ferie, start, runEnd), JsonOpts);
				response.EnsureSuccessStatusCode();
				runStart = null;
			}
		}
	}

	// ── API shortcuts ────────────────────────────────────────────────────────────

	public static async Task<HttpResponseMessage> SaveAttendanceAsync(
		HttpClient client, Guid classId, DateOnly date, AttendanceCheckpoint checkpoint,
		params (Guid StudentId, AbsenceCategory Category)[] absent) =>
		await client.PutAsJsonAsync(
			$"/api/v1/attendance/classes/{classId}?date={date:yyyy-MM-dd}",
			new SaveAttendanceRequest(checkpoint, absent.Select(a => new AbsentStudentRequest(a.StudentId, a.Category)).ToList()),
			JsonOpts);

	public static async Task<HttpResponseMessage> ReportAsync(
		HttpClient parent, Guid studentId, AbsenceCategory category, DateOnly date, DateOnly? endDate = null, string? reason = null) =>
		await parent.PostAsJsonAsync("/api/v1/absence",
			new ReportAbsenceRequest(studentId, date, endDate, category, reason), JsonOpts);

	public static async Task<List<AbsenceRecordDto>> ParentRecordsAsync(HttpClient parent) =>
		(await parent.GetFromJsonAsync<List<AbsenceRecordDto>>("/api/v1/absence/mine", JsonOpts))!;

	public static async Task<QuarterStatsDto> StatsAsync(HttpClient client, DateOnly date, Guid? classId = null)
	{
		var (year, quarter) = QuarterOf(date);
		var url = $"/api/v1/absence/stats?year={year}&quarter={quarter}" + (classId is null ? "" : $"&classId={classId}");
		return (await client.GetFromJsonAsync<QuarterStatsDto>(url, JsonOpts))!;
	}
}
