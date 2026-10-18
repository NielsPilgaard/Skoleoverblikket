using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/courses")]
[Authorize]
public sealed class CoursesController(CourseService courses) : ControllerBase
{
	public record CourseDto(Guid Id, string Name, string? Description, string? Color, SubjectCategory? Category);
	public record UpsertCourseRequest(
		[Required, StringLength(200, MinimumLength = 1)] string Name,
		[StringLength(2000)] string? Description,
		[StringLength(7)] string? Color,
		SubjectCategory? Category);

	[HttpGet]
	public async Task<ActionResult<List<CourseDto>>> GetAll(CancellationToken cancellationToken) =>
		Ok(await courses.GetAllAsync(cancellationToken));

	[HttpGet("{id:guid}")]
	public async Task<ActionResult<CourseDto>> GetById(Guid id, CancellationToken cancellationToken) =>
		await courses.GetByIdAsync(id, cancellationToken) is { } course ? Ok(course) : NotFound();

	[HttpPost]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<CourseDto>> Create([FromBody] UpsertCourseRequest req, CancellationToken cancellationToken)
	{
		var course = await courses.CreateCourseAsync(req, cancellationToken);
		return CreatedAtAction(nameof(GetById), new { id = course.Id }, course);
	}

	[HttpPut("{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<CourseDto>> Update(Guid id, [FromBody] UpsertCourseRequest req, CancellationToken cancellationToken) =>
		await courses.UpdateCourseAsync(id, req, cancellationToken) is { } course ? Ok(course) : NotFound();

	[HttpDelete("{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
		await courses.DeleteCourseAsync(id, cancellationToken) ? NoContent() : NotFound();
}
