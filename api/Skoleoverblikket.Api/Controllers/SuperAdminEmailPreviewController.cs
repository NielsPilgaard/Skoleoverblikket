using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Email;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/admin/email-preview")]
[Authorize(Roles = Roles.SuperAdmin)]
public sealed class SuperAdminEmailPreviewController(IOptions<ApplicationOptions> appOptions) : ControllerBase
{
	[HttpGet("staff-invitation")]
	[Produces("text/html")]
	public ContentResult StaffInvitation(
		[FromQuery] string name = "Mette Hansen",
		[FromQuery] string school = "Testskolen",
		[FromQuery] bool withPassword = true)
	{
		var baseUrl = appOptions.Value.SanitizedBaseUrl;
		var link = $"{baseUrl}/invitation/preview-token";
		const string password = "Abc!12345xyz";

		var html = StaffInvitationEmail.BuildHtml(name, school, link, withPassword ? password : null);
		return Content(html, "text/html");
	}

	[HttpGet("parent-invitation")]
	[Produces("text/html")]
	public ContentResult ParentInvitation(
		[FromQuery] string name = "Lars Andersen",
		[FromQuery] string school = "Testskolen",
		[FromQuery] bool withPassword = true)
	{
		var baseUrl = appOptions.Value.SanitizedBaseUrl;
		var link = $"{baseUrl}/parent-invitation/preview-token";
		const string password = "Abc!12345xyz";

		var html = ParentInvitationEmail.BuildHtml(name, school, link, withPassword ? password : null);
		return Content(html, "text/html");
	}

	[HttpGet("notification")]
	[Produces("text/html")]
	public ContentResult Notification(
		[FromQuery] string body = "Du har fået en ny besked i kontaktbogen.")
	{
		var baseUrl = appOptions.Value.SanitizedBaseUrl;
		var settingsUrl = $"{baseUrl}/indstillinger/notifikationer";

		var html = BuildNotification(body, settingsUrl);
		return Content(html, "text/html");
	}

	/// <summary>The founder's personal welcome email a new school's admin gets a day after signup.</summary>
	[HttpGet("welcome")]
	[Produces("text/html")]
	public ContentResult Welcome([FromQuery] string name = "Mette", [FromQuery] string school = "Testskolen") =>
		Content(WelcomeEmail.Build("preview@skoleoverblikket.dk", name, school).HtmlBody, "text/html");

	/// <summary>The warning a canceled school's admins get 7 days before all its data is deleted.</summary>
	[HttpGet("deletion-warning")]
	[Produces("text/html")]
	public ContentResult DeletionWarning([FromQuery] string school = "Testskolen")
	{
		var (_, html, _) = SchoolDeletionService.DeletionWarningEmail(
			school, DateTimeOffset.UtcNow.AddDays(7), appOptions.Value.SanitizedBaseUrl);
		return Content(html, "text/html");
	}

	/// <summary>The Bcc notice every school's admins get before the sub-processor list changes.</summary>
	[HttpGet("sub-processor-notice")]
	[Produces("text/html")]
	public ContentResult SubProcessorNotice(
		[FromQuery] string change = "Vi tilføjer Alexandra Instituttet (Danmark) til AI-forslag til skemaer.")
	{
		var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(31));
		var (_, html) = DataProcessingAgreementService.SubProcessorNoticeEmail(
			new SubProcessorNoticeRequest(change, effectiveFrom), appOptions.Value.SanitizedBaseUrl);
		return Content(html, "text/html");
	}

	private static string BuildNotification(string body, string settingsUrl)
	{
		var encodedBody = HtmlEncoder.Default.Encode(body);
		var encodedSettingsUrl = HtmlEncoder.Default.Encode(settingsUrl);

		return EmailTemplate.Wrap("Notifikation", $"""
            <p>{encodedBody}</p>
            <div class="notice">
              <p style="margin-bottom:0;">Du modtager denne e-mail, fordi du har slået e-mailnotifikationer til.
              <a href="{encodedSettingsUrl}" style="color:#1f6321;">Administrér notifikationer</a></p>
            </div>
            """);
	}
}
