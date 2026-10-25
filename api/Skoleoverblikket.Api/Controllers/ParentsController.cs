using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Tenancy;
using System.ComponentModel.DataAnnotations;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/parents")]
[Authorize(Roles = Roles.Admin)]
[RequiresModule(SubscriptionModule.ParentModule)]
public sealed class ParentsController(
	AppDbContext db,
	ParentInvitationService invitationService,
	ILogger<ParentsController> logger) : ControllerBase
{
	public record ParentSummaryDto(Guid Id, string Name, string? Phone, string? Address, string? PostalCode, string? City, IReadOnlyList<StudentRefDto> Students, bool HasAccount, DateTimeOffset CreatedAt, bool AdresseBeskyttet);
	public record ParentDto(Guid Id, string Name, string Email, string? Phone, string? Address, string? PostalCode, string? City, IReadOnlyList<StudentRefDto> Students, bool HasAccount, DateTimeOffset CreatedAt, bool AdresseBeskyttet, CoParentDto? CoParent);
	public record StudentRefDto(Guid Id, string Name, Guid ClassId, string ClassName);
	public record CoParentDto(Guid Id, string Name);
	public record InviteParentRequest(
		[Required, StringLength(200, MinimumLength = 1)] string Name,
		[Required, EmailAddress, StringLength(500)] string Email,
		[Required] IReadOnlyList<Guid> StudentIds);

	[HttpGet]
	public async Task<ActionResult<List<ParentSummaryDto>>> GetAll(CancellationToken cancellationToken)
	{
		var parents = await db.Parents
			.AsNoTracking()
			.Include(p => p.Students).ThenInclude(s => s.Class)
			.OrderBy(p => p.Name)
			.ToListAsync(cancellationToken);

		return Ok(parents.Select(ToSummaryDto).ToList());
	}

	[HttpGet("{id:guid}")]
	public async Task<ActionResult<ParentDto>> GetById(Guid id, CancellationToken cancellationToken)
	{
		var parent = await db.Parents
			.AsNoTracking()
			.Include(p => p.Students).ThenInclude(s => s.Class)
			.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

		if (parent is null)
		{
			return Problem(statusCode: StatusCodes.Status404NotFound);
		}

		var studentIds = parent.Students.Select(s => s.Id).ToList();
		var coParent = await db.Parents
			.AsNoTracking()
			.Where(p => p.Id != id && p.Students.Any(s => studentIds.Contains(s.Id)))
			.OrderBy(p => p.Name)
			.Select(p => new CoParentDto(p.Id, p.Name))
			.FirstOrDefaultAsync(cancellationToken);

		return Ok(ToDto(parent, coParent));
	}

	[HttpPost("invite")]
	public async Task<ActionResult<ParentDto>> Invite([FromBody] InviteParentRequest req, CancellationToken cancellationToken)
	{
		(ParentInvitationService.InviteResult Result, Guid? ParentId) invite;
		try
		{
			invite = await invitationService.InviteAsync(req.Name, req.Email, req.StudentIds, cancellationToken);
		}
		catch (InvalidOperationException ex)
		{
			return Problem(title: "Konfigurationsfejl", detail: ex.Message, statusCode: 500);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to invite parent {Email}", req.Email);
			return Problem(title: "Kunne ikke sende invitation", detail: "Der opstod en fejl. Prøv igen.", statusCode: 502);
		}

		switch (invite.Result)
		{
			case ParentInvitationService.InviteResult.StudentsNotFound:
				return ValidationProblem("One or more student IDs were not found.");
			case ParentInvitationService.InviteResult.EmailInOtherSchool:
				return Problem(
					title: "E-mailadressen bruges på en anden skole",
					detail: "Denne e-mail bruges allerede på en anden skole. Brug en anden e-mailadresse.",
					statusCode: StatusCodes.Status409Conflict);
		}

		var withStudents = await db.Parents
			.AsNoTracking()
			.Include(p => p.Students).ThenInclude(s => s.Class)
			.FirstAsync(p => p.Id == invite.ParentId, cancellationToken);

		return CreatedAtAction(nameof(GetById), new { id = withStudents.Id }, ToDto(withStudents, coParent: null));
	}

	[HttpDelete("{id:guid}")]
	public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
		await invitationService.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();

	[HttpPost("{id:guid}/students/{studentId:guid}")]
	public async Task<ActionResult> LinkStudent(Guid id, Guid studentId, CancellationToken cancellationToken)
	{
		var parent = await db.Parents.Include(p => p.Students).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
		if (parent is null)
		{
			return NotFound();
		}

		var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, cancellationToken);
		if (student is null)
		{
			return NotFound();
		}

		if (parent.Students.Any(s => s.Id == studentId))
		{
			return Conflict();
		}

		parent.Students.Add(student);
		await db.SaveChangesAsync(cancellationToken);
		return NoContent();
	}

	[HttpDelete("{id:guid}/students/{studentId:guid}")]
	public async Task<ActionResult> UnlinkStudent(Guid id, Guid studentId, CancellationToken cancellationToken)
	{
		var parent = await db.Parents.Include(p => p.Students).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
		if (parent is null)
		{
			return NotFound();
		}

		var student = parent.Students.FirstOrDefault(s => s.Id == studentId);
		if (student is null)
		{
			return NotFound();
		}

		parent.Students.Remove(student);
		await db.SaveChangesAsync(cancellationToken);
		return NoContent();
	}

	[HttpPatch("{id:guid}/adresse-beskyttelse")]
	public async Task<ActionResult> SetAdresseBeskyttelse(Guid id, [FromBody] AdresseBeskyttelseRequest req, CancellationToken cancellationToken)
	{
		var parent = await db.Parents.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
		if (parent is null)
		{
			return NotFound();
		}

		parent.AdresseBeskyttet = req.AdresseBeskyttet;
		await db.SaveChangesAsync(cancellationToken);
		return NoContent();
	}

	public record AdresseBeskyttelseRequest(bool AdresseBeskyttet);

	public record UpdateParentContactRequest(
		[Required, StringLength(200, MinimumLength = 1)] string Name,
		[StringLength(50)] string? Phone,
		[StringLength(500)] string? Address,
		[StringLength(10)] string? PostalCode,
		[StringLength(100)] string? City);

	[HttpPatch("{id:guid}/contact")]
	public async Task<ActionResult> UpdateContact(Guid id, [FromBody] UpdateParentContactRequest req, CancellationToken cancellationToken)
	{
		var parent = await db.Parents.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
		if (parent is null)
		{
			return NotFound();
		}

		var trimmedName = req.Name.Trim();
		if (trimmedName.Length == 0)
		{
			return ValidationProblem(new ValidationProblemDetails { Errors = { ["name"] = ["Navn er påkrævet."] } });
		}

		if (!ContactValidation.TryNormalizePhone(req.Phone, out var normalizedPhone))
		{
			return ValidationProblem(new ValidationProblemDetails { Errors = { ["phone"] = ["Telefonnummer skal være 8 cifre, evt. med +45 foran."] } });
		}

		if (!ContactValidation.TryNormalizePostalCode(req.PostalCode, out var normalizedPostalCode))
		{
			return ValidationProblem(new ValidationProblemDetails { Errors = { ["postalCode"] = ["Postnummer skal være 4 cifre."] } });
		}

		parent.Name = trimmedName;
		parent.Phone = normalizedPhone;
		parent.Address = req.Address;
		parent.PostalCode = normalizedPostalCode;
		parent.City = req.City;
		await db.SaveChangesAsync(cancellationToken);
		return NoContent();
	}

	private static ParentSummaryDto ToSummaryDto(Parent p) => new(
		p.Id,
		p.Name,
		p.Phone,
		p.Address,
		p.PostalCode,
		p.City,
		p.Students.Select(s => new StudentRefDto(s.Id, s.Name, s.ClassId, s.Class?.Name ?? string.Empty)).ToList(),
		p.KeycloakSubject is not null,
		p.CreatedAt,
		p.AdresseBeskyttet);

	private static ParentDto ToDto(Parent p, CoParentDto? coParent) => new(
		p.Id,
		p.Name,
		p.Email,
		p.Phone,
		p.Address,
		p.PostalCode,
		p.City,
		p.Students.Select(s => new StudentRefDto(s.Id, s.Name, s.ClassId, s.Class?.Name ?? string.Empty)).ToList(),
		p.KeycloakSubject is not null,
		p.CreatedAt,
		p.AdresseBeskyttet,
		coParent);
}
