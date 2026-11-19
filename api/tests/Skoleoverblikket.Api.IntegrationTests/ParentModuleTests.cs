using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.IntegrationTests;

[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class ParentModuleTests(ApiFactory factory)
{
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
	}

	// -- helper to insert a parent + student directly via DB --
	private async Task<(Parent parent, Student student, Class klass)> SeedParentWithStudentAsync(
		string keycloakSubject)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		var klass = new Class
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = "3.a",
		};
		db.Classes.Add(klass);

		var student = new Student
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = "Lars Larsen",
			ClassId = klass.Id,
		};
		db.Students.Add(student);

		var parent = new Parent
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = "Bente Larsen",
			Email = "bente@test.dk",
			KeycloakSubject = keycloakSubject,
		};
		parent.Students.Add(student);
		db.Parents.Add(parent);

		await db.SaveChangesAsync();
		return (parent, student, klass);
	}

	[Test]
	public async Task GetParents_ReturnsEmptyList_WhenNoneExist()
	{
		var response = await _adminClient.GetAsync("/api/v1/parents");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var list = await response.Content.ReadFromJsonAsync<List<ParentsController.ParentSummaryDto>>();
		await Assert.That(list).IsNotNull();
		await Assert.That(list!.Count).IsEqualTo(0);
	}

	[Test]
	public async Task GetParents_Returns403_ForNonAdmin()
	{
		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "teacher");

		var response = await client.GetAsync("/api/v1/parents");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task GetStudents_ReturnsEmptyList_WhenNoneExist()
	{
		var response = await _adminClient.GetAsync("/api/v1/students");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var list = await response.Content.ReadFromJsonAsync<List<StudentsController.StudentDto>>();
		await Assert.That(list).IsNotNull();
		await Assert.That(list!.Count).IsEqualTo(0);
	}

	[Test]
	public async Task GetStudents_Returns403_ForNonAdmin()
	{
		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "teacher");

		var response = await client.GetAsync("/api/v1/students");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task GetSchemas_Returns200_ForParent_WithAccessToClass()
	{
		const string parentSubject = "parent-sub-001";
		var (_, _, klass) = await SeedParentWithStudentAsync(parentSubject);

		using var parentClient = _factory.CreateClient();
		parentClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		parentClient.DefaultRequestHeaders.Add("X-Test-Roles", "parent");
		parentClient.DefaultRequestHeaders.Add("X-Test-Subject", parentSubject);

		var response = await parentClient.GetAsync($"/api/v1/classes/{klass.Id}/schemas");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task GetSchemas_Returns403_ForParent_WithNoAccessToClass()
	{
		const string parentSubject = "parent-sub-002";
		await SeedParentWithStudentAsync(parentSubject);

		// A different class this parent has no student in
		var otherClass = new Class
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = "7.x",
		};
		using (var scope = _factory.Services.CreateScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			db.Classes.Add(otherClass);
			await db.SaveChangesAsync();
		}

		using var parentClient = _factory.CreateClient();
		parentClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		parentClient.DefaultRequestHeaders.Add("X-Test-Roles", "parent");
		parentClient.DefaultRequestHeaders.Add("X-Test-Subject", parentSubject);

		var response = await parentClient.GetAsync($"/api/v1/classes/{otherClass.Id}/schemas");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task GetSchemas_Returns200_ForAdmin_Always()
	{
		var (_, _, klass) = await SeedParentWithStudentAsync("some-other-subject");

		var response = await _adminClient.GetAsync($"/api/v1/classes/{klass.Id}/schemas");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task UpdateContact_Returns204_AtMaxFieldLengths()
	{
		const string parentSubject = "parent-sub-boundary-max";
		await SeedParentWithStudentAsync(parentSubject);

		using var parentClient = _factory.CreateClient();
		parentClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		parentClient.DefaultRequestHeaders.Add("X-Test-Roles", "parent");
		parentClient.DefaultRequestHeaders.Add("X-Test-Subject", parentSubject);

		var response = await parentClient.PatchAsJsonAsync("/api/v1/parents/me/contact", new
		{
			Name = new string('N', 200),
			Phone = "+4512345678",
			Address = new string('A', 500),
			PostalCode = "1234",
			City = new string('C', 100),
			ShareContactInfo = true,
		});

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task UpdateContact_Returns400_WhenPhoneIsInvalidFormat()
	{
		var response = await PatchContactAsync("parent-sub-boundary-over-phone", new
		{
			Name = "Bente Larsen",
			Phone = "not-a-phone-number",
			Address = (string?)null,
			PostalCode = (string?)null,
			City = (string?)null,
			ShareContactInfo = true,
		});

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task UpdateContact_Returns204_WhenPhoneHasSpacesAndPrefix()
	{
		var response = await PatchContactAsync("parent-sub-phone-spaces", new
		{
			Name = "Bente Larsen",
			Phone = "+45 12 34 56 78",
			Address = (string?)null,
			PostalCode = (string?)null,
			City = (string?)null,
			ShareContactInfo = true,
		});

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task UpdateContact_Returns400_WhenAddressExceedsMaxLength()
	{
		var response = await PatchContactAsync("parent-sub-boundary-over-address", new
		{
			Name = "Bente Larsen",
			Phone = (string?)null,
			Address = new string('A', 501),
			PostalCode = (string?)null,
			City = (string?)null,
			ShareContactInfo = true,
		});

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task UpdateContact_Returns400_WhenPostalCodeIsInvalidFormat()
	{
		var response = await PatchContactAsync("parent-sub-boundary-over-postalcode", new
		{
			Name = "Bente Larsen",
			Phone = (string?)null,
			Address = (string?)null,
			PostalCode = "12345",
			City = (string?)null,
			ShareContactInfo = true,
		});

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task UpdateContact_Returns400_WhenCityExceedsMaxLength()
	{
		var response = await PatchContactAsync("parent-sub-boundary-over-city", new
		{
			Name = "Bente Larsen",
			Phone = (string?)null,
			Address = (string?)null,
			PostalCode = (string?)null,
			City = new string('C', 101),
			ShareContactInfo = true,
		});

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	private async Task<HttpResponseMessage> PatchContactAsync(string parentSubject, object body)
	{
		await SeedParentWithStudentAsync(parentSubject);

		using var parentClient = _factory.CreateClient();
		parentClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		parentClient.DefaultRequestHeaders.Add("X-Test-Roles", "parent");
		parentClient.DefaultRequestHeaders.Add("X-Test-Subject", parentSubject);

		return await parentClient.PatchAsJsonAsync("/api/v1/parents/me/contact", body);
	}
}
