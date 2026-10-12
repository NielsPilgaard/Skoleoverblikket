using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Arranges test data directly via DbContext (not via the API) to keep
/// test setup fast and independent of the endpoints under test.
/// Only used for prerequisite entities (tenant root, time slots, etc.) —
/// test assertions always go through the HTTP API.
/// </summary>
public static class TestDataBuilder
{
	public static async Task<School> CreateSchoolAsync(IServiceProvider services, Guid tenantId, string name = "Teststole")
	{
		using var scope = services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var school = new School
		{
			Id = tenantId,
			Name = name,
			ContactEmail = "test@skole.dk",
		};
		db.Schools.Add(school);
		await db.SaveChangesAsync();
		return school;
	}

	public static async Task<Staff> CreateStaffAsync(
		IServiceProvider services,
		Guid tenantId,
		string name = "Anders Lærer",
		StaffRole role = StaffRole.Teacher,
		bool isAdmin = false,
		string? keycloakSubject = null)
	{
		using var scope = services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var staff = new Staff
		{
			Id = Guid.NewGuid(),
			TenantId = tenantId,
			Name = name,
			Role = role,
			IsAdmin = isAdmin,
			KeycloakSubject = keycloakSubject,
		};
		db.Staff.Add(staff);
		await db.SaveChangesAsync();
		return staff;
	}

	public static async Task<Course> CreateCourseAsync(IServiceProvider services, Guid tenantId, string name = "Dansk")
	{
		using var scope = services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var course = new Course
		{
			Id = Guid.NewGuid(),
			TenantId = tenantId,
			Name = name,
		};
		db.Courses.Add(course);
		await db.SaveChangesAsync();
		return course;
	}

	public static async Task<TimeSlot> CreateTimeSlotAsync(
		IServiceProvider services, Guid tenantId,
		TimeOnly start, TimeOnly end, int sortOrder = 1)
	{
		using var scope = services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var slot = new TimeSlot
		{
			Id = Guid.NewGuid(),
			TenantId = tenantId,
			StartTime = start,
			EndTime = end,
			SortOrder = sortOrder,
		};
		db.TimeSlots.Add(slot);
		await db.SaveChangesAsync();
		return slot;
	}

	public static async Task<CalendarEntry> CreateCalendarEntryAsync(
		IServiceProvider services, Guid tenantId,
		CalendarEntryType type, string title, DateOnly startDate, DateOnly endDate)
	{
		using var scope = services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var entry = new CalendarEntry
		{
			Id = Guid.NewGuid(),
			TenantId = tenantId,
			Type = type,
			Title = title,
			StartDate = startDate,
			EndDate = endDate,
		};
		db.CalendarEntries.Add(entry);
		await db.SaveChangesAsync();
		return entry;
	}

	public static async Task<SchoolFile> CreateSchoolFileAsync(
		IServiceProvider services, Guid tenantId, string fileName = "test.pdf")
	{
		using var scope = services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var file = new SchoolFile
		{
			Id = Guid.NewGuid(),
			TenantId = tenantId,
			FileName = fileName,
			ContentType = "application/pdf",
			SizeBytes = 1024,
			StorageKey = $"test/{Guid.NewGuid()}/{fileName}",
			Url = $"https://storage.example.com/{fileName}",
			UploadedBy = "test@skole.dk",
		};
		db.SchoolFiles.Add(file);
		await db.SaveChangesAsync();
		return file;
	}

	public static async Task<(Class klass, Schema schema)> CreateClassWithSchemaAsync(
		IServiceProvider services, Guid tenantId,
		string className = "2.b", string schemaName = "Skema 2024")
	{
		using var scope = services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		var klass = new Class
		{
			Id = Guid.NewGuid(),
			TenantId = tenantId,
			Name = className,
		};
		db.Classes.Add(klass);
		await db.SaveChangesAsync();

		var schema = new Schema
		{
			Id = Guid.NewGuid(),
			TenantId = tenantId,
			ClassId = klass.Id,
			Name = schemaName,
			StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1),
			EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(11),
		};
		db.Schemas.Add(schema);
		await db.SaveChangesAsync();

		return (klass, schema);
	}

	public static async Task<SchemaSlot> CreateSchemaSlotAsync(
		IServiceProvider services, Guid tenantId,
		Guid schemaId, Guid timeSlotId, Guid courseId, Guid teacherId,
		DayOfWeek weekday = DayOfWeek.Monday)
	{
		using var scope = services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var slot = new SchemaSlot
		{
			Id = Guid.NewGuid(),
			TenantId = tenantId,
			SchemaId = schemaId,
			TimeSlotId = timeSlotId,
			CourseId = courseId,
			TeacherId = teacherId,
			Weekday = weekday,
		};
		db.SchemaSlots.Add(slot);
		await db.SaveChangesAsync();
		return slot;
	}

	/// <summary>
	/// Gives the school a paid (non-trial) subscription with exactly <paramref name="modules"/>.
	/// A completed checkout without a Stripe subscription id activates it without calling Stripe.
	/// </summary>
	public static async Task CreateActiveSubscriptionAsync(
		IServiceProvider services, Guid tenantId, params SubscriptionModule[] modules)
	{
		await using var scope = services.CreateAsyncScope();
		var subscriptions = scope.ServiceProvider.GetRequiredService<SubscriptionService>();
		await subscriptions.GetOrCreateAsync(tenantId);
		await subscriptions.HandleWebhookAsync(new Stripe.Event
		{
			Type = Stripe.EventTypes.CheckoutSessionCompleted,
			Data = new Stripe.EventData
			{
				Object = new Stripe.Checkout.Session { Metadata = new Dictionary<string, string> { ["school_id"] = tenantId.ToString() } },
			},
		});

		foreach (var module in modules)
		{
			await subscriptions.GrantModuleOverrideAsync(tenantId, module);
		}
	}

	/// <summary>Starts a fresh 30-day trial, which has every module.</summary>
	public static async Task CreateTrialSubscriptionAsync(IServiceProvider services, Guid tenantId)
	{
		await using var scope = services.CreateAsyncScope();
		await scope.ServiceProvider.GetRequiredService<SubscriptionService>().GetOrCreateAsync(tenantId);
	}

	public static async Task RemoveModuleAsync(IServiceProvider services, Guid tenantId, SubscriptionModule module)
	{
		await using var scope = services.CreateAsyncScope();
		await scope.ServiceProvider.GetRequiredService<SubscriptionService>().RemoveModuleAsync(tenantId, module);
	}

	public static async Task<RoomsController.RoomDto> CreateRoomAsync(
		IServiceProvider services, Guid tenantId, string name = "Lokale 1")
	{
		await using var scope = services.CreateAsyncScope();
		scope.ServiceProvider.GetRequiredService<HttpTenantContext>().UseBackgroundTenant(tenantId);
		return await scope.ServiceProvider.GetRequiredService<RoomService>()
			.CreateRoomAsync(new RoomsController.UpsertRoomRequest(name, null, null), CancellationToken.None);
	}
}
