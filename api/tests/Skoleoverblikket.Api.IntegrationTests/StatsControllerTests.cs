using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Integration tests for StatsController's dashboard endpoints.
/// Covers:
///   - GET /stats/dashboard: pending absence count, open vacation window selection,
///     and the parent-module gating of the unread beskeder/kontaktbog counts.
///   - GET /stats/my-dashboard: staff-only access at the API boundary, today's
///     schedule scoped to the calling staff member, and unread counts.
/// Each test uses its own tenant so counts stay isolated from sibling tests.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class StatsControllerTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private readonly ApiFactory _factory = factory;

	// ── Private helpers ──────────────────────────────────────────────────────────

	private HttpClient CreateClient(Guid tenantId, string roles, string subject)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", roles);
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	/// <summary>Creates a fresh tenant so per-test counts don't collide.</summary>
	private async Task<Guid> CreateTenantAsync()
	{
		var tenantId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, tenantId);
		return tenantId;
	}

	/// <summary>
	/// Grants the parent module via an admin override, mirroring how BillingTests
	/// activates a module without going through Stripe.
	/// </summary>
	private async Task ActivateParentModuleAsync(Guid tenantId)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		var subscriptionId = Guid.NewGuid();
		db.Subscriptions.Add(new Subscription
		{
			Id = subscriptionId,
			SchoolId = tenantId,
			Status = SubscriptionStatus.Active,
			Interval = BillingInterval.Monthly,
			TrialEnd = DateTimeOffset.UtcNow.AddDays(-1),
		});
		db.SubscriptionModuleItems.Add(new SubscriptionModuleItem
		{
			Id = Guid.NewGuid(),
			SubscriptionId = subscriptionId,
			Module = SubscriptionModule.ParentModule,
			IsAdminOverride = true,
		});
		await db.SaveChangesAsync();
	}

	/// <summary>Creates an Active subscription with no modules — parent module inactive.</summary>
	private async Task CreateSubscriptionWithoutModulesAsync(Guid tenantId)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		db.Subscriptions.Add(new Subscription
		{
			Id = Guid.NewGuid(),
			SchoolId = tenantId,
			Status = SubscriptionStatus.Active,
			Interval = BillingInterval.Monthly,
			TrialEnd = DateTimeOffset.UtcNow.AddDays(-1),
		});
		await db.SaveChangesAsync();
	}

	private async Task<Student> CreateStudentAsync(Guid tenantId, Guid classId, string name = "Elev Testsen")
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var student = new Student
		{
			Id = Guid.NewGuid(),
			TenantId = tenantId,
			Name = name,
			ClassId = classId,
		};
		db.Students.Add(student);
		await db.SaveChangesAsync();
		return student;
	}

	private async Task<Parent> CreateParentAsync(Guid tenantId, string keycloakSubject)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var parent = new Parent
		{
			Id = Guid.NewGuid(),
			TenantId = tenantId,
			Name = "Forælder Testsen",
			Email = $"{keycloakSubject}@test.dk",
			KeycloakSubject = keycloakSubject,
		};
		db.Parents.Add(parent);
		await db.SaveChangesAsync();
		return parent;
	}

	private async Task CreateAbsenceReportAsync(
		Guid tenantId, Guid studentId, Guid parentId, AbsenceStatus status)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		db.AbsenceReports.Add(new AbsenceReport
		{
			Id = Guid.NewGuid(),
			TenantId = tenantId,
			StudentId = studentId,
			ReportedByParentId = parentId,
			Date = DateOnly.FromDateTime(DateTime.UtcNow),
			Status = status,
		});
		await db.SaveChangesAsync();
	}

	private async Task<VacationRegistrationWindow> CreateWindowAsync(
		Guid tenantId, bool isOpen, int deadlineInDays, string title = "Sommerferie tilmelding")
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var window = new VacationRegistrationWindow
		{
			Id = Guid.NewGuid(),
			TenantId = tenantId,
			Title = title,
			RegistrationDeadline = today.AddDays(deadlineInDays),
			CareStartDate = today.AddDays(60),
			CareEndDate = today.AddDays(90),
			Granularity = VacationRegistrationGranularity.Weeks,
			IsOpen = isOpen,
		};
		db.VacationRegistrationWindows.Add(window);
		await db.SaveChangesAsync();
		return window;
	}

	private async Task CreateMessageAsync(Guid tenantId, Guid recipientId, bool read)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		db.Messages.Add(new Message
		{
			Id = Guid.NewGuid(),
			TenantId = tenantId,
			SenderId = Guid.NewGuid(),
			SenderType = Skoleoverblikket.Api.Services.RecipientType.Parent,
			RecipientId = recipientId,
			RecipientType = Skoleoverblikket.Api.Services.RecipientType.Staff,
			Subject = "Test",
			Body = "Test besked",
			SentAt = DateTimeOffset.UtcNow,
			ReadAt = read ? DateTimeOffset.UtcNow : null,
		});
		await db.SaveChangesAsync();
	}

	private async Task CreateContactMessageAsync(Guid tenantId, Guid studentId, SenderType senderType, bool read)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		var thread = await db.ContactThreads
			.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.StudentId == studentId);
		if (thread is null)
		{
			thread = new ContactThread
			{
				Id = Guid.NewGuid(),
				TenantId = tenantId,
				StudentId = studentId,
			};
			db.ContactThreads.Add(thread);
			await db.SaveChangesAsync();
		}

		db.ContactMessages.Add(new ContactMessage
		{
			Id = Guid.NewGuid(),
			TenantId = tenantId,
			ThreadId = thread.Id,
			SenderType = senderType,
			SenderId = Guid.NewGuid(),
			Body = "Test kontaktbesked",
			SentAt = DateTimeOffset.UtcNow,
			ReadAt = read ? DateTimeOffset.UtcNow : null,
		});
		await db.SaveChangesAsync();
	}

	// ── GET /stats/dashboard — attention alerts ──────────────────────────────────

	[Test]
	public async Task GetDashboard_CountsOnlyReportedAbsences()
	{
		var tenantId = await CreateTenantAsync();
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, tenantId);
		var student = await CreateStudentAsync(tenantId, klass.Id);
		var parent = await CreateParentAsync(tenantId, "stats-absence-parent");

		await CreateAbsenceReportAsync(tenantId, student.Id, parent.Id, AbsenceStatus.Reported);
		await CreateAbsenceReportAsync(tenantId, student.Id, parent.Id, AbsenceStatus.Reported);
		await CreateAbsenceReportAsync(tenantId, student.Id, parent.Id, AbsenceStatus.Confirmed);
		await CreateAbsenceReportAsync(tenantId, student.Id, parent.Id, AbsenceStatus.Dismissed);

		using var client = CreateClient(tenantId, "admin", "stats-admin");
		var stats = await client.GetFromJsonAsync<StatsController.DashboardStats>(
			"/api/v1/stats/dashboard", JsonOpts);

		await Assert.That(stats).IsNotNull();
		await Assert.That(stats!.PendingAbsenceCount).IsEqualTo(2);
	}

	[Test]
	public async Task GetDashboard_NoPendingAbsences_ReturnsZero()
	{
		var tenantId = await CreateTenantAsync();

		using var client = CreateClient(tenantId, "admin", "stats-admin-empty");
		var stats = await client.GetFromJsonAsync<StatsController.DashboardStats>(
			"/api/v1/stats/dashboard", JsonOpts);

		await Assert.That(stats!.PendingAbsenceCount).IsEqualTo(0);
		await Assert.That(stats.OpenVacationWindow).IsNull();
	}

	[Test]
	public async Task GetDashboard_PicksEarliestDeadlineOpenWindow()
	{
		var tenantId = await CreateTenantAsync();
		await CreateWindowAsync(tenantId, isOpen: true, deadlineInDays: 30, title: "Senere frist");
		var earliest = await CreateWindowAsync(tenantId, isOpen: true, deadlineInDays: 7, title: "Tidligste frist");

		using var client = CreateClient(tenantId, "admin", "stats-admin-window");
		var stats = await client.GetFromJsonAsync<StatsController.DashboardStats>(
			"/api/v1/stats/dashboard", JsonOpts);

		await Assert.That(stats!.OpenVacationWindow).IsNotNull();
		await Assert.That(stats.OpenVacationWindow!.WindowId).IsEqualTo(earliest.Id);
		await Assert.That(stats.OpenVacationWindow.Title).IsEqualTo("Tidligste frist");
	}

	[Test]
	public async Task GetDashboard_IgnoresClosedAndExpiredWindows()
	{
		var tenantId = await CreateTenantAsync();
		await CreateWindowAsync(tenantId, isOpen: false, deadlineInDays: 7);
		await CreateWindowAsync(tenantId, isOpen: true, deadlineInDays: -1);

		using var client = CreateClient(tenantId, "admin", "stats-admin-closed");
		var stats = await client.GetFromJsonAsync<StatsController.DashboardStats>(
			"/api/v1/stats/dashboard", JsonOpts);

		await Assert.That(stats!.OpenVacationWindow).IsNull();
	}

	// ── GET /stats/dashboard — module gating ─────────────────────────────────────

	[Test]
	public async Task GetDashboard_ParentModuleInactive_UnreadCountsAreNull()
	{
		var tenantId = await CreateTenantAsync();
		await CreateSubscriptionWithoutModulesAsync(tenantId);

		using var client = CreateClient(tenantId, "admin", "stats-admin-nomodule");
		var stats = await client.GetFromJsonAsync<StatsController.DashboardStats>(
			"/api/v1/stats/dashboard", JsonOpts);

		await Assert.That(stats!.UnreadMessageCount).IsNull();
		await Assert.That(stats.UnreadKontaktbogCount).IsNull();
	}

	[Test]
	public async Task GetDashboard_ParentModuleActive_ReturnsUnreadCounts()
	{
		var tenantId = await CreateTenantAsync();
		await ActivateParentModuleAsync(tenantId);

		var admin = await TestDataBuilder.CreateStaffAsync(
			_factory.Services, tenantId, "Admin Hansen", StaffRole.Teacher,
			isAdmin: true, keycloakSubject: "stats-admin-module");

		await CreateMessageAsync(tenantId, admin.Id, read: false);
		await CreateMessageAsync(tenantId, admin.Id, read: false);
		await CreateMessageAsync(tenantId, admin.Id, read: true);

		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, tenantId);
		var student = await CreateStudentAsync(tenantId, klass.Id);
		await CreateContactMessageAsync(tenantId, student.Id, SenderType.Parent, read: false);
		await CreateContactMessageAsync(tenantId, student.Id, SenderType.Parent, read: true);
		// Staff-sent messages are never "unread" for staff.
		await CreateContactMessageAsync(tenantId, student.Id, SenderType.Staff, read: false);

		using var client = CreateClient(tenantId, "admin", "stats-admin-module");
		var stats = await client.GetFromJsonAsync<StatsController.DashboardStats>(
			"/api/v1/stats/dashboard", JsonOpts);

		await Assert.That(stats!.UnreadMessageCount).IsEqualTo(2);
		await Assert.That(stats.UnreadKontaktbogCount).IsEqualTo(1);
	}

	// ── GET /stats/my-dashboard — access control ─────────────────────────────────

	[Test]
	public async Task GetMyDashboard_Admin_IsRejected()
	{
		var tenantId = await CreateTenantAsync();
		await TestDataBuilder.CreateStaffAsync(
			_factory.Services, tenantId, "Admin Hansen", StaffRole.Teacher,
			isAdmin: true, keycloakSubject: "my-dash-admin");

		using var client = CreateClient(tenantId, "admin", "my-dash-admin");
		var response = await client.GetAsync("/api/v1/stats/my-dashboard");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task GetMyDashboard_Parent_IsRejected()
	{
		var tenantId = await CreateTenantAsync();

		using var client = CreateClient(tenantId, "parent", "my-dash-parent");
		var response = await client.GetAsync("/api/v1/stats/my-dashboard");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task GetMyDashboard_Board_IsRejected()
	{
		var tenantId = await CreateTenantAsync();

		using var client = CreateClient(tenantId, "board", "my-dash-board");
		var response = await client.GetAsync("/api/v1/stats/my-dashboard");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task GetMyDashboard_SuperAdmin_IsRejected()
	{
		var tenantId = await CreateTenantAsync();

		using var client = CreateClient(tenantId, "superadmin", "my-dash-superadmin");
		var response = await client.GetAsync("/api/v1/stats/my-dashboard");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task GetMyDashboard_StaffFlaggedAdminInDb_IsRejected()
	{
		// IsAdmin can be set on the Staff row without the Keycloak admin claim being
		// present on the token — the claim reject alone would let this caller through.
		var tenantId = await CreateTenantAsync();
		await TestDataBuilder.CreateStaffAsync(
			_factory.Services, tenantId, "Db Admin", StaffRole.Teacher,
			isAdmin: true, keycloakSubject: "my-dash-db-admin");

		using var client = CreateClient(tenantId, "user", "my-dash-db-admin");
		var response = await client.GetAsync("/api/v1/stats/my-dashboard");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task GetMyDashboard_UnknownStaffSubject_IsRejected()
	{
		var tenantId = await CreateTenantAsync();

		using var client = CreateClient(tenantId, "user", "my-dash-nobody");
		var response = await client.GetAsync("/api/v1/stats/my-dashboard");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	// ── GET /stats/my-dashboard — today's schedule ───────────────────────────────

	[Test]
	public async Task GetMyDashboard_ReturnsOnlyCallersLektionerForToday()
	{
		var tenantId = await CreateTenantAsync();
		var teacher = await TestDataBuilder.CreateStaffAsync(
			_factory.Services, tenantId, "Anders Lærer", StaffRole.Teacher,
			keycloakSubject: "my-dash-teacher");
		var other = await TestDataBuilder.CreateStaffAsync(
			_factory.Services, tenantId, "Bente Lærer", StaffRole.Teacher,
			keycloakSubject: "my-dash-other");

		var (_, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, tenantId);
		var course = await TestDataBuilder.CreateCourseAsync(_factory.Services, tenantId);
		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(
			_factory.Services, tenantId, new TimeOnly(8, 0), new TimeOnly(8, 45));

		var today = DateOnly.FromDateTime(DateTime.UtcNow).DayOfWeek;
		var notToday = today == DayOfWeek.Monday ? DayOfWeek.Tuesday : DayOfWeek.Monday;

		// Caller's lesson today — should appear.
		await TestDataBuilder.CreateSchemaSlotAsync(
			_factory.Services, tenantId, schema.Id, timeSlot.Id, course.Id, teacher.Id, today);
		// Caller's lesson on another weekday — should not appear.
		await TestDataBuilder.CreateSchemaSlotAsync(
			_factory.Services, tenantId, schema.Id, timeSlot.Id, course.Id, teacher.Id, notToday);
		// Another teacher's lesson today — should not appear.
		await TestDataBuilder.CreateSchemaSlotAsync(
			_factory.Services, tenantId, schema.Id, timeSlot.Id, course.Id, other.Id, today);

		using var client = CreateClient(tenantId, "user", "my-dash-teacher");
		var stats = await client.GetFromJsonAsync<StatsController.MyDashboardStats>(
			"/api/v1/stats/my-dashboard", JsonOpts);

		await Assert.That(stats).IsNotNull();
		await Assert.That(stats!.TodaySchedule.Count).IsEqualTo(1);
		await Assert.That(stats.TodaySchedule[0].CourseName).IsEqualTo("Dansk");
		await Assert.That(stats.TodaySchedule[0].StartTime).IsEqualTo(new TimeOnly(8, 0));
	}

	[Test]
	public async Task GetMyDashboard_IncludesLektionerWhereCallerIsAide()
	{
		var tenantId = await CreateTenantAsync();
		var teacher = await TestDataBuilder.CreateStaffAsync(
			_factory.Services, tenantId, "Anders Lærer", StaffRole.Teacher,
			keycloakSubject: "my-dash-aide-teacher");
		var aide = await TestDataBuilder.CreateStaffAsync(
			_factory.Services, tenantId, "Pia Pædagog", StaffRole.Aide,
			keycloakSubject: "my-dash-aide");

		var (_, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, tenantId);
		var course = await TestDataBuilder.CreateCourseAsync(_factory.Services, tenantId);
		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(
			_factory.Services, tenantId, new TimeOnly(9, 0), new TimeOnly(9, 45));

		var today = DateOnly.FromDateTime(DateTime.UtcNow).DayOfWeek;
		var slot = await TestDataBuilder.CreateSchemaSlotAsync(
			_factory.Services, tenantId, schema.Id, timeSlot.Id, course.Id, teacher.Id, today);

		using (var scope = _factory.Services.CreateScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			var tracked = await db.SchemaSlots.FindAsync(slot.Id);
			tracked!.AideId = aide.Id;
			await db.SaveChangesAsync();
		}

		using var client = CreateClient(tenantId, "user", "my-dash-aide");
		var stats = await client.GetFromJsonAsync<StatsController.MyDashboardStats>(
			"/api/v1/stats/my-dashboard", JsonOpts);

		await Assert.That(stats!.TodaySchedule.Count).IsEqualTo(1);
		await Assert.That(stats.TodaySchedule[0].SlotId).IsEqualTo(slot.Id);
	}

	[Test]
	public async Task GetMyDashboard_NoLektioner_ReturnsEmptySchedule()
	{
		var tenantId = await CreateTenantAsync();
		await TestDataBuilder.CreateStaffAsync(
			_factory.Services, tenantId, "Ledig Lærer", StaffRole.Teacher,
			keycloakSubject: "my-dash-free");

		using var client = CreateClient(tenantId, "user", "my-dash-free");
		var stats = await client.GetFromJsonAsync<StatsController.MyDashboardStats>(
			"/api/v1/stats/my-dashboard", JsonOpts);

		await Assert.That(stats!.TodaySchedule).IsEmpty();
	}

	// ── GET /stats/my-dashboard — unread counts ──────────────────────────────────

	[Test]
	public async Task GetMyDashboard_ParentModuleInactive_UnreadCountsAreNull()
	{
		var tenantId = await CreateTenantAsync();
		await CreateSubscriptionWithoutModulesAsync(tenantId);
		await TestDataBuilder.CreateStaffAsync(
			_factory.Services, tenantId, "Anders Lærer", StaffRole.Teacher,
			keycloakSubject: "my-dash-nomodule");

		using var client = CreateClient(tenantId, "user", "my-dash-nomodule");
		var stats = await client.GetFromJsonAsync<StatsController.MyDashboardStats>(
			"/api/v1/stats/my-dashboard", JsonOpts);

		await Assert.That(stats!.UnreadMessageCount).IsNull();
		await Assert.That(stats.UnreadKontaktbogCount).IsNull();
	}

	[Test]
	public async Task GetMyDashboard_UnreadMessagesScopedToCaller()
	{
		var tenantId = await CreateTenantAsync();
		await ActivateParentModuleAsync(tenantId);

		var caller = await TestDataBuilder.CreateStaffAsync(
			_factory.Services, tenantId, "Anders Lærer", StaffRole.Teacher,
			keycloakSubject: "my-dash-unread-caller");
		var other = await TestDataBuilder.CreateStaffAsync(
			_factory.Services, tenantId, "Bente Lærer", StaffRole.Teacher,
			keycloakSubject: "my-dash-unread-other");

		await CreateMessageAsync(tenantId, caller.Id, read: false);
		await CreateMessageAsync(tenantId, caller.Id, read: true);
		// Addressed to a colleague — must not count toward the caller.
		await CreateMessageAsync(tenantId, other.Id, read: false);

		using var client = CreateClient(tenantId, "user", "my-dash-unread-caller");
		var stats = await client.GetFromJsonAsync<StatsController.MyDashboardStats>(
			"/api/v1/stats/my-dashboard", JsonOpts);

		await Assert.That(stats!.UnreadMessageCount).IsEqualTo(1);
	}
}
