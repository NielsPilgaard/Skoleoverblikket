using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.Data;

public static class DbSeeder
{
	// Well-known IDs — stable across environments so seeding is idempotent.
	public static readonly Guid SeedSchoolId = new("11111111-1111-1111-1111-111111111111");
	public static readonly Guid SeedStaffId = new("22222222-2222-2222-2222-222222222222");
	public static readonly Guid SeedSubscriptionId = new("33333333-3333-3333-3333-333333333333");

	/// <summary>Keycloak subject for the seed admin user (matches the fixed id in Skoleoverblikket-realm.json).</summary>
	private const string SeedAdminKeycloakSubject = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

	public static async Task SeedAsync(this IServiceProvider services)
	{
		await using var scope = services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		await SeedSchoolAsync(db);
		await SeedSubscriptionAsync(db);
		await SeedStaffAsync(db);
		await SeedCoursesAsync(db);
	}

	private static async Task SeedSchoolAsync(AppDbContext db)
	{
		var exists = await db.Schools.IgnoreQueryFilters().AnyAsync(s => s.Id == SeedSchoolId);
		if (exists)
		{
			return;
		}

		db.Schools.Add(new School
		{
			Id = SeedSchoolId,
			Name = "Debugskolen",
			ContactEmail = "admin@debugskolen.dk",
			CreatedAt = DateTimeOffset.UtcNow,
		});

		await db.SaveChangesAsync();
	}

	private static async Task SeedSubscriptionAsync(AppDbContext db)
	{
		var exists = await db.Subscriptions.AnyAsync(s => s.SchoolId == SeedSchoolId);
		if (exists)
		{
			return;
		}

		db.Subscriptions.Add(new Subscription
		{
			Id = SeedSubscriptionId,
			SchoolId = SeedSchoolId,
			Status = SubscriptionStatus.Trialing,
			TrialEnd = DateTimeOffset.UtcNow.AddDays(30),
			CreatedAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow,
		});

		await db.SaveChangesAsync();
	}

	private static async Task SeedStaffAsync(AppDbContext db)
	{
		var exists = await db.Staff.IgnoreQueryFilters().AnyAsync(s => s.Id == SeedStaffId);
		if (exists)
		{
			return;
		}

		db.Staff.Add(new Staff
		{
			Id = SeedStaffId,
			TenantId = SeedSchoolId,
			Name = "Debug Admin",
			Email = "admin@debugskolen.dk",
			Role = StaffRole.Teacher,
			KeycloakSubject = SeedAdminKeycloakSubject,
			IsAdmin = true,
		});

		await db.SaveChangesAsync();
	}

	private static async Task SeedCoursesAsync(AppDbContext context)
	{
		var anyExists = await context.Courses.IgnoreQueryFilters().AnyAsync(c => c.TenantId == SeedSchoolId);
		if (anyExists)
		{
			return;
		}

		context.Courses.AddRange(CourseSeeder.BuildStandardCourses(SeedSchoolId));

		await context.SaveChangesAsync();
	}
}
