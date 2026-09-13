using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Integration tests for ClassChatController — the per-klasse group chat.
/// Covers the security invariants: membership is derived (parents via enrolled students,
/// staff via SchemaSlot roster, admins see all), tenant isolation holds, only the sender
/// or a moderator can delete, and notification fan-out excludes the sender without
/// duplicating recipients.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class ClassChatTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private record ThreadDto(Guid ClassId, string ClassName, string? LastMessageBody, int MessageCount);

	private record AttachmentDto(Guid Id, string FileName, string ContentType, long SizeBytes, string Url);

	private record MessageDto(
		Guid Id,
		SenderType SenderType,
		Guid SenderId,
		string SenderName,
		string? SenderAvatarUrl,
		string Body,
		DateTimeOffset SentAt,
		bool CanDelete,
		List<AttachmentDto> Attachments);

	private record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize);

	private readonly ApiFactory _factory = factory;
	private readonly Guid _tenantId = Guid.NewGuid();

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, _tenantId);
	}

	// ── Private helpers ───────────────────────────────────────────────────────

	private HttpClient CreateParentClient(string subject, Guid? tenantId = null)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", (tenantId ?? _tenantId).ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "parent");
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	private HttpClient CreateStaffClient(string subject, bool isAdmin = false, Guid? tenantId = null)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", (tenantId ?? _tenantId).ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", isAdmin ? "admin" : "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	private async Task<Student> CreateStudentAsync(Guid classId, string name, Guid? tenantId = null)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var student = new Student
		{
			Id = Guid.NewGuid(),
			TenantId = tenantId ?? _tenantId,
			Name = name,
			ClassId = classId,
		};
		db.Students.Add(student);
		await db.SaveChangesAsync();
		return student;
	}

	/// <summary>Creates a parent linked to every supplied student.</summary>
	private async Task<Parent> CreateParentAsync(
		string keycloakSubject, string name, Guid? tenantId = null, params Guid[] studentIds)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		var parent = new Parent
		{
			Id = Guid.NewGuid(),
			TenantId = tenantId ?? _tenantId,
			Name = name,
			Email = $"{keycloakSubject}@test.dk",
			KeycloakSubject = keycloakSubject,
		};

		foreach (var studentId in studentIds)
		{
			var studentRef = await db.Students.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == studentId);
			if (studentRef is not null)
			{
				parent.Students.Add(studentRef);
			}
		}

		db.Parents.Add(parent);
		await db.SaveChangesAsync();
		return parent;
	}

	/// <summary>Puts a staff member on a klasse's roster by giving them a SchemaSlot there.</summary>
	private async Task<Staff> CreateRosterStaffAsync(
		Guid schemaId, string subject, string name, bool isAdmin = false, Guid? tenantId = null)
	{
		var tid = tenantId ?? _tenantId;
		var staff = await TestDataBuilder.CreateStaffAsync(
			_factory.Services, tid, name, StaffRole.Teacher, isAdmin, subject);
		var course = await TestDataBuilder.CreateCourseAsync(_factory.Services, tid, $"Fag {Guid.NewGuid():N}");
		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(
			_factory.Services, tid, new TimeOnly(8, 0), new TimeOnly(8, 45));

		await TestDataBuilder.CreateSchemaSlotAsync(
			_factory.Services, tid, schemaId, timeSlot.Id, course.Id, staff.Id);

		return staff;
	}

	private static async Task<Guid> PostMessageAsync(HttpClient client, Guid classId, string body)
	{
		var response = await client.PostAsJsonAsync($"/api/v1/class-chats/{classId}/messages", new { body });
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var dto = await response.Content.ReadFromJsonAsync<MessageDto>(JsonOpts);
		return dto!.Id;
	}

	// ── GET /api/v1/class-chats — thread visibility ───────────────────────────

	[Test]
	public async Task GetThreads_Parent_SeesOnlyOwnChildrensClasses()
	{
		const string parentSubject = "cc-threads-parent";
		var (mine, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "3.a");
		var (theirs, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "3.b");

		var student = await CreateStudentAsync(mine.Id, "Emil Elev");
		await CreateStudentAsync(theirs.Id, "Anden Elev");
		await CreateParentAsync(parentSubject, "Karen Forælder", null, student.Id);

		using var client = CreateParentClient(parentSubject);
		var response = await client.GetAsync("/api/v1/class-chats");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var threads = await response.Content.ReadFromJsonAsync<List<ThreadDto>>(JsonOpts);

		await Assert.That(threads!.Select(t => t.ClassId)).Contains(mine.Id);
		await Assert.That(threads!.Select(t => t.ClassId)).DoesNotContain(theirs.Id);
	}

	[Test]
	public async Task GetThreads_ParentWithTwoChildren_SeesTwoSeparateThreads()
	{
		const string parentSubject = "cc-threads-two-kids";
		var (first, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "4.a");
		var (second, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "6.c");

		var older = await CreateStudentAsync(first.Id, "Ida Elev");
		var younger = await CreateStudentAsync(second.Id, "Nikolaj Elev");
		await CreateParentAsync(parentSubject, "Birgitte Forælder", null, older.Id, younger.Id);

		using var client = CreateParentClient(parentSubject);
		var threads = await client.GetFromJsonAsync<List<ThreadDto>>("/api/v1/class-chats", JsonOpts);

		var visible = threads!.Where(t => t.ClassId == first.Id || t.ClassId == second.Id).ToList();
		await Assert.That(visible.Count).IsEqualTo(2);
	}

	[Test]
	public async Task GetThreads_Staff_SeesOnlyRosterClasses()
	{
		const string staffSubject = "cc-threads-staff";
		var (taught, taughtSchema) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "5.a");
		var (untaught, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "5.b");

		await CreateRosterStaffAsync(taughtSchema.Id, staffSubject, "Thomas Lærer");

		using var client = CreateStaffClient(staffSubject);
		var threads = await client.GetFromJsonAsync<List<ThreadDto>>("/api/v1/class-chats", JsonOpts);

		await Assert.That(threads!.Select(t => t.ClassId)).Contains(taught.Id);
		await Assert.That(threads!.Select(t => t.ClassId)).DoesNotContain(untaught.Id);
	}

	[Test]
	public async Task GetThreads_Admin_SeesAllClasses()
	{
		const string adminSubject = "cc-threads-admin";
		var (first, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "7.a");
		var (second, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "7.b");

		await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId, "Hanne Sekretær", StaffRole.Teacher, isAdmin: true, adminSubject);

		using var client = CreateStaffClient(adminSubject, isAdmin: true);
		var threads = await client.GetFromJsonAsync<List<ThreadDto>>("/api/v1/class-chats", JsonOpts);

		await Assert.That(threads!.Select(t => t.ClassId)).Contains(first.Id);
		await Assert.That(threads!.Select(t => t.ClassId)).Contains(second.Id);
	}

	/// <summary>ShareContactInfo gates the Kontakt directory, never chat visibility.</summary>
	[Test]
	public async Task GetThreads_ParentWithoutShareContactInfo_StillSeesThread()
	{
		const string parentSubject = "cc-threads-no-consent";
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "8.a");
		var student = await CreateStudentAsync(klass.Id, "Freja Elev");
		var parent = await CreateParentAsync(parentSubject, "Lone Forælder", null, student.Id);

		using (var scope = _factory.Services.CreateScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			var row = await db.Parents.IgnoreQueryFilters().FirstAsync(p => p.Id == parent.Id);
			row.ShareContactInfo = false;
			await db.SaveChangesAsync();
		}

		using var client = CreateParentClient(parentSubject);
		var threads = await client.GetFromJsonAsync<List<ThreadDto>>("/api/v1/class-chats", JsonOpts);

		await Assert.That(threads!.Select(t => t.ClassId)).Contains(klass.Id);
	}

	// ── Messages — authorization ──────────────────────────────────────────────

	[Test]
	public async Task GetMessages_NonMemberParent_Returns403()
	{
		const string memberSubject = "cc-msg-member";
		const string outsiderSubject = "cc-msg-outsider";

		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "9.a");
		var (otherKlass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "9.b");

		var student = await CreateStudentAsync(klass.Id, "Oscar Elev");
		var otherStudent = await CreateStudentAsync(otherKlass.Id, "Alma Elev");
		await CreateParentAsync(memberSubject, "Peter Forælder", null, student.Id);
		await CreateParentAsync(outsiderSubject, "Ukendt Forælder", null, otherStudent.Id);

		using var client = CreateParentClient(outsiderSubject);
		var response = await client.GetAsync($"/api/v1/class-chats/{klass.Id}/messages");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task PostMessage_NonMemberParent_Returns403()
	{
		const string outsiderSubject = "cc-post-outsider";
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "10.a");
		var (otherKlass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "10.b");

		var otherStudent = await CreateStudentAsync(otherKlass.Id, "Villads Elev");
		await CreateParentAsync(outsiderSubject, "Fremmed Forælder", null, otherStudent.Id);

		using var client = CreateParentClient(outsiderSubject);
		var response = await client.PostAsJsonAsync(
			$"/api/v1/class-chats/{klass.Id}/messages", new { body = "Må jeg være med?" });

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task PostMessage_MemberParent_ThenAppearsInThread()
	{
		const string parentSubject = "cc-post-member";
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "1.c");
		var student = await CreateStudentAsync(klass.Id, "Selma Elev");
		await CreateParentAsync(parentSubject, "Jonas Forælder", null, student.Id);

		using var client = CreateParentClient(parentSubject);
		await PostMessageAsync(client, klass.Id, "Er der nogen der har glemt en trøje?");

		var page = await client.GetFromJsonAsync<PagedResult<MessageDto>>(
			$"/api/v1/class-chats/{klass.Id}/messages", JsonOpts);

		await Assert.That(page!.Items.Count).IsEqualTo(1);
		await Assert.That(page.Items[0].Body).IsEqualTo("Er der nogen der har glemt en trøje?");
		await Assert.That(page.Items[0].SenderName).IsEqualTo("Jonas Forælder");
		await Assert.That(page.Items[0].SenderType).IsEqualTo(SenderType.Parent);
	}

	[Test]
	public async Task PostMessage_EmptyBody_Returns400()
	{
		const string parentSubject = "cc-post-empty";
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "1.d");
		var student = await CreateStudentAsync(klass.Id, "Tom Elev");
		await CreateParentAsync(parentSubject, "Tomme Forælder", null, student.Id);

		using var client = CreateParentClient(parentSubject);
		var response = await client.PostAsJsonAsync(
			$"/api/v1/class-chats/{klass.Id}/messages", new { body = "   " });

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	// ── Deletion / moderation ─────────────────────────────────────────────────

	[Test]
	public async Task DeleteMessage_OwnMessage_Succeeds()
	{
		const string parentSubject = "cc-del-own";
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "2.c");
		var student = await CreateStudentAsync(klass.Id, "Malthe Elev");
		await CreateParentAsync(parentSubject, "Ejer Forælder", null, student.Id);

		using var client = CreateParentClient(parentSubject);
		var messageId = await PostMessageAsync(client, klass.Id, "Skrevet ved en fejl");

		var response = await client.DeleteAsync($"/api/v1/class-chats/{klass.Id}/messages/{messageId}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var page = await client.GetFromJsonAsync<PagedResult<MessageDto>>(
			$"/api/v1/class-chats/{klass.Id}/messages", JsonOpts);
		await Assert.That(page!.Items).IsEmpty();
	}

	[Test]
	public async Task DeleteMessage_OtherParentsMessage_Returns403()
	{
		const string authorSubject = "cc-del-author";
		const string peerSubject = "cc-del-peer";

		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "2.d");
		var first = await CreateStudentAsync(klass.Id, "Elev En");
		var second = await CreateStudentAsync(klass.Id, "Elev To");
		await CreateParentAsync(authorSubject, "Forfatter Forælder", null, first.Id);
		await CreateParentAsync(peerSubject, "Med Forælder", null, second.Id);

		using var authorClient = CreateParentClient(authorSubject);
		var messageId = await PostMessageAsync(authorClient, klass.Id, "Min besked");

		// Same klasse, so the peer can read the thread — but not moderate it.
		using var peerClient = CreateParentClient(peerSubject);
		var response = await peerClient.DeleteAsync($"/api/v1/class-chats/{klass.Id}/messages/{messageId}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task DeleteMessage_RosterStaff_CanModerateParentMessage()
	{
		const string parentSubject = "cc-mod-parent";
		const string staffSubject = "cc-mod-staff";

		var (klass, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "3.c");
		var student = await CreateStudentAsync(klass.Id, "Moderation Elev");
		await CreateParentAsync(parentSubject, "Skrivende Forælder", null, student.Id);
		await CreateRosterStaffAsync(schema.Id, staffSubject, "Vagt Lærer");

		using var parentClient = CreateParentClient(parentSubject);
		var messageId = await PostMessageAsync(parentClient, klass.Id, "Upassende besked");

		using var staffClient = CreateStaffClient(staffSubject);
		var response = await staffClient.DeleteAsync($"/api/v1/class-chats/{klass.Id}/messages/{messageId}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task DeleteMessage_Admin_CanModerateAnyMessage()
	{
		const string parentSubject = "cc-admin-mod-parent";
		const string adminSubject = "cc-admin-mod-admin";

		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "3.d");
		var student = await CreateStudentAsync(klass.Id, "Admin Elev");
		await CreateParentAsync(parentSubject, "Almindelig Forælder", null, student.Id);
		await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId, "Hanne Admin", StaffRole.Teacher, isAdmin: true, adminSubject);

		using var parentClient = CreateParentClient(parentSubject);
		var messageId = await PostMessageAsync(parentClient, klass.Id, "Noget der skal fjernes");

		using var adminClient = CreateStaffClient(adminSubject, isAdmin: true);
		var response = await adminClient.DeleteAsync($"/api/v1/class-chats/{klass.Id}/messages/{messageId}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	// ── Tenant isolation ──────────────────────────────────────────────────────

	[Test]
	public async Task GetMessages_OtherTenant_CannotSeeThread()
	{
		var otherTenantId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, otherTenantId);

		const string insiderSubject = "cc-tenant-insider";
		const string outsiderSubject = "cc-tenant-outsider";

		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "4.d");
		var student = await CreateStudentAsync(klass.Id, "Intern Elev");
		await CreateParentAsync(insiderSubject, "Intern Forælder", null, student.Id);

		using var insiderClient = CreateParentClient(insiderSubject);
		await PostMessageAsync(insiderClient, klass.Id, "Intern besked");

		// An admin of a different tenant must not reach this klasse at all.
		await TestDataBuilder.CreateStaffAsync(
			_factory.Services, otherTenantId, "Fremmed Admin", StaffRole.Teacher, isAdmin: true, outsiderSubject);

		using var outsiderClient = CreateStaffClient(outsiderSubject, isAdmin: true, tenantId: otherTenantId);
		var response = await outsiderClient.GetAsync($"/api/v1/class-chats/{klass.Id}/messages");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	// ── Notification fan-out ──────────────────────────────────────────────────

	[Test]
	public async Task PostMessage_NotifiesOtherMembers_ButNotSender()
	{
		const string senderSubject = "cc-notify-sender";
		const string peerSubject = "cc-notify-peer";
		const string staffSubject = "cc-notify-staff";

		var (klass, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "5.d");
		var first = await CreateStudentAsync(klass.Id, "Notify Elev En");
		var second = await CreateStudentAsync(klass.Id, "Notify Elev To");

		var sender = await CreateParentAsync(senderSubject, "Afsender Forælder", null, first.Id);
		var peer = await CreateParentAsync(peerSubject, "Modtager Forælder", null, second.Id);
		var staff = await CreateRosterStaffAsync(schema.Id, staffSubject, "Notify Lærer");

		using var client = CreateParentClient(senderSubject);
		await PostMessageAsync(client, klass.Id, "Husk fælles arrangement på fredag");

		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var notifications = await db.Notifications
			.IgnoreQueryFilters()
			.Where(n => n.TenantId == _tenantId && n.Type == NotificationType.ClassChatMessage && n.ReferenceId == klass.Id)
			.ToListAsync();

		await Assert.That(notifications.Any(n => n.RecipientId == peer.Id)).IsTrue();
		await Assert.That(notifications.Any(n => n.RecipientId == staff.Id)).IsTrue();
		await Assert.That(notifications.Any(n => n.RecipientId == sender.Id)).IsFalse();
	}

	/// <summary>A parent with two children in the same klasse must be notified once, not twice.</summary>
	[Test]
	public async Task PostMessage_ParentWithTwoChildrenInClass_NotifiedOnce()
	{
		const string senderSubject = "cc-dedupe-sender";
		const string siblingParentSubject = "cc-dedupe-parent";

		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "6.d");
		var senderChild = await CreateStudentAsync(klass.Id, "Afsenders Barn");
		var twinA = await CreateStudentAsync(klass.Id, "Tvilling A");
		var twinB = await CreateStudentAsync(klass.Id, "Tvilling B");

		await CreateParentAsync(senderSubject, "Dedupe Afsender", null, senderChild.Id);
		var siblingParent = await CreateParentAsync(
			siblingParentSubject, "Tvilling Forælder", null, twinA.Id, twinB.Id);

		using var client = CreateParentClient(senderSubject);
		await PostMessageAsync(client, klass.Id, "Besked til tvillingeforældre");

		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var count = await db.Notifications
			.IgnoreQueryFilters()
			.CountAsync(n => n.TenantId == _tenantId
						  && n.Type == NotificationType.ClassChatMessage
						  && n.ReferenceId == klass.Id
						  && n.RecipientId == siblingParent.Id);

		await Assert.That(count).IsEqualTo(1);
	}
}
