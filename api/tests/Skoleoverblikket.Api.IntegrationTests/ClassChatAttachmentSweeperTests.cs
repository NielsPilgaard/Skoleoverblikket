using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Storage;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="ClassChatAttachmentSweeper"/> — the background job that reaps
/// class-chat attachments that were confirmed but never attached to a post. Covers: only rows past
/// the grace window and still unclaimed are removed, claimed rows and fresh rows survive, the
/// storage object is deleted alongside the row, and the sweep spans tenants (it runs with the
/// global tenant filter bypassed).
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class ClassChatAttachmentSweeperTests(ApiFactory factory)
{
	private readonly ApiFactory _factory = factory;

	private async Task<Guid> SeedAttachmentAsync(
		Guid tenantId, Guid classId, DateTimeOffset uploadedAt, Guid? messageId = null, bool putObject = true)
	{
		var id = Guid.NewGuid();
		var key = $"class-chat/{tenantId}/{classId}/{id}.pdf";

		using var scope = _factory.Services.CreateScope();

		if (putObject)
		{
			var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
			using var content = new MemoryStream([1, 2, 3, 4]);
			await storage.UploadAsync(key, "application/pdf", content);
		}

		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		db.ClassChatAttachments.Add(new ClassChatAttachment
		{
			Id = id,
			TenantId = tenantId,
			ClassId = classId,
			MessageId = messageId,
			FileName = "vedhaeftning.pdf",
			ContentType = "application/pdf",
			SizeBytes = 4,
			StorageKey = key,
			Url = $"https://storage.example.com/{key}",
			UploadedAt = uploadedAt,
		});
		await db.SaveChangesAsync();
		return id;
	}

	private async Task<int> RunSweepAsync()
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();

		return await ClassChatAttachmentSweeper.SweepAsync(
			db,
			storage,
			NullLogger.Instance,
			DateTimeOffset.UtcNow - ClassChatAttachmentSweeper.Grace,
			ClassChatAttachmentSweeper.BatchSize,
			CancellationToken.None);
	}

	private async Task<bool> AttachmentExistsAsync(Guid id)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		return await db.ClassChatAttachments.IgnoreQueryFilters().AnyAsync(a => a.Id == id);
	}

	private async Task<bool> ObjectExistsAsync(string key)
	{
		using var scope = _factory.Services.CreateScope();
		var s3 = scope.ServiceProvider.GetRequiredService<IAmazonS3>();
		try
		{
			await s3.GetObjectMetadataAsync("skoleoverblikket-test", key);
			return true;
		}
		catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
		{
			return false;
		}
	}

	[Test]
	public async Task Sweep_RemovesRowAndStorageObject_ForExpiredUnclaimedAttachment()
	{
		var tenantId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, tenantId);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, tenantId, "sweep-a");

		var uploadedAt = DateTimeOffset.UtcNow - ClassChatAttachmentSweeper.Grace - TimeSpan.FromMinutes(5);
		var id = await SeedAttachmentAsync(tenantId, klass.Id, uploadedAt);
		var key = $"class-chat/{tenantId}/{klass.Id}/{id}.pdf";

		await RunSweepAsync();

		await Assert.That(await AttachmentExistsAsync(id)).IsFalse();
		await Assert.That(await ObjectExistsAsync(key)).IsFalse();
	}

	[Test]
	public async Task Sweep_KeepsFreshUnclaimedAttachment()
	{
		var tenantId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, tenantId);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, tenantId, "sweep-b");

		// Uploaded just now — a confirm could still be racing the sweep.
		var id = await SeedAttachmentAsync(tenantId, klass.Id, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1));

		await RunSweepAsync();

		await Assert.That(await AttachmentExistsAsync(id)).IsTrue();
	}

	[Test]
	public async Task Sweep_KeepsClaimedAttachment_EvenWhenOld()
	{
		var tenantId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, tenantId);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, tenantId, "sweep-c");

		Guid messageId;
		using (var scope = _factory.Services.CreateScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			var message = new ClassChatMessage
			{
				Id = Guid.NewGuid(),
				TenantId = tenantId,
				ClassId = klass.Id,
				SenderType = SenderType.Staff,
				SenderId = Guid.NewGuid(),
				Body = "Se vedhæftning",
				SentAt = DateTimeOffset.UtcNow,
			};
			db.ClassChatMessages.Add(message);
			await db.SaveChangesAsync();
			messageId = message.Id;
		}

		var uploadedAt = DateTimeOffset.UtcNow - ClassChatAttachmentSweeper.Grace - TimeSpan.FromDays(30);
		var id = await SeedAttachmentAsync(tenantId, klass.Id, uploadedAt, messageId);

		await RunSweepAsync();

		await Assert.That(await AttachmentExistsAsync(id)).IsTrue();
	}

	[Test]
	public async Task Sweep_SpansTenants()
	{
		var firstTenant = Guid.NewGuid();
		var secondTenant = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, firstTenant);
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, secondTenant);
		var (firstClass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, firstTenant, "sweep-d1");
		var (secondClass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, secondTenant, "sweep-d2");

		var uploadedAt = DateTimeOffset.UtcNow - ClassChatAttachmentSweeper.Grace - TimeSpan.FromMinutes(5);
		var firstId = await SeedAttachmentAsync(firstTenant, firstClass.Id, uploadedAt);
		var secondId = await SeedAttachmentAsync(secondTenant, secondClass.Id, uploadedAt);

		await RunSweepAsync();

		await Assert.That(await AttachmentExistsAsync(firstId)).IsFalse();
		await Assert.That(await AttachmentExistsAsync(secondId)).IsFalse();
	}
}
