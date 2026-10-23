using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Email;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Notification e-mail delivery: the real <see cref="NotificationService"/> (ApiFactory swaps in a no-op for
/// every endpoint) queues e-mails that go out after the call returns, in Bcc batches that fit the provider's
/// recipient limit, with every recipient reached exactly once.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class NotificationEmailTests(ApiFactory factory)
{
	private readonly Guid _tenantId = Guid.NewGuid();

	[Before(Test)]
	public Task SetUp() => TestDataBuilder.CreateSchoolAsync(factory.Services, _tenantId);

	private async Task<string[]> CreateParentsAsync(int count)
	{
		using var scope = factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var parents = Enumerable.Range(0, count).Select(i => new Parent
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = $"Forælder {i}",
			Email = $"parent-{Guid.NewGuid():N}@test.dk",
		}).ToList();
		db.Parents.AddRange(parents);
		await db.SaveChangesAsync();
		return [.. parents.Select(p => p.Email)];
	}

	private async Task NotifyAsync(Func<NotificationService, Task> notify)
	{
		await using var scope = factory.Services.CreateAsyncScope();
		scope.ServiceProvider.GetRequiredService<HttpTenantContext>().UseBackgroundTenant(_tenantId);
		await notify(ActivatorUtilities.CreateInstance<NotificationService>(scope.ServiceProvider));
	}

	/// <summary>The queue sends in the background, so wait until the expected number of messages has arrived.</summary>
	private async Task<List<EmailMessage>> WaitForEmailsAsync(string subject, int expected)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		List<EmailMessage> sent;
		do
		{
			sent = [.. factory.Emails.Sent.Where(m => m.Subject == subject)];
			if (sent.Count >= expected)
			{
				break;
			}

			await Task.Delay(50);
		}
		while (DateTime.UtcNow < deadline);

		return sent;
	}

	[Test]
	public async Task CreateBatch_ManyParents_SendsBccBatchesWithinTheRecipientLimit()
	{
		var smtp = factory.Services.GetRequiredService<IOptions<SmtpOptions>>().Value;
		var addresses = await CreateParentsAsync(smtp.BccBatchSize * 2 + 3);
		var parentIds = await ParentIdsAsync();
		var body = $"Ferietilmelding åben: {Guid.NewGuid()}";

		await NotifyAsync(service => service.CreateBatchAsync(
			parentIds.Select(id => new NotificationRequest(
				id, RecipientType.Parent, NotificationType.VacationRegistrationOpened, Guid.NewGuid(), body)),
			CancellationToken.None));

		var sent = await WaitForEmailsAsync(body, expected: 3);
		await Assert.That(sent.Count).IsEqualTo(3);
		await Assert.That(sent.All(m => m.To == smtp.FromAddress)).IsTrue();
		await Assert.That(sent.All(m => m.Bcc!.Count + 1 <= smtp.MaxRecipientsPerMessage)).IsTrue();
		await Assert.That(sent.SelectMany(m => m.Bcc!).Order().ToArray()).IsEquivalentTo(addresses.Order().ToArray());
	}

	[Test]
	public async Task Create_SingleParent_SendsOneEmailToThatParent()
	{
		var address = (await CreateParentsAsync(1)).Single();
		var parentId = (await ParentIdsAsync()).Single();
		var body = $"Ny besked: {Guid.NewGuid()}";

		await NotifyAsync(service => service.CreateAsync(
			parentId, RecipientType.Parent, NotificationType.VacationRegistrationOpened, null, body, CancellationToken.None));

		var sent = await WaitForEmailsAsync(body, expected: 1);
		await Assert.That(sent.Count).IsEqualTo(1);
		await Assert.That(sent[0].To).IsEqualTo(address);
	}

	private async Task<Guid[]> ParentIdsAsync()
	{
		await using var scope = factory.Services.CreateAsyncScope();
		scope.ServiceProvider.GetRequiredService<HttpTenantContext>().UseBackgroundTenant(_tenantId);
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		return await db.Parents.Select(p => p.Id).ToArrayAsync();
	}
}
