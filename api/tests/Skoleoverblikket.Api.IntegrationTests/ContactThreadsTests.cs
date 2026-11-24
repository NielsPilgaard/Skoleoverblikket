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
/// Integration tests for ContactThreadsController.
/// Covers the core security invariant: a parent may only create threads and
/// read messages for students they own; attempting to access another parent's
/// student yields 403.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class ContactThreadsTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	// Local DTO matching the anonymous object returned by POST /api/v1/contact-threads
	private record CreateThreadResponse(Guid ThreadId);

	private readonly ApiFactory _factory = factory;
	private readonly Guid _tenantId = Guid.NewGuid();

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, _tenantId);
	}

	// ── Private helpers ───────────────────────────────────────────────────────

	private HttpClient CreateParentClient(string subject)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	private HttpClient CreateStaffClient(string subject, bool isAdmin = false)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", isAdmin ? "admin" : "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	private async Task<Student> CreateStudentAsync(Guid classId, string name = "Mikkel Testsen")
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var student = new Student
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = name,
			ClassId = classId,
		};
		db.Students.Add(student);
		await db.SaveChangesAsync();
		return student;
	}

	private async Task<Parent> CreateParentAsync(string keycloakSubject, Guid studentId, string name = "Dorte Testsen")
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		var parent = new Parent
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = name,
			Email = $"{keycloakSubject}@test.dk",
			KeycloakSubject = keycloakSubject,
		};

		var studentRef = await db.Students.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == studentId);
		if (studentRef is not null)
		{
			parent.Students.Add(studentRef);
		}

		db.Parents.Add(parent);
		await db.SaveChangesAsync();
		return parent;
	}

	// ── POST /api/v1/contact-threads ─────────────────────────────────────────

	[Test]
	public async Task CreateThread_Parent_OwnStudent_Returns201()
	{
		const string parentSubject = "ct-create-own-parent";
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "1.a");
		var student = await CreateStudentAsync(klass.Id, "Lasse Elev");
		await CreateParentAsync(parentSubject, student.Id, "Karen Forælder");

		using var client = CreateParentClient(parentSubject);
		var response = await client.PostAsJsonAsync("/api/v1/contact-threads", new
		{
			studentId = student.Id,
			body = "Hej, jeg vil gerne tale om Lasse.",
		});

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var body = await response.Content.ReadFromJsonAsync<CreateThreadResponse>(JsonOpts);
		await Assert.That(body).IsNotNull();
		await Assert.That(body!.ThreadId).IsNotEqualTo(Guid.Empty);
	}

	[Test]
	public async Task CreateThread_Parent_OtherParentsStudent_Returns403()
	{
		const string ownerSubject = "ct-owner-parent";
		const string intruderSubject = "ct-intruder-parent";

		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "2.a");
		var student = await CreateStudentAsync(klass.Id, "Sofie Elev");

		// Owner is linked to the student; intruder has no link
		await CreateParentAsync(ownerSubject, student.Id, "Mette Forælder");

		// Create intruder parent record but link them to a different student
		var (otherKlass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "2.b");
		var otherStudent = await CreateStudentAsync(otherKlass.Id, "Jonas Elev");
		await CreateParentAsync(intruderSubject, otherStudent.Id, "Per Forælder");

		using var client = CreateParentClient(intruderSubject);
		var response = await client.PostAsJsonAsync("/api/v1/contact-threads", new
		{
			studentId = student.Id,  // student owned by ownerSubject, not intruderSubject
			body = "Jeg snager lidt.",
		});

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task CreateThread_Staff_Returns201()
	{
		const string staffSubject = "ct-create-staff";
		await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId,
			name: "Birgit Lærer", isAdmin: false, keycloakSubject: staffSubject);

		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "3.a");
		var student = await CreateStudentAsync(klass.Id, "Emma Elev");

		// Create a parent so notifications don't fail on missing parent lookup
		await CreateParentAsync("ct-staff-test-parent", student.Id, "Hanne Forælder");

		using var client = CreateStaffClient(staffSubject);
		var response = await client.PostAsJsonAsync("/api/v1/contact-threads", new
		{
			studentId = student.Id,
			body = "Hej, jeg vil gerne tale om Emma.",
		});

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var body = await response.Content.ReadFromJsonAsync<CreateThreadResponse>(JsonOpts);
		await Assert.That(body).IsNotNull();
		await Assert.That(body!.ThreadId).IsNotEqualTo(Guid.Empty);
	}

	[Test]
	public async Task CreateThread_Parent_NoParentRecord_Returns403()
	{
		// Authenticated with "parent" role, but no Parent row exists in the DB
		const string ghostSubject = "ct-ghost-parent";

		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "4.a");
		var student = await CreateStudentAsync(klass.Id, "Noah Elev");

		using var client = CreateParentClient(ghostSubject);
		var response = await client.PostAsJsonAsync("/api/v1/contact-threads", new
		{
			studentId = student.Id,
			body = "Jeg eksisterer ikke i databasen.",
		});

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	// ── GET /api/v1/contact-threads/{threadId}/messages ──────────────────────

	[Test]
	public async Task GetMessages_Parent_OwnStudent_Returns200()
	{
		const string parentSubject = "ct-getmsg-own-parent";
		const string staffSubject = "ct-getmsg-staff";
		await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId,
			name: "Lars Lærer", isAdmin: true, keycloakSubject: staffSubject);

		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "5.a");
		var student = await CreateStudentAsync(klass.Id, "Ida Elev");
		await CreateParentAsync(parentSubject, student.Id, "Susanne Forælder");

		// Create thread via staff so we get a threadId back
		using var staffClient = CreateStaffClient(staffSubject, isAdmin: true);
		var createResponse = await staffClient.PostAsJsonAsync("/api/v1/contact-threads", new
		{
			studentId = student.Id,
			body = "Hej fra skolen.",
		});
		await Assert.That(createResponse.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var created = await createResponse.Content.ReadFromJsonAsync<CreateThreadResponse>(JsonOpts);
		var threadId = created!.ThreadId;

		using var parentClient = CreateParentClient(parentSubject);
		var response = await parentClient.GetAsync($"/api/v1/contact-threads/{threadId}/messages");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var paged = await response.Content.ReadFromJsonAsync<ContactThreadsController.PagedResult<ContactThreadsController.ContactMessageDto>>(JsonOpts);
		await Assert.That(paged).IsNotNull();
		await Assert.That(paged!.Total).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async Task GetMessages_Parent_OtherParentsThread_Returns403()
	{
		const string ownerSubject = "ct-getmsg-owner-parent";
		const string intruderSubject = "ct-getmsg-intruder-parent";
		const string staffSubject = "ct-getmsg-403-staff";
		await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId,
			name: "Mads Lærer", isAdmin: true, keycloakSubject: staffSubject);

		var (ownerKlass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "6.a");
		var (intruderKlass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "6.b");

		var ownerStudent = await CreateStudentAsync(ownerKlass.Id, "Luna Elev");
		var intruderStudent = await CreateStudentAsync(intruderKlass.Id, "Victor Elev");

		await CreateParentAsync(ownerSubject, ownerStudent.Id, "Anne Forælder");
		await CreateParentAsync(intruderSubject, intruderStudent.Id, "Jens Forælder");

		// Staff creates a thread for the owner's student
		using var staffClient = CreateStaffClient(staffSubject, isAdmin: true);
		var createResponse = await staffClient.PostAsJsonAsync("/api/v1/contact-threads", new
		{
			studentId = ownerStudent.Id,
			body = "Besked til Luna's forælder.",
		});
		await Assert.That(createResponse.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var created = await createResponse.Content.ReadFromJsonAsync<CreateThreadResponse>(JsonOpts);
		var threadId = created!.ThreadId;

		// Intruder parent tries to read messages from owner's thread
		using var intruderClient = CreateParentClient(intruderSubject);
		var response = await intruderClient.GetAsync($"/api/v1/contact-threads/{threadId}/messages");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	// ── GET /api/v1/contact-threads ──────────────────────────────────────────

	[Test]
	public async Task GetThreads_ShowsLatestMessageAndUnreadFromOtherSide_OnlyToOwnParent()
	{
		const string parentSubject = "ct-list-parent";
		const string otherParentSubject = "ct-list-other-parent";
		const string staffSubject = "ct-list-staff";
		await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId,
			name: "Pia Lærer", isAdmin: true, keycloakSubject: staffSubject);

		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "8.a");
		var student = await CreateStudentAsync(klass.Id, "Oscar Elev");
		var otherStudent = await CreateStudentAsync(klass.Id, "Alma Elev");
		await CreateParentAsync(parentSubject, student.Id, "Mette Forælder");
		await CreateParentAsync(otherParentSubject, otherStudent.Id, "Kim Forælder");

		using var parentClient = CreateParentClient(parentSubject);
		var createResponse = await parentClient.PostAsJsonAsync("/api/v1/contact-threads", new
		{
			studentId = student.Id,
			body = "Oscar er syg i dag.",
		});
		await Assert.That(createResponse.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var threadId = (await createResponse.Content.ReadFromJsonAsync<CreateThreadResponse>(JsonOpts))!.ThreadId;

		var secondParentMessage = await parentClient.PostAsJsonAsync(
			$"/api/v1/contact-threads/{threadId}/messages", new { body = "Han kommer i morgen." });
		await Assert.That(secondParentMessage.StatusCode).IsEqualTo(HttpStatusCode.Created);

		using var staffClient = CreateStaffClient(staffSubject, isAdmin: true);
		var staffReply = await staffClient.PostAsJsonAsync(
			$"/api/v1/contact-threads/{threadId}/messages", new { body = "God bedring til Oscar." });
		await Assert.That(staffReply.StatusCode).IsEqualTo(HttpStatusCode.Created);

		// The parent sees their thread, with the school's reply as latest and unread.
		var parentThreads = await parentClient.GetFromJsonAsync<List<ContactThreadsController.ContactThreadDto>>(
			"/api/v1/contact-threads", JsonOpts);
		await Assert.That(parentThreads!.Count).IsEqualTo(1);
		var parentView = parentThreads[0];
		await Assert.That(parentView.Id).IsEqualTo(threadId);
		await Assert.That(parentView.StudentName).IsEqualTo("Oscar Elev");
		await Assert.That(parentView.LastMessageBody).IsEqualTo("God bedring til Oscar.");
		await Assert.That(parentView.LastMessageSenderType).IsEqualTo(SenderType.Staff);
		await Assert.That(parentView.UnreadCount).IsEqualTo(1);

		// Staff see the same latest message, and the parent's two messages as unread.
		var staffThreads = await staffClient.GetFromJsonAsync<List<ContactThreadsController.ContactThreadDto>>(
			"/api/v1/contact-threads", JsonOpts);
		var staffView = staffThreads!.Single(t => t.Id == threadId);
		await Assert.That(staffView.LastMessageBody).IsEqualTo("God bedring til Oscar.");
		await Assert.That(staffView.UnreadCount).IsEqualTo(2);

		var markRead = await staffClient.PostAsync($"/api/v1/contact-threads/{threadId}/read", null);
		await Assert.That(markRead.IsSuccessStatusCode).IsTrue();
		staffThreads = await staffClient.GetFromJsonAsync<List<ContactThreadsController.ContactThreadDto>>(
			"/api/v1/contact-threads", JsonOpts);
		await Assert.That(staffThreads!.Single(t => t.Id == threadId).UnreadCount).IsEqualTo(0);

		// A parent of another child in the same klasse does not see the thread.
		using var otherParentClient = CreateParentClient(otherParentSubject);
		var otherParentThreads = await otherParentClient.GetFromJsonAsync<List<ContactThreadsController.ContactThreadDto>>(
			"/api/v1/contact-threads", JsonOpts);
		await Assert.That(otherParentThreads!).IsEmpty();
	}

	// ── Notification failure resilience ─────────────────────────────────────

	[Test]
	public async Task AddMessage_NotificationDeliveryFails_RequestStillSucceedsWithOneMessage()
	{
		const string parentSubject = "ct-notifyfail-parent";
		const string staffSubject = "ct-notifyfail-staff";
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "7.a");
		var student = await CreateStudentAsync(klass.Id, "Freja Elev");
		await CreateParentAsync(parentSubject, student.Id, "Thomas Forælder");
		await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId,
			name: "Nanna Lærer", isAdmin: true, keycloakSubject: staffSubject);

		using var client = CreateParentClient(parentSubject);
		var createResponse = await client.PostAsJsonAsync("/api/v1/contact-threads", new
		{
			studentId = student.Id,
			body = "Første besked.",
		});
		await Assert.That(createResponse.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var created = await createResponse.Content.ReadFromJsonAsync<CreateThreadResponse>(JsonOpts);
		var threadId = created!.ThreadId;

		// Second message sent by staff so the recipient (the parent, via thread.StudentId's
		// Parents) is guaranteed to exist without seeding a schema slot or ClassPermission —
		// that's what actually drives INotificationService.CreateAsync being invoked below.
		using var staffClient = CreateStaffClient(staffSubject, isAdmin: true);

		NoOpNotificationService.ShouldThrow.Value = true;
		try
		{
			var response = await staffClient.PostAsJsonAsync($"/api/v1/contact-threads/{threadId}/messages", new
			{
				body = "Besked hvor notifikation fejler.",
			});

			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		}
		finally
		{
			NoOpNotificationService.ShouldThrow.Value = false;
		}

		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var messageCount = await db.ContactMessages.IgnoreQueryFilters()
			.CountAsync(m => m.ThreadId == threadId);
		await Assert.That(messageCount).IsEqualTo(2);
	}
}
