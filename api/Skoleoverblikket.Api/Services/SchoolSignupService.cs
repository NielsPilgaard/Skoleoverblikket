using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Email;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;

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
/// courses and the databehandleraftale acceptance given on the signup form. A day later
/// <see cref="WelcomeEmailJob"/> sends the founder's personal welcome email.
/// </summary>
public sealed class SchoolSignupService(
	AppDbContext db,
	KeycloakAdminService keycloakAdmin,
	DataProcessingAgreementService agreements,
	ITenantContext tenant,
	IEmailSender email,
	ILogger<SchoolSignupService> logger)
{
	/// <summary>The welcome email waits a day, so it arrives after the admin has had a look around.</summary>
	public static readonly TimeSpan WelcomeEmailDelay = TimeSpan.FromHours(24);

	/// <summary>A welcome email that still fails this long after it was due is given up on.</summary>
	private static readonly TimeSpan WelcomeEmailGiveUpAfter = TimeSpan.FromDays(3);

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
			WelcomeEmailDueAt = DateTimeOffset.UtcNow + WelcomeEmailDelay,
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

	/// <summary>
	/// Schools whose welcome email is due. Reads every school, like the retention job: the caller
	/// then pins each school with <see cref="HttpTenantContext.UseBackgroundTenant"/>.
	/// </summary>
	public async Task<IReadOnlyList<Guid>> ListWelcomeEmailsDueAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
		// School's query filter cannot be translated (its TenantId is not mapped), so it is skipped here.
		await db.Schools.IgnoreQueryFilters()
			.Where(s => s.WelcomeEmailDueAt != null && s.WelcomeEmailDueAt <= now.ToUniversalTime())
			.Select(s => s.Id)
			.ToListAsync(cancellationToken);

	/// <summary>
	/// Sends the current school's welcome email to the admin who signed up, if it is due. Sends
	/// before saving, so a failed send is retried on the next pass; a failed save only means a
	/// duplicate email.
	/// </summary>
	public async Task SendWelcomeEmailAsync(DateTimeOffset now, CancellationToken cancellationToken)
	{
		// School's query filter cannot be translated (its TenantId is not mapped), so match on Id.
		var school = await db.Schools.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == tenant.TenantId, cancellationToken);
		if (school is not { WelcomeEmailDueAt: { } dueAt } || dueAt > now)
		{
			return;
		}

		// Signup stores the admin's email as the school's contact email. If that admin is gone by
		// now, there is nobody to welcome.
		var adminName = school.ContactEmail is null
			? null
			: await db.Staff.AsNoTracking()
				.Where(s => s.Email == school.ContactEmail)
				.Select(s => s.Name)
				.FirstOrDefaultAsync(cancellationToken);

		if (adminName is not null)
		{
			try
			{
				// Signup saves "first last" as one name; the first word is the first name.
				var firstName = adminName.Split(' ', 2)[0];
				await email.SendAsync(WelcomeEmail.Build(school.ContactEmail!, firstName, school.Name), cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException && dueAt + WelcomeEmailGiveUpAfter < now)
			{
				logger.LogError(ex, "Gave up on welcome email for school {SchoolId}", school.Id);
			}
		}

		school.WelcomeEmailDueAt = null;
		await db.SaveChangesAsync(cancellationToken);
	}
}
