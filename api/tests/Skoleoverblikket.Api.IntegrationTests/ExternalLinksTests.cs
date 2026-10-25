using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Links (/api/v1/external-links): admin CRUD and reorder, who sees which link (parents by their
/// children's classes, staff and board by audience), URL rules and tenant isolation.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class ExternalLinksTests(ApiFactory factory)
{
	private readonly Guid _tenantId = Guid.NewGuid();
	private HttpClient _admin = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(factory.Services, _tenantId);
		var adminSubject = $"links-admin-{Guid.NewGuid():N}";
		await TestDataBuilder.CreateStaffAsync(factory.Services, _tenantId, "Hanne Kontor", isAdmin: true, keycloakSubject: adminSubject);
		_admin = Client(_tenantId, adminSubject, "admin");
	}

	[After(Test)]
	public void TearDown() => _admin.Dispose();

	[Test]
	public async Task Admin_CreatesUpdatesReordersAndDeletesLinks()
	{
		var aula = await CreateLinkAsync(Link("Aula", "https://www.aula.dk", parents: true, staff: true));
		var bus = await CreateLinkAsync(Link("Buskort", "https://buskort.dk", parents: true));

		var update = await _admin.PutAsJsonAsync($"/api/v1/external-links/{bus}",
			Link("Buskort Midttrafik", "https://midttrafik.dk", parents: true) with { Description = "Bestil inden 1. august" });
		await Assert.That(update.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var reorder = await _admin.PutAsJsonAsync("/api/v1/external-links/order", new ReorderExternalLinksRequest([bus, aula]));
		await Assert.That(reorder.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var all = await GetAllAsync();
		await Assert.That(all.Select(l => l.Id).ToList()).IsEquivalentTo([bus, aula]);
		await Assert.That(all[0].Id).IsEqualTo(bus);
		await Assert.That(all[0].Title).IsEqualTo("Buskort Midttrafik");
		await Assert.That(all[0].Url).IsEqualTo("https://midttrafik.dk");
		await Assert.That(all[0].Description).IsEqualTo("Bestil inden 1. august");
		await Assert.That(all[1].VisibleToStaff).IsTrue();

		// A link added after reordering goes to the end.
		var website = await CreateLinkAsync(Link("Skolens hjemmeside", "https://skole.dk", staff: true));
		await Assert.That((await GetAllAsync()).Last().Id).IsEqualTo(website);

		var stale = await _admin.PutAsJsonAsync("/api/v1/external-links/order", new ReorderExternalLinksRequest([bus, aula]));
		await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);

		var delete = await _admin.DeleteAsync($"/api/v1/external-links/{aula}");
		await Assert.That(delete.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That((await GetAllAsync()).Select(l => l.Id)).IsEquivalentTo([bus, website]);
		await Assert.That((await _admin.DeleteAsync($"/api/v1/external-links/{aula}")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task Parent_SeesSchoolWideAndOwnClassLinks_NotOtherClassOrOtherAudiences()
	{
		var ownClass = await CreateClassAsync("3.a");
		var otherClass = await CreateClassAsync("5.b");
		var parentSubject = await CreateParentAsync(ownClass);

		var schoolWide = await CreateLinkAsync(Link("Aula", "https://aula.dk", parents: true));
		var ownClassLink = await CreateLinkAsync(Link("3.a på Facebook", "https://facebook.com/groups/3a", parents: true) with { ClassIds = [ownClass] });
		await CreateLinkAsync(Link("5.b på Facebook", "https://facebook.com/groups/5b", parents: true) with { ClassIds = [otherClass] });
		await CreateLinkAsync(Link("Personaleportal", "https://intra.skole.dk", staff: true));
		await CreateLinkAsync(Link("Bestyrelsesmappe", "https://drev.skole.dk", board: true));

		using var parent = Client(_tenantId, parentSubject, "parent");
		var visible = await GetVisibleAsync(parent);

		await Assert.That(visible.Select(l => l.Id).ToList()).IsEquivalentTo([schoolWide, ownClassLink]);
		await Assert.That(visible[0].Id).IsEqualTo(schoolWide);
	}

	[Test]
	public async Task Staff_SeesStaffLinksRegardlessOfClass_NotParentOnlyLinks()
	{
		var klass = await CreateClassAsync("4.c");
		var staffSubject = $"links-staff-{Guid.NewGuid():N}";
		await TestDataBuilder.CreateStaffAsync(factory.Services, _tenantId, keycloakSubject: staffSubject);

		var classLink = await CreateLinkAsync(Link("4.c forældreintra", "https://4c.dk", parents: true, staff: true) with { ClassIds = [klass] });
		var staffLink = await CreateLinkAsync(Link("Personaleportal", "https://intra.skole.dk", staff: true));
		await CreateLinkAsync(Link("Buskort", "https://buskort.dk", parents: true));
		await CreateLinkAsync(Link("Bestyrelsesmappe", "https://drev.skole.dk", board: true));

		using var staff = Client(_tenantId, staffSubject, "user");
		var visible = await GetVisibleAsync(staff);

		await Assert.That(visible.Select(l => l.Id)).IsEquivalentTo([classLink, staffLink]);
	}

	[Test]
	public async Task Board_SeesBoardLinksOnly()
	{
		var boardSubject = $"links-board-{Guid.NewGuid():N}";
		await TestDataBuilder.CreateBoardMemberAsync(factory.Services, _tenantId, boardSubject);

		var boardLink = await CreateLinkAsync(Link("Bestyrelsesmappe", "https://drev.skole.dk", board: true));
		await CreateLinkAsync(Link("Aula", "https://aula.dk", parents: true, staff: true));

		using var board = Client(_tenantId, boardSubject, "user");
		var visible = await GetVisibleAsync(board);

		await Assert.That(visible.Select(l => l.Id)).IsEquivalentTo([boardLink]);
	}

	[Test]
	public async Task NonAdmin_CannotManageLinks()
	{
		var linkId = await CreateLinkAsync(Link("Aula", "https://aula.dk", staff: true));
		var staffSubject = $"links-staff-{Guid.NewGuid():N}";
		await TestDataBuilder.CreateStaffAsync(factory.Services, _tenantId, keycloakSubject: staffSubject);
		using var staff = Client(_tenantId, staffSubject, "user");

		await Assert.That((await staff.GetAsync("/api/v1/external-links/all")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await staff.PostAsJsonAsync("/api/v1/external-links", Link("X", "https://x.dk", staff: true))).StatusCode)
			.IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await staff.PutAsJsonAsync($"/api/v1/external-links/{linkId}", Link("X", "https://x.dk", staff: true))).StatusCode)
			.IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await staff.DeleteAsync($"/api/v1/external-links/{linkId}")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await staff.PutAsJsonAsync("/api/v1/external-links/order", new ReorderExternalLinksRequest([linkId]))).StatusCode)
			.IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	[Arguments("javascript:alert(1)")]
	[Arguments("mailto:kontor@skole.dk")]
	[Arguments("tel:12345678")]
	[Arguments("ftp://filer.skole.dk")]
	public async Task Create_RejectsNonHttpUrls(string url)
	{
		var response = await _admin.PostAsJsonAsync("/api/v1/external-links", Link("Ond", url, parents: true));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
		await Assert.That(problem!.Errors.Keys).Contains(nameof(UpsertExternalLinkRequest.Url));
		await Assert.That(await GetAllAsync()).IsEmpty();
	}

	[Test]
	public async Task Create_AddsHttpsToUrlWithoutScheme()
	{
		await CreateLinkAsync(Link("Aula", "  aula.dk ", parents: true));

		await Assert.That((await GetAllAsync()).Single().Url).IsEqualTo("https://aula.dk");
	}

	[Test]
	public async Task Create_WithoutAudience_Returns400()
	{
		var response = await _admin.PostAsJsonAsync("/api/v1/external-links", Link("Aula", "https://aula.dk"));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task ClassesAreDropped_WhenParentsAudienceIsOff()
	{
		var klass = await CreateClassAsync("1.a");

		await CreateLinkAsync(Link("Personaleportal", "https://intra.skole.dk", staff: true) with { ClassIds = [klass] });

		var link = (await GetAllAsync()).Single();
		await Assert.That(link.RestrictToClasses).IsFalse();
		await Assert.That(link.Classes).IsEmpty();
	}

	[Test]
	public async Task DeletingTheOnlyRestrictedClass_HidesTheLinkFromParents()
	{
		var restrictedClass = await CreateClassAsync("9.a");
		var childClass = await CreateClassAsync("2.b");
		var parentSubject = await CreateParentAsync(childClass);
		await CreateLinkAsync(Link("9.a på Facebook", "https://facebook.com/groups/9a", parents: true) with { ClassIds = [restrictedClass] });

		await Assert.That((await _admin.DeleteAsync($"/api/v1/classes/{restrictedClass}")).StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		using var parent = Client(_tenantId, parentSubject, "parent");
		await Assert.That(await GetVisibleAsync(parent)).IsEmpty();
		var link = (await GetAllAsync()).Single();
		await Assert.That(link.RestrictToClasses).IsTrue();
		await Assert.That(link.Classes).IsEmpty();
	}

	[Test]
	public async Task OtherTenantsLinks_AreInvisibleAndNotEditable()
	{
		var linkId = await CreateLinkAsync(Link("Aula", "https://aula.dk", parents: true, staff: true, board: true));

		var otherTenant = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(factory.Services, otherTenant, "Anden Skole");
		var otherSubject = $"links-other-{Guid.NewGuid():N}";
		await TestDataBuilder.CreateStaffAsync(factory.Services, otherTenant, isAdmin: true, keycloakSubject: otherSubject);
		using var otherAdmin = Client(otherTenant, otherSubject, "admin");

		await Assert.That(await GetVisibleAsync(otherAdmin)).IsEmpty();
		await Assert.That(await otherAdmin.GetFromJsonAsync<List<ExternalLinkAdminDto>>("/api/v1/external-links/all")).IsEmpty();
		await Assert.That((await otherAdmin.PutAsJsonAsync($"/api/v1/external-links/{linkId}", Link("Hacket", "https://evil.dk", staff: true))).StatusCode)
			.IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That((await otherAdmin.DeleteAsync($"/api/v1/external-links/{linkId}")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That((await GetAllAsync()).Single().Title).IsEqualTo("Aula");
	}

	// ── Helpers ──────────────────────────────────────────────────────────────────

	private HttpClient Client(Guid tenantId, string subject, string realmRoles)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", realmRoles);
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	private static UpsertExternalLinkRequest Link(
		string title, string url, bool parents = false, bool staff = false, bool board = false) =>
		new(title, url, null, parents, staff, board, null);

	private async Task<Guid> CreateLinkAsync(UpsertExternalLinkRequest request)
	{
		var response = await _admin.PostAsJsonAsync("/api/v1/external-links", request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var body = await response.Content.ReadFromJsonAsync<CreatedId>();
		return body!.Id;
	}

	private sealed record CreatedId(Guid Id);

	private async Task<List<ExternalLinkAdminDto>> GetAllAsync() =>
		(await _admin.GetFromJsonAsync<List<ExternalLinkAdminDto>>("/api/v1/external-links/all"))!;

	private static async Task<List<ExternalLinkDto>> GetVisibleAsync(HttpClient client)
	{
		var response = await client.GetAsync("/api/v1/external-links");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (await response.Content.ReadFromJsonAsync<List<ExternalLinkDto>>())!;
	}

	private async Task<Guid> CreateClassAsync(string name)
	{
		var response = await _admin.PostAsJsonAsync("/api/v1/classes", new ClassesController.UpsertClassRequest(name, null, null));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		return (await response.Content.ReadFromJsonAsync<ClassesController.ClassDto>())!.Id;
	}

	/// <summary>A parent login with one child in <paramref name="classId"/>. Returns the Keycloak subject.</summary>
	private async Task<string> CreateParentAsync(Guid classId)
	{
		var subject = $"links-parent-{Guid.NewGuid():N}";
		using var scope = factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var student = new Student { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Elev Testsen", ClassId = classId };
		var parent = new Parent
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = "Forælder Testsen",
			Email = $"{subject}@test.dk",
			KeycloakSubject = subject,
			Students = [student],
		};
		db.Parents.Add(parent);
		await db.SaveChangesAsync();
		return subject;
	}
}
