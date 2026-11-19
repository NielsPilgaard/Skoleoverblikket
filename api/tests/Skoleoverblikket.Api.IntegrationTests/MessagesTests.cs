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
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Integration tests for MessagesController.
/// Covers:
///   - POST /api/v1/messages: staff→parent (201), parent→parent with consent (201),
///     parent→parent without consent (403), no sender record (403).
///   - GET /api/v1/messages/inbox: staff inbox with data (200), parent empty inbox (200).
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class MessagesTests(ApiFactory factory)
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

	// ── Private helpers ──────────────────────────────────────────────────────────

	private HttpClient CreateStaffClient(string subject, bool isAdmin = false)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", isAdmin ? "admin" : "user");
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

	private async Task<Parent> CreateParentAsync(
		string keycloakSubject,
		string name = "Dorte Testsen",
		bool shareContactInfo = false)
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
			ShareContactInfo = shareContactInfo,
		};
		db.Parents.Add(parent);
		await db.SaveChangesAsync();
		return parent;
	}

	private async Task<Message> CreateMessageAsync(
		Guid senderId,
		RecipientType senderType,
		Guid recipientId,
		RecipientType recipientType,
		string subject = "Test emne",
		string body = "Test besked")
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var message = new Message
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			SenderId = senderId,
			SenderType = senderType,
			RecipientId = recipientId,
			RecipientType = recipientType,
			Subject = subject,
			Body = body,
			SentAt = DateTimeOffset.UtcNow,
		};
		db.Messages.Add(message);
		await db.SaveChangesAsync();
		return message;
	}

	// ── POST /api/v1/messages ─────────────────────────────────────────────────────

	[Test]
	public async Task SendMessage_StaffToParent_Returns201()
	{
		const string senderSubject = "msg-staff-sender";
		await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId,
			name: "Lars Lærer", keycloakSubject: senderSubject);
		var recipient = await CreateParentAsync("msg-staff-to-parent-recipient", name: "Britta Forælder");

		var request = new MessagesController.SendMessageRequest(
			recipient.Id,
			RecipientType.Parent,
			"Hej fra lærer",
			"Dette er en besked fra en lærer til en forælder.");

		using var client = CreateStaffClient(senderSubject);
		var response = await client.PostAsJsonAsync("/api/v1/messages", request, JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
	}

	[Test]
	public async Task SendMessage_ParentToParent_WithConsent_Returns201()
	{
		const string senderSubject = "msg-parent-sender-consent";
		await CreateParentAsync(senderSubject, name: "Anne Afsender", shareContactInfo: true);
		var recipient = await CreateParentAsync("msg-parent-recipient-consent", name: "Bo Modtager", shareContactInfo: true);

		var request = new MessagesController.SendMessageRequest(
			recipient.Id,
			RecipientType.Parent,
			"Hej fra forælder med samtykke",
			"Begge forældre har givet samtykke til deling af kontaktoplysninger.");

		using var client = CreateParentClient(senderSubject);
		var response = await client.PostAsJsonAsync("/api/v1/messages", request, JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
	}

	[Test]
	public async Task SendMessage_ParentToParent_WithoutConsent_Returns403()
	{
		const string senderSubject = "msg-parent-sender-no-consent";
		await CreateParentAsync(senderSubject, name: "Carla Afsender", shareContactInfo: true);
		var recipient = await CreateParentAsync("msg-parent-recipient-no-consent", name: "Dan Modtager", shareContactInfo: false);

		var request = new MessagesController.SendMessageRequest(
			recipient.Id,
			RecipientType.Parent,
			"Hej fra forælder uden samtykke",
			"Modtageren har ikke givet samtykke til deling af kontaktoplysninger.");

		using var client = CreateParentClient(senderSubject);
		var response = await client.PostAsJsonAsync("/api/v1/messages", request, JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task SendMessage_NoSenderRecord_Returns403()
	{
		// Authenticated user whose Keycloak subject has no Staff or Parent row in DB
		const string unknownSubject = "msg-unknown-sender-no-record";
		var recipient = await CreateParentAsync("msg-no-record-recipient", name: "Eva Modtager");

		var request = new MessagesController.SendMessageRequest(
			recipient.Id,
			RecipientType.Parent,
			"Besked fra ukendt bruger",
			"Denne bruger eksisterer ikke i databasen.");

		using var client = CreateStaffClient(unknownSubject);
		var response = await client.PostAsJsonAsync("/api/v1/messages", request, JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	// ── GET /api/v1/messages/inbox ────────────────────────────────────────────────

	[Test]
	public async Task GetInbox_Staff_Returns200()
	{
		const string recipientSubject = "msg-inbox-staff-recipient";
		var recipient = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId,
			name: "Frede Modtager", keycloakSubject: recipientSubject);

		var sender = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId,
			name: "Gitte Afsender", keycloakSubject: "msg-inbox-staff-sender");

		await CreateMessageAsync(
			sender.Id,
			RecipientType.Staff,
			recipient.Id,
			RecipientType.Staff,
			subject: "Indbakke testbesked til medarbejder",
			body: "Denne besked skal vises i medarbejderens indbakke.");

		using var client = CreateStaffClient(recipientSubject);
		var response = await client.GetAsync("/api/v1/messages/inbox");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var inbox = await response.Content.ReadFromJsonAsync<List<MessagesController.InboxMessageDto>>(JsonOpts);
		await Assert.That(inbox).IsNotNull();
		await Assert.That(inbox!.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(inbox.Any(m => m.Subject == "Indbakke testbesked til medarbejder")).IsTrue();
	}

	[Test]
	public async Task GetInbox_ParentWithNoMessages_Returns200Empty()
	{
		const string parentSubject = "msg-inbox-parent-empty";
		await CreateParentAsync(parentSubject, name: "Hanne Tom Indbakke");

		using var client = CreateParentClient(parentSubject);
		var response = await client.GetAsync("/api/v1/messages/inbox");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var inbox = await response.Content.ReadFromJsonAsync<List<MessagesController.InboxMessageDto>>(JsonOpts);
		await Assert.That(inbox).IsNotNull();
		await Assert.That(inbox!.Count).IsEqualTo(0);
	}

	// ── GET /api/v1/messages/{id}/thread ──────────────────────────────────────────

	[Test]
	public async Task GetThread_MissingAnchor_Returns404()
	{
		const string callerSubject = "msg-thread-missing-anchor";
		await CreateParentAsync(callerSubject, name: "Ida Missingtråd");

		using var client = CreateParentClient(callerSubject);
		var response = await client.GetAsync($"/api/v1/messages/{Guid.NewGuid()}/thread");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task GetThread_CallerNotParticipant_Returns403()
	{
		var sender = await CreateParentAsync("msg-thread-nonpart-sender", name: "Jens Afsender", shareContactInfo: true);
		var recipient = await CreateParentAsync("msg-thread-nonpart-recipient", name: "Kim Modtager", shareContactInfo: true);
		var outsiderSubject = "msg-thread-nonpart-outsider";
		await CreateParentAsync(outsiderSubject, name: "Liv Udenfor");

		var anchor = await CreateMessageAsync(sender.Id, RecipientType.Parent, recipient.Id, RecipientType.Parent);

		using var client = CreateParentClient(outsiderSubject);
		var response = await client.GetAsync($"/api/v1/messages/{anchor.Id}/thread");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task GetThread_WithBranchingReplies_ReturnsAllMessagesOrderedBySentAt()
	{
		var a = await CreateParentAsync("msg-thread-branch-a", name: "Mie A", shareContactInfo: true);
		var b = await CreateParentAsync("msg-thread-branch-b", name: "Noah B", shareContactInfo: true);

		var root = await CreateMessageAsync(a.Id, RecipientType.Parent, b.Id, RecipientType.Parent, subject: "Rod", body: "Rodbesked");

		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		// Two replies to the same root message — a plain single-child walk would only ever
		// surface one of these; the recursive CTE must return both.
		var replyOne = new Message
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			SenderId = b.Id,
			SenderType = RecipientType.Parent,
			RecipientId = a.Id,
			RecipientType = RecipientType.Parent,
			Subject = "Rod",
			Body = "Svar et",
			SentAt = root.SentAt.AddMinutes(1),
			InReplyToId = root.Id,
		};
		var replyTwo = new Message
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			SenderId = b.Id,
			SenderType = RecipientType.Parent,
			RecipientId = a.Id,
			RecipientType = RecipientType.Parent,
			Subject = "Rod",
			Body = "Svar to",
			SentAt = root.SentAt.AddMinutes(2),
			InReplyToId = root.Id,
		};
		// replyThree shares replyTwo's SentAt exactly — the ordering must fall back to Id
		// as a deterministic tie-breaker rather than leaving the pair in arbitrary order.
		var replyThree = new Message
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			SenderId = a.Id,
			SenderType = RecipientType.Parent,
			RecipientId = b.Id,
			RecipientType = RecipientType.Parent,
			Subject = "Rod",
			Body = "Svar tre",
			SentAt = replyTwo.SentAt,
			InReplyToId = root.Id,
		};
		db.Messages.AddRange(replyOne, replyTwo, replyThree);
		await db.SaveChangesAsync();

		using var client = CreateParentClient("msg-thread-branch-a");
		var response = await client.GetAsync($"/api/v1/messages/{root.Id}/thread");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var thread = await response.Content.ReadFromJsonAsync<List<MessagesController.ThreadMessageDto>>(JsonOpts);
		await Assert.That(thread).IsNotNull();
		await Assert.That(thread!.Select(m => m.Id)).Contains(root.Id);
		await Assert.That(thread.Select(m => m.Id)).Contains(replyOne.Id);
		await Assert.That(thread.Select(m => m.Id)).Contains(replyTwo.Id);
		await Assert.That(thread.Select(m => m.Id)).Contains(replyThree.Id);
		var sentTimes = thread.Select(m => m.SentAt).ToList();
		await Assert.That(sentTimes.SequenceEqual(sentTimes.OrderBy(t => t))).IsTrue();

		var tiedPairIds = thread.Where(m => m.SentAt == replyTwo.SentAt).Select(m => m.Id).ToList();
		var expectedTiedOrder = tiedPairIds.OrderBy(id => id).ToList();
		await Assert.That(tiedPairIds.SequenceEqual(expectedTiedOrder)).IsTrue();

		var rootDto = thread.Single(m => m.Id == root.Id);
		await Assert.That(rootDto.InReplyToId).IsNull();
		var replyOneDto = thread.Single(m => m.Id == replyOne.Id);
		await Assert.That(replyOneDto.InReplyToId).IsEqualTo(root.Id);
		var replyTwoDto = thread.Single(m => m.Id == replyTwo.Id);
		await Assert.That(replyTwoDto.InReplyToId).IsEqualTo(root.Id);
		var replyThreeDto = thread.Single(m => m.Id == replyThree.Id);
		await Assert.That(replyThreeDto.InReplyToId).IsEqualTo(root.Id);
	}

	// ── Reply authorization guards (POST /api/v1/messages with InReplyToId) ────────

	[Test]
	public async Task SendMessage_ReplyToGroupMessage_Returns404()
	{
		var sender = await CreateParentAsync("msg-reply-group-sender", name: "Ole Afsender", shareContactInfo: true);
		var recipient = await CreateParentAsync("msg-reply-group-recipient", name: "Pia Modtager", shareContactInfo: true);

		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var groupMessage = new GroupMessage
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			SenderParentId = sender.Id,
			SenderName = sender.Name,
			Audience = BroadcastAudience.AllParents,
			Subject = "Gruppe",
			Body = "Gruppebesked",
			RecipientCount = 1,
		};
		db.GroupMessages.Add(groupMessage);
		var groupFanOutMessage = new Message
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			SenderId = sender.Id,
			SenderType = RecipientType.Parent,
			RecipientId = recipient.Id,
			RecipientType = RecipientType.Parent,
			Subject = "Gruppe",
			Body = "Gruppebesked",
			SentAt = DateTimeOffset.UtcNow,
			GroupMessageId = groupMessage.Id,
		};
		db.Messages.Add(groupFanOutMessage);
		await db.SaveChangesAsync();

		var request = new MessagesController.SendMessageRequest(
			sender.Id, RecipientType.Parent, "Gruppe", "Forsøg på at svare på gruppebesked", groupFanOutMessage.Id);

		using var client = CreateParentClient("msg-reply-group-recipient");
		var response = await client.PostAsJsonAsync("/api/v1/messages", request, JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task SendMessage_ValidReply_PersistsInReplyToId()
	{
		var sender = await CreateParentAsync("msg-reply-valid-sender", name: "Nanna Afsender", shareContactInfo: true);
		var recipient = await CreateParentAsync("msg-reply-valid-recipient", name: "Oscar Modtager", shareContactInfo: true);

		var original = await CreateMessageAsync(sender.Id, RecipientType.Parent, recipient.Id, RecipientType.Parent);

		var request = new MessagesController.SendMessageRequest(
			sender.Id, RecipientType.Parent, "Re: Test emne", "Gyldigt svar", original.Id);

		using var client = CreateParentClient("msg-reply-valid-recipient");
		var response = await client.PostAsJsonAsync("/api/v1/messages", request, JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var created = await response.Content.ReadFromJsonAsync<CreatedMessageDto>(JsonOpts);
		await Assert.That(created).IsNotNull();

		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var reply = await db.Messages.AsNoTracking().IgnoreQueryFilters().SingleAsync(m => m.Id == created!.Id);
		await Assert.That(reply.InReplyToId).IsEqualTo(original.Id);

		using var threadClient = CreateParentClient("msg-reply-valid-recipient");
		var threadResponse = await threadClient.GetAsync($"/api/v1/messages/{original.Id}/thread");
		var thread = await threadResponse.Content.ReadFromJsonAsync<List<MessagesController.ThreadMessageDto>>(JsonOpts);
		var replyDto = thread!.Single(m => m.Id == reply.Id);
		await Assert.That(replyDto.InReplyToId).IsEqualTo(original.Id);
	}

	private sealed record CreatedMessageDto(Guid Id);

	[Test]
	public async Task SendMessage_ReplyByNonParticipant_Returns403()
	{
		var sender = await CreateParentAsync("msg-reply-nonpart-sender", name: "Qasim Afsender", shareContactInfo: true);
		var recipient = await CreateParentAsync("msg-reply-nonpart-recipient", name: "Rikke Modtager", shareContactInfo: true);
		var outsiderSubject = "msg-reply-nonpart-outsider";
		await CreateParentAsync(outsiderSubject, name: "Sara Udenfor", shareContactInfo: true);

		var original = await CreateMessageAsync(sender.Id, RecipientType.Parent, recipient.Id, RecipientType.Parent);

		var request = new MessagesController.SendMessageRequest(
			sender.Id, RecipientType.Parent, "Test emne", "Forsøg på at svare uden at være deltager", original.Id);

		using var client = CreateParentClient(outsiderSubject);
		var response = await client.PostAsJsonAsync("/api/v1/messages", request, JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task SendMessage_ReplyWithRecipientMismatch_Returns403()
	{
		var sender = await CreateParentAsync("msg-reply-mismatch-sender", name: "Troels Afsender", shareContactInfo: true);
		var recipient = await CreateParentAsync("msg-reply-mismatch-recipient", name: "Ulla Modtager", shareContactInfo: true);
		var thirdParty = await CreateParentAsync("msg-reply-mismatch-third", name: "Vibe Tredjepart", shareContactInfo: true);

		var original = await CreateMessageAsync(sender.Id, RecipientType.Parent, recipient.Id, RecipientType.Parent);

		// Recipient replies but targets a third party instead of the original sender.
		var request = new MessagesController.SendMessageRequest(
			thirdParty.Id, RecipientType.Parent, "Test emne", "Forkert modtager på svar", original.Id);

		using var client = CreateParentClient("msg-reply-mismatch-recipient");
		var response = await client.PostAsJsonAsync("/api/v1/messages", request, JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}
}
