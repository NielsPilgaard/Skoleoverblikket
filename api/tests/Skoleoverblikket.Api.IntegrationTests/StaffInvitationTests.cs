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
/// Integration tests for StaffInvitationsController at /api/v1/staff-invitations.
/// Covers the token preview and accept flow without touching Keycloak or email sending.
/// Admin list and by-staff lookup are also covered.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class StaffInvitationTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private readonly ApiFactory _factory = factory;
	private readonly Guid _tenantId = Guid.NewGuid();
	private HttpClient _adminClient = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, _tenantId);
		_adminClient = _factory.CreateClient();
		_adminClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		_adminClient.DefaultRequestHeaders.Add("X-Test-Roles", "admin");
		_adminClient.DefaultRequestHeaders.Add("X-Test-Subject", "admin-subject");
	}

	// ── Helpers ──────────────────────────────────────────────────────────────────

	private async Task<(Models.Staff staff, StaffInvitation invitation)> SeedInvitationAsync(
		string staffName = "Bo Testlærer",
		bool expired = false)
	{
		var staff = await TestDataBuilder.CreateStaffAsync(
			_factory.Services,
			_tenantId,
			name: staffName,
			keycloakSubject: null);

		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		var invitation = new StaffInvitation
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			StaffId = staff.Id,
			Email = "bo@testskole.dk",
			Token = "staff-token-" + Guid.NewGuid().ToString("N"),
			ExpiresAt = expired
				? DateTimeOffset.UtcNow.AddDays(-1)
				: DateTimeOffset.UtcNow.AddDays(14),
		};

		db.StaffInvitations.Add(invitation);
		await db.SaveChangesAsync();

		return (staff, invitation);
	}

	// ── GET /preview ─────────────────────────────────────────────────────────────

	[Test]
	public async Task PreviewInvitation_ValidToken_Returns200WithStaffInfo()
	{
		var (staff, invitation) = await SeedInvitationAsync("Eva Testlærer");

		var response = await _adminClient.GetAsync($"/api/v1/staff-invitations/preview?token={invitation.Token}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var body = await response.Content.ReadFromJsonAsync<StaffInvitationPreviewDto>(JsonOpts);
		await Assert.That(body).IsNotNull();
		await Assert.That(body!.StaffName).IsEqualTo(staff.Name);
		await Assert.That(body.Email).IsEqualTo(invitation.Email);
		await Assert.That(body.SchoolName).IsNotNull();
		await Assert.That(body.ExpiresAt).IsGreaterThan(DateTimeOffset.UtcNow);
	}

	[Test]
	public async Task PreviewInvitation_ExpiredToken_Returns404()
	{
		var (_, invitation) = await SeedInvitationAsync(expired: true);

		var response = await _adminClient.GetAsync($"/api/v1/staff-invitations/preview?token={invitation.Token}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task PreviewInvitation_MissingToken_Returns400()
	{
		var response = await _adminClient.GetAsync("/api/v1/staff-invitations/preview");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	// ── POST /accept ─────────────────────────────────────────────────────────────

	[Test]
	public async Task AcceptInvitation_ValidToken_Returns204AndSetsSubject()
	{
		const string acceptingSubject = "new-keycloak-subject-staff";
		var (staff, invitation) = await SeedInvitationAsync("Finn Testlærer");

		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", acceptingSubject);

		var request = new StaffInvitationsController.AcceptInvitationRequest(
			invitation.Token,
			acceptingSubject);

		var response = await client.PostAsJsonAsync("/api/v1/staff-invitations/accept", request);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		// Verify KeycloakSubject was persisted
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var updatedStaff = await db.Staff.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == staff.Id);
		await Assert.That(updatedStaff).IsNotNull();
		await Assert.That(updatedStaff!.KeycloakSubject).IsEqualTo(acceptingSubject);
	}

	[Test]
	public async Task AcceptInvitation_ExpiredToken_Returns400()
	{
		const string acceptingSubject = "expired-accept-subject";
		var (_, invitation) = await SeedInvitationAsync(expired: true);

		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", acceptingSubject);

		var request = new StaffInvitationsController.AcceptInvitationRequest(
			invitation.Token,
			acceptingSubject);

		var response = await client.PostAsJsonAsync("/api/v1/staff-invitations/accept", request);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	// ── GET / ────────────────────────────────────────────────────────────────────

	[Test]
	public async Task GetAll_Admin_Returns200WithSeededInvitation()
	{
		var (staff, invitation) = await SeedInvitationAsync("Gitte Testlærer");

		var response = await _adminClient.GetAsync("/api/v1/staff-invitations");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var list = await response.Content.ReadFromJsonAsync<List<StaffInvitationsController.InvitationDto>>(JsonOpts);
		await Assert.That(list).IsNotNull();
		await Assert.That(list!.Any(i => i.Id == invitation.Id)).IsTrue();
		await Assert.That(list.Any(i => i.StaffId == staff.Id)).IsTrue();
	}

	// ── GET /by-staff/{staffId} ───────────────────────────────────────────────────

	[Test]
	public async Task GetByStaff_Admin_Returns200WithMatchingInvitation()
	{
		var (staff, invitation) = await SeedInvitationAsync("Hans Testlærer");

		var response = await _adminClient.GetAsync($"/api/v1/staff-invitations/by-staff/{staff.Id}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var list = await response.Content.ReadFromJsonAsync<List<StaffInvitationsController.InvitationDto>>(JsonOpts);
		await Assert.That(list).IsNotNull();
		await Assert.That(list!.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(list.All(i => i.StaffId == staff.Id)).IsTrue();
		await Assert.That(list.Any(i => i.Id == invitation.Id)).IsTrue();
	}

	// ── Private DTO for deserialising the anonymous /preview response ─────────────

	private sealed record StaffInvitationPreviewDto(
		string StaffName,
		string Email,
		string SchoolName,
		DateTimeOffset ExpiresAt);
}
