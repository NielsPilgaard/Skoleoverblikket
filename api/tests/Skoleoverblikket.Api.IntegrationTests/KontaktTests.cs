using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Integration tests for KontaktController.
/// Covers the ShareContactInfo consent filtering rules:
///   - Admin sees all parents regardless of ShareContactInfo.
///   - Staff (non-admin, non-parent) sees only parents with ShareContactInfo=true.
///   - Parent sees only co-class parents with ShareContactInfo=true.
///   - Parent with no co-class parents that have consent gets an empty list.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class KontaktTests(ApiFactory factory)
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
		_adminClient.DefaultRequestHeaders.Add("X-Test-Subject", "kontakt-admin-subject");
	}

	// ── Private helpers ───────────────────────────────────────────────────────────

	private HttpClient CreateStaffClient(string subject)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	private HttpClient CreateParentClient(string subject)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "parent");
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	/// <summary>
	/// Creates a Parent with a Student linked to the given class.
	/// </summary>
	private async Task<Parent> CreateParentWithStudentAsync(
		string subject,
		Guid classId,
		bool shareContactInfo,
		string name)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		var student = new Student
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = $"{name} Junior",
			ClassId = classId,
		};
		db.Students.Add(student);

		var parent = new Parent
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = name,
			Email = $"{subject}@test.dk",
			KeycloakSubject = subject,
			ShareContactInfo = shareContactInfo,
		};
		parent.Students.Add(student);
		db.Parents.Add(parent);

		await db.SaveChangesAsync();
		return parent;
	}

	/// <summary>
	/// Creates a Parent without any linked students.
	/// </summary>
	private async Task<Parent> CreateParentAsync(
		string subject,
		bool shareContactInfo,
		string name)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		var parent = new Parent
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = name,
			Email = $"{subject}@test.dk",
			KeycloakSubject = subject,
			ShareContactInfo = shareContactInfo,
		};
		db.Parents.Add(parent);

		await db.SaveChangesAsync();
		return parent;
	}

	// ── Tests ─────────────────────────────────────────────────────────────────────

	[Test]
	public async Task GetKontakt_Admin_SeesAllParents()
	{
		// Arrange: one parent with consent, one without
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "admin-sees-all-1a");

		var consentingParent = await CreateParentWithStudentAsync(
			"admin-test-consenting-parent", klass.Id, shareContactInfo: true, "Annette Ja");
		var nonConsentingParent = await CreateParentWithStudentAsync(
			"admin-test-nonconsenting-parent", klass.Id, shareContactInfo: false, "Boris Nej");

		// Act
		var response = await _adminClient.GetAsync("/api/v1/kontakt");

		// Assert: admin sees both
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var list = await response.Content.ReadFromJsonAsync<List<KontaktController.KontaktParentDto>>();
		await Assert.That(list).IsNotNull();
		await Assert.That(list!.Any(p => p.Id == consentingParent.Id)).IsTrue();
		await Assert.That(list.Any(p => p.Id == nonConsentingParent.Id)).IsTrue();
	}

	[Test]
	public async Task GetKontakt_Staff_SeesOnlyConsentingParents()
	{
		// Arrange: one parent with consent, one without
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "staff-consent-filter-2a");

		var consentingParent = await CreateParentWithStudentAsync(
			"staff-test-consenting-parent", klass.Id, shareContactInfo: true, "Charlotte Ja");
		var nonConsentingParent = await CreateParentWithStudentAsync(
			"staff-test-nonconsenting-parent", klass.Id, shareContactInfo: false, "Dennis Nej");

		// Act
		using var staffClient = CreateStaffClient("staff-test-caller");
		var response = await staffClient.GetAsync("/api/v1/kontakt");

		// Assert: staff sees only the consenting parent
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var list = await response.Content.ReadFromJsonAsync<List<KontaktController.KontaktParentDto>>();
		await Assert.That(list).IsNotNull();
		await Assert.That(list!.Any(p => p.Id == consentingParent.Id)).IsTrue();
		await Assert.That(list.Any(p => p.Id == nonConsentingParent.Id)).IsFalse();
	}

	[Test]
	public async Task GetKontakt_Parent_SeesOnlyCoClassConsentingParents()
	{
		// Arrange:
		//   - requesting parent + student in class A
		//   - co-class parent with consent in class A  → should appear
		//   - co-class parent without consent in class A → should NOT appear
		//   - parent in a different class with consent → should NOT appear
		var (classA, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "parent-coclass-3a");
		var (classB, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "parent-coclass-3b");

		// The requesting parent is in class A
		await CreateParentWithStudentAsync(
			"parent-coclass-requester", classA.Id, shareContactInfo: true, "Eva Requester");

		// Co-class parent with consent — should be visible
		var coClassConsenting = await CreateParentWithStudentAsync(
			"parent-coclass-consenting", classA.Id, shareContactInfo: true, "Freja Ja");

		// Co-class parent without consent — should NOT be visible
		var coClassNonConsenting = await CreateParentWithStudentAsync(
			"parent-coclass-nonconsenting", classA.Id, shareContactInfo: false, "Georg Nej");

		// Parent in a different class with consent — should NOT be visible
		var differentClassParent = await CreateParentWithStudentAsync(
			"parent-coclass-otherclass", classB.Id, shareContactInfo: true, "Hanne Anden");

		// Act
		using var parentClient = CreateParentClient("parent-coclass-requester");
		var response = await parentClient.GetAsync("/api/v1/kontakt");

		// Assert
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var list = await response.Content.ReadFromJsonAsync<List<KontaktController.KontaktParentDto>>();
		await Assert.That(list).IsNotNull();
		await Assert.That(list!.Any(p => p.Id == coClassConsenting.Id)).IsTrue();
		await Assert.That(list.Any(p => p.Id == coClassNonConsenting.Id)).IsFalse();
		await Assert.That(list.Any(p => p.Id == differentClassParent.Id)).IsFalse();
	}

	[Test]
	public async Task GetKontakt_Parent_NoCoClassParentsWithConsent_ReturnsEmpty()
	{
		// Arrange: requesting parent in class, one co-class parent but without consent
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "parent-empty-result-4a");

		await CreateParentWithStudentAsync(
			"parent-empty-requester", klass.Id, shareContactInfo: false, "Ida Requester");
		await CreateParentWithStudentAsync(
			"parent-empty-coclass-noconsent", klass.Id, shareContactInfo: false, "Jens Nej");

		// Act
		using var parentClient = CreateParentClient("parent-empty-requester");
		var response = await parentClient.GetAsync("/api/v1/kontakt");

		// Assert: 200 with empty list — no co-class parents have consent
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var list = await response.Content.ReadFromJsonAsync<List<KontaktController.KontaktParentDto>>();
		await Assert.That(list).IsNotNull();
		await Assert.That(list!.Count).IsEqualTo(0);
	}
}
