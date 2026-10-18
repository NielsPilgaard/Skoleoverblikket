using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;
using static Skoleoverblikket.Api.Controllers.CoursesController;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// The one writer of <see cref="Course"/> (fag) rows, apart from <see cref="CourseSeeder"/> seeding standard subjects at signup.
/// The request and response records stay nested in <see cref="CoursesController"/> so their OpenAPI schema names,
/// and with them the generated web client, are unchanged.
/// </summary>
public sealed class CourseService(AppDbContext db, ITenantContext tenant)
{
	public Task<List<CourseDto>> GetAllAsync(CancellationToken cancellationToken) =>
		db.Courses
			.AsNoTracking()
			.OrderBy(c => c.Name)
			.Select(c => new CourseDto(c.Id, c.Name, c.Description, c.Color, c.Category))
			.ToListAsync(cancellationToken);

	public Task<CourseDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
		db.Courses
			.AsNoTracking()
			.Where(c => c.Id == id)
			.Select(c => new CourseDto(c.Id, c.Name, c.Description, c.Color, c.Category))
			.FirstOrDefaultAsync(cancellationToken);

	public async Task<CourseDto> CreateCourseAsync(UpsertCourseRequest req, CancellationToken cancellationToken)
	{
		var course = new Course
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			Name = req.Name,
			Description = req.Description,
			Color = req.Color,
			Category = req.Category,
		};
		db.Courses.Add(course);
		await db.SaveChangesAsync(cancellationToken);
		return new CourseDto(course.Id, course.Name, course.Description, course.Color, course.Category);
	}

	public async Task<CourseDto?> UpdateCourseAsync(Guid id, UpsertCourseRequest req, CancellationToken cancellationToken)
	{
		var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
		if (course is null)
		{
			return null;
		}

		course.Name = req.Name;
		course.Description = req.Description;
		course.Color = req.Color;
		course.Category = req.Category;
		await db.SaveChangesAsync(cancellationToken);
		return new CourseDto(course.Id, course.Name, course.Description, course.Color, course.Category);
	}

	/// <summary>Deletes a fag. Returns false when it doesn't exist.</summary>
	public async Task<bool> DeleteCourseAsync(Guid id, CancellationToken cancellationToken)
	{
		var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
		if (course is null)
		{
			return false;
		}

		db.Courses.Remove(course);
		await db.SaveChangesAsync(cancellationToken);
		return true;
	}
}
