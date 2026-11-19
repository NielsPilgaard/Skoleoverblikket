using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.IntegrationTests;

[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class ViewModeTests(ApiFactory factory)
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
	}

	private HttpClient CreateClientWithRoles(params string[] roles)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", string.Join(",", roles));
		return client;
	}

	private HttpClient CreateClientWithSubject(string subject, params string[] roles)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add("X-Test-Roles", string.Join(",", roles));
		}

		return client;
	}

	[Test]
	public async Task GetMe_ReturnsCorrectStaff_ForAuthenticatedUser()
	{
		const string subject = "teacher-kc-subject";
		var staff = await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId,
			name: "Mette Lærer", role: StaffRole.Teacher,
			keycloakSubject: subject);

		using var client = CreateClientWithSubject(subject, "teacher");

		var response = await client.GetAsync("/api/v1/staff/me");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<StaffController.StaffDto>(JsonOpts);
		await Assert.That(dto!.Id).IsEqualTo(staff.Id);
		await Assert.That(dto.Name).IsEqualTo("Mette Lærer");
		await Assert.That(dto.Role).IsEqualTo(StaffRole.Teacher);
		await Assert.That(dto.IsAdmin).IsFalse();
	}

	[Test]
	public async Task GetMe_Returns403_WhenSubjectNotLinkedToStaff()
	{
		// No Staff row means no staff role, so the staff endpoints turn the login away.
		using var client = CreateClientWithSubject("unknown-subject", "teacher");

		var response = await client.GetAsync("/api/v1/staff/me");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task AdminOnlyEndpoint_Returns403_ForTeacherRole()
	{
		using var client = CreateClientWithRoles("teacher");

		// POST /api/v1/staff requires [Authorize(Roles = "admin")]
		var response = await client.PostAsJsonAsync("/api/v1/staff",
			new { name = "Test", role = "Teacher" });

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task AdminOnlyEndpoint_Returns201_ForAdminRole()
	{
		var response = await _adminClient.PostAsJsonAsync("/api/v1/staff",
			new { name = "Ny Medarbejder", role = "Teacher" });

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
	}

	[Test]
	public async Task GetMe_Returns404_WhenNoSubjectClaim()
	{
		// Default test-user-id subject — no staff seeded with that subject
		var response = await _adminClient.GetAsync("/api/v1/staff/me");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}
}
