using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// The backoffice backup card reads the agent's status.json from the ops bucket (Silo here,
/// never real OVH). Superadmins get it, school admins don't.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class BackupStatusTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
	{
		Converters = { new JsonStringEnumConverter() },
	};

	private HttpClient Client(string roles)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", Guid.NewGuid().ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", roles);
		return client;
	}

	private async Task WriteStatusAsync(string json)
	{
		var s3 = factory.Services.GetRequiredService<IAmazonS3>();
		if (!await AmazonS3Util.DoesS3BucketExistV2Async(s3, ApiFactory.OpsBucketName))
		{
			await s3.PutBucketAsync(ApiFactory.OpsBucketName);
		}

		await s3.PutObjectAsync(new PutObjectRequest
		{
			BucketName = ApiFactory.OpsBucketName,
			Key = BackupStatusService.StatusObjectKey,
			ContentBody = json,
			ContentType = "application/json",
		});
	}

	[Test]
	public async Task SuperAdmin_GetsStatus_AdminIsForbidden()
	{
		var generatedAt = DateTimeOffset.UtcNow.AddMinutes(-4);
		await WriteStatusAsync($$"""
			{
			  "schemaVersion": 1,
			  "generatedAt": "{{generatedAt:O}}",
			  "health": "Degraded",
			  "issues": ["pgdata-b has held the old database for 8 days. Delete it when you're done investigating."],
			  "dataSecuredAt": "{{generatedAt.AddMinutes(-3):O}}",
			  "backups": { "lastFullAt": "{{generatedAt.AddHours(-9):O}}", "oldestRestorableAt": "{{generatedAt.AddDays(-13):O}}", "retentionDays": 14, "retentionOk": true, "count": 14 },
			  "drill": { "lastAt": "{{generatedAt.AddDays(-2):O}}", "lastOk": true, "lastMinutes": 3.5 },
			  "wal": { "slotStatus": "reserved" },
			  "sshTunnelCommand": "ssh -L 9090:127.0.0.1:9090 ubuntu@vps"
			}
			""");

		var response = await Client("superadmin").GetAsync("/api/v1/admin/backup-status");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var status = (await response.Content.ReadFromJsonAsync<BackupStatusDto>(JsonOpts))!;
		await Assert.That(status.Health).IsEqualTo(BackupHealth.Degraded);
		await Assert.That(status.GeneratedAt).IsEqualTo(generatedAt);
		await Assert.That(status.RetentionOk).IsTrue();
		await Assert.That(status.LastDrillOk).IsEqualTo(true);
		await Assert.That(status.Issues.Count).IsEqualTo(1);
		await Assert.That(status.SshTunnelCommand).IsEqualTo("ssh -L 9090:127.0.0.1:9090 ubuntu@vps");

		var forbidden = await Client("admin").GetAsync("/api/v1/admin/backup-status");
		await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}
}
