using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Integration tests for ParentInvitationsController at /api/v1/parent-invitations.
/// Covers the token preview and accept flow. Only the pending-invitation test invites, via <see cref="FakeKeycloak"/>.
/// Resend is excluded because it calls ParentInvitationService.CreateAndSendAsync.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class ParentInvitationTests(ApiFactory factory)
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
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, _tenantId, adminSubject: "admin-subject");
		_adminClient = _factory.CreateClient();
		_adminClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		_adminClient.DefaultRequestHeaders.Add("X-Test-Subject", "admin-subject");
	}

	// ── Helpers ──────────────────────────────────────────────────────────────────

	private async Task<(Parent parent, ParentInvitation invitation)> SeedInvitationAsync(
		string parentName = "Dorte Testforælder",
		bool expired = false)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		var parent = new Parent
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = parentName,
			Email = "dorte@testskole.dk",
			KeycloakSubject = null,
		};
		db.Parents.Add(parent);

		var invitation = new ParentInvitation
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			ParentId = parent.Id,
			Email = parent.Email,
			Token = "parent-token-" + Guid.NewGuid().ToString("N"),
			ExpiresAt = expired
				? DateTimeOffset.UtcNow.AddDays(-1)
				: DateTimeOffset.UtcNow.AddDays(14),
		};
		db.ParentInvitations.Add(invitation);

		await db.SaveChangesAsync();

		return (parent, invitation);
	}

	// ── GET /preview ─────────────────────────────────────────────────────────────

	[Test]
	public async Task PreviewInvitation_ValidToken_Returns200WithParentInfo()
	{
		var (parent, invitation) = await SeedInvitationAsync("Anne Testforælder");

		var response = await _adminClient.GetAsync($"/api/v1/parent-invitations/preview?token={invitation.Token}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var body = await response.Content.ReadFromJsonAsync<ParentInvitationPreviewDto>(JsonOpts);
		await Assert.That(body).IsNotNull();
		await Assert.That(body!.ParentName).IsEqualTo(parent.Name);
		await Assert.That(body.Email).IsEqualTo(invitation.Email);
		await Assert.That(body.SchoolName).IsNotNull();
	}

	[Test]
	public async Task PreviewInvitation_ExpiredToken_Returns404()
	{
		var (_, invitation) = await SeedInvitationAsync(expired: true);

		var response = await _adminClient.GetAsync($"/api/v1/parent-invitations/preview?token={invitation.Token}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task PreviewInvitation_MissingToken_Returns400()
	{
		var response = await _adminClient.GetAsync("/api/v1/parent-invitations/preview");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	// ── POST /accept ─────────────────────────────────────────────────────────────

	[Test]
	public async Task AcceptInvitation_ValidToken_Returns204AndSetsSubject()
	{
		const string acceptingSubject = "new-keycloak-subject-parent";
		var (parent, invitation) = await SeedInvitationAsync("Bent Testforælder");

		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Subject", acceptingSubject);

		var response = await client.PostAsync(
			$"/api/v1/parent-invitations/accept?token={invitation.Token}",
			content: null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		// Verify KeycloakSubject was persisted on the parent
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var updatedParent = await db.Parents.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == parent.Id);
		await Assert.That(updatedParent).IsNotNull();
		await Assert.That(updatedParent!.KeycloakSubject).IsEqualTo(acceptingSubject);
	}

	[Test]
	public async Task ParentList_ShowsPendingInvitationUntilAccepted()
	{
		// Inviting creates the login, so only this flag tells the list to keep offering a resend.
		var keycloak = new FakeKeycloak();
		await using var app = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
		{
			services.RemoveAll<IKeycloakAdminApi>();
			services.RemoveAll<IKeycloakTokenApi>();
			services.AddSingleton<IKeycloakAdminApi>(keycloak);
			services.AddSingleton<IKeycloakTokenApi>(keycloak);
		}));
		using var admin = app.CreateClient();
		admin.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		admin.DefaultRequestHeaders.Add("X-Test-Subject", "admin-subject");

		var email = $"lone-{Guid.NewGuid():N}@test.dk";
		var invite = await admin.PostAsJsonAsync("/api/v1/parents/invite",
			new ParentsController.InviteParentRequest("Lone Testforælder", email, []));
		await Assert.That(invite.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var parent = await invite.Content.ReadFromJsonAsync<ParentsController.ParentDto>(JsonOpts);
		var token = _factory.Emails.LastInvitationToken(email, "parent-invitation");

		async Task<ParentsController.ParentSummaryDto> SummaryAsync()
		{
			var list = await admin.GetFromJsonAsync<List<ParentsController.ParentSummaryDto>>(
				"/api/v1/parents", JsonOpts);
			return list!.Single(p => p.Id == parent!.Id);
		}

		var before = await SummaryAsync();
		await Assert.That(before.HasAccount).IsTrue();
		await Assert.That(before.HasPendingInvitation).IsTrue();

		using var parentClient = app.CreateClient();
		parentClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		parentClient.DefaultRequestHeaders.Add("X-Test-Subject", keycloak.SubjectFor(email));
		var accepted = await parentClient.PostAsync(
			$"/api/v1/parent-invitations/accept?token={token}", content: null);
		await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		await Assert.That((await SummaryAsync()).HasPendingInvitation).IsFalse();
	}

	[Test]
	public async Task AcceptInvitation_ExpiredToken_Returns404()
	{
		const string acceptingSubject = "expired-accept-subject-parent";
		var (_, invitation) = await SeedInvitationAsync(expired: true);

		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Subject", acceptingSubject);

		var response = await client.PostAsync(
			$"/api/v1/parent-invitations/accept?token={invitation.Token}",
			content: null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	// ── Private DTO for deserialising the anonymous /preview response ─────────────

	private sealed record ParentInvitationPreviewDto(
		string ParentName,
		string Email,
		string SchoolName,
		DateTimeOffset ExpiresAt);
}
