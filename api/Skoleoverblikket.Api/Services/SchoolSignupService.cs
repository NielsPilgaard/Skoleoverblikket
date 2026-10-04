using System.ComponentModel.DataAnnotations;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.Services;

public sealed record CreateTenantRequest(
	[Required]
	[MinLength(1)]
	[MaxLength(100)]
	string Name,
	[Required]
	[EmailAddress]
	string AdminEmail,
	[Required]
	[MinLength(1)]
	string AdminFirstName,
	[Required]
	[MinLength(1)]
	string AdminLastName,
	[Required]
	[MinLength(8)]
	string AdminPassword,
	[Range(typeof(bool), "true", "true", ErrorMessage = "Du skal acceptere databehandleraftalen for at oprette skolen.")]
	bool AcceptDataProcessingAgreement);

public sealed record TenantCreatedDto(Guid Id, string Name, string AdminEmail, string AccessToken, string? RefreshToken, int ExpiresIn);

/// <summary>
/// Self-serve signup: creates the school, its first admin (in Keycloak and as staff), the standard
/// courses and the databehandleraftale acceptance given on the signup form.
/// </summary>
public sealed class SchoolSignupService(
	AppDbContext db,
	KeycloakAdminService keycloakAdmin,
	DataProcessingAgreementService agreements,
	ILogger<SchoolSignupService> logger)
{
	public enum Failure
	{
		EmailTaken,
		AccountFailed,
		SaveFailed,
		LoginFailed,
	}

	public sealed record Result(TenantCreatedDto? Created, Failure? Failure = null, string? Detail = null);

	public async Task<Result> CreateAsync(CreateTenantRequest req, CancellationToken cancellationToken)
	{
		var school = new School
		{
			Id = Guid.NewGuid(),
			Name = req.Name,
			ContactEmail = req.AdminEmail,
		};

		db.Schools.Add(school);

		string keycloakSubject;
		try
		{
			keycloakSubject = await keycloakAdmin.CreateAdminUserAsync(
				email: req.AdminEmail,
				firstName: req.AdminFirstName,
				lastName: req.AdminLastName,
				password: req.AdminPassword,
				tenantId: school.Id,
				cancellationToken);
		}
		catch (KeycloakUserExistsException)
		{
			// A login belongs to one school (tenant_id is single-valued in Keycloak), so an existing
			// account cannot become this school's admin. Never touch it: not linked, not deleted on cleanup.
			return new Result(null, Failure.EmailTaken);
		}
		catch (KeycloakException ex)
		{
			return new Result(null, Failure.AccountFailed, ex.Message);
		}

		var adminName = $"{req.AdminFirstName} {req.AdminLastName}".Trim();
		db.Staff.Add(new Staff
		{
			Id = Guid.NewGuid(),
			TenantId = school.Id,
			Name = adminName,
			Email = req.AdminEmail,
			Role = StaffRole.Teacher,
			KeycloakSubject = keycloakSubject,
		});

		db.Courses.AddRange(CourseSeeder.BuildStandardCourses(school.Id));
		agreements.AddSignupAcceptance(school.Id, keycloakSubject, adminName, req.AdminEmail);

		try
		{
			await db.SaveChangesAsync(cancellationToken);
		}
		catch (Exception ex)
		{
			// Cleanup must not hide the save failure, and must run even if the request was canceled.
			try
			{
				await keycloakAdmin.DeleteStaffUserAsync(keycloakSubject, CancellationToken.None);
			}
			catch (Exception cleanupEx)
			{
				logger.LogError(cleanupEx, "Could not delete Keycloak user {KeycloakSubject} after failed signup save", keycloakSubject);
			}

			return new Result(null, Failure.SaveFailed, ex.Message);
		}

		TokenResponse token;
		try
		{
			token = await keycloakAdmin.GetTokenForUserAsync(req.AdminEmail, req.AdminPassword, cancellationToken);
		}
		catch (Exception ex)
		{
			return new Result(null, Failure.LoginFailed, ex.Message);
		}

		return new Result(new TenantCreatedDto(school.Id, school.Name, req.AdminEmail, token.AccessToken, token.RefreshToken, token.ExpiresIn));
	}
}
