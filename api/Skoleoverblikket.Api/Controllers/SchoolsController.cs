using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/schools")]
[Authorize(Roles = Roles.Admin)]
public sealed class SchoolsController(SchoolSettingsService settings) : ControllerBase
{
	public record SchoolSettingsDto(string Name, string? ContactEmail, string? ContactPhone, string? LogoUrl);

	public record OnboardingStatusDto(
		bool HasLogo,
		int StaffCount,
		int ClassCount,
		int CourseCount,
		int RoomCount,
		int StepsCompleted,
		int StepsTotal);

	public record UpdateSchoolSettingsRequest(
		[Required]
		[StringLength(200, MinimumLength = 1)]
		string Name,
		[EmailAddress] string? ContactEmail,
		[Phone] string? ContactPhone);

	[HttpGet("settings")]
	public async Task<ActionResult<SchoolSettingsDto>> GetSettings(CancellationToken cancellationToken) =>
		await settings.GetAsync(cancellationToken) is { } school ? Ok(school) : NotFound();

	[HttpPut("settings")]
	public async Task<ActionResult<SchoolSettingsDto>> UpdateSettings(
		[FromBody] UpdateSchoolSettingsRequest req,
		CancellationToken cancellationToken) =>
		await settings.UpdateAsync(req, cancellationToken) is { } school ? Ok(school) : NotFound();

	internal static string OnboardingCacheKey(Guid tenantId) => $"onboarding:{tenantId}";

	[HttpGet("onboarding-status")]
	public async Task<ActionResult<OnboardingStatusDto>> GetOnboardingStatus(
		[FromServices] OnboardingService onboarding,
		CancellationToken cancellationToken)
	{
		var result = await onboarding.GetStatusAsync(cancellationToken);

		return result is null ? NotFound() : Ok(result);
	}

	[HttpPost("logo")]
	[Consumes("multipart/form-data")]
	public async Task<ActionResult<SchoolSettingsDto>> UploadLogo(IFormFile file, CancellationToken cancellationToken)
	{
		await using var stream = file.OpenReadStream();
		var (result, school) = await settings.UploadLogoAsync(file.FileName, file.Length, stream, cancellationToken);

		return result switch
		{
			SchoolSettingsService.LogoResult.Done => Ok(school),
			SchoolSettingsService.LogoResult.TooLarge => LogoProblem("Logo må maksimalt være 2 MB."),
			SchoolSettingsService.LogoResult.UnsupportedType => LogoProblem("Kun PNG, JPG og WebP er tilladt."),
			_ => NotFound(),
		};
	}

	private ActionResult LogoProblem(string message) =>
		ValidationProblem(new ValidationProblemDetails { Errors = { ["file"] = [message] } });
}
