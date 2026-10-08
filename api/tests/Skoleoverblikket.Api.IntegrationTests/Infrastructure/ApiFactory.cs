using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Email;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Storage;
using Stripe;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using TUnit.AspNetCore;
using TUnit.Core.Interfaces;

namespace Skoleoverblikket.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Spins up a real PostgreSQL container and a Silo (MinIO fork) S3 container (via Testcontainers)
/// alongside the full ASP.NET Core pipeline. Auth is replaced by <see cref="TestAuthHandler"/>
/// which reads X-Test-TenantId / X-Test-Roles / X-Test-Subject headers so each HttpClient
/// can carry its own tenant identity without shared mutable state.
/// </summary>
public sealed class ApiFactory : TestWebApplicationFactory<Program>, IAsyncInitializer
{
	private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
		.WithDatabase("skoleoverblikket_test")
		.WithUsername("test")
		.WithPassword("test")
		.Build();

	private readonly MinioContainer _silo = new MinioBuilder("pgsty/silo:RELEASE.2026-09-16T00-00-00Z").Build();

	private readonly IContainer _stripeMock = new ContainerBuilder("stripe/stripe-mock:v0.196.0")
		.WithPortBinding(12111, true)
		.WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r
			.ForPort(12111)
			.ForPath("/v1/charges")
			.ForStatusCode(HttpStatusCode.Unauthorized)))
		.Build();

	/// <summary>The backup agent's ops bucket (task 60), in the same Silo as the files bucket.</summary>
	public const string OpsBucketName = "skoleoverblikket-ops-test";

	/// <summary>Every email the API sent during the test session. Filter by a per-test address.</summary>
	public RecordingEmailSender Emails { get; } = new();

	public async Task InitializeAsync()
	{
		await Task.WhenAll(_postgres.StartAsync(), _silo.StartAsync(), _stripeMock.StartAsync());

		await using var scope = Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		await db.Database.MigrateAsync();

		var s3 = scope.ServiceProvider.GetRequiredService<IAmazonS3>();
		var opts = scope.ServiceProvider.GetRequiredService<IOptions<S3Options>>().Value;
		var exists = await AmazonS3Util.DoesS3BucketExistV2Async(s3, opts.DefaultBucketName);
		if (!exists)
		{
			await s3.PutBucketAsync(new PutBucketRequest { BucketName = opts.DefaultBucketName });
		}
	}

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		builder.UseEnvironment("Testing");

		builder.ConfigureAppConfiguration(config =>
		{
			var siloUrl = _silo.GetConnectionString();
			config.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["ObjectStorage:ServiceUrl"] = siloUrl,
				["ObjectStorage:AccessKey"] = _silo.GetAccessKey(),
				["ObjectStorage:SecretKey"] = _silo.GetSecretKey(),
				["ObjectStorage:DefaultBucketName"] = "skoleoverblikket-test",
				["ObjectStorage:PublicEndpoint"] = siloUrl,
				["ObjectStorage:PresignedUploadSigningKey"] = "test-signing-key",
				["BackupStatus:ServiceUrl"] = localStackUrl,
				["BackupStatus:AccessKey"] = "test",
				["BackupStatus:SecretKey"] = "test",
				["BackupStatus:BucketName"] = OpsBucketName,
			});
		});

		builder.ConfigureServices(services =>
		{
			// Replace DB with the Testcontainers instance
			services.RemoveAll<DbContextOptions<AppDbContext>>();
			services.AddDbContext<AppDbContext>(options =>
				options.UseNpgsql(_postgres.GetConnectionString()));

			// Replace JWT auth with a header-driven test scheme.
			// Tenant identity comes from the tenant_id claim set by TestAuthHandler
			// reading X-Test-TenantId — the real HttpTenantContext reads that claim.
			services.AddAuthentication(TestAuthHandler.SchemeName)
					.AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
						TestAuthHandler.SchemeName, _ => { });

			// Replace notification service with a no-op so tests don't need an SMTP server
			services.RemoveAll<INotificationService>();
			services.AddScoped<INotificationService, NoOpNotificationService>();

			// Record email instead of sending it — there is no SMTP server in tests
			services.RemoveAll<IEmailSender>();
			services.AddSingleton<IEmailSender>(Emails);

			// Point S3 client and S3Options at Silo
			var siloUrl = _silo.GetConnectionString();
			services.RemoveAll<IAmazonS3>();
			services.AddSingleton<IAmazonS3>(_ =>
			{
				var config = new AmazonS3Config { ServiceURL = siloUrl, ForcePathStyle = true };
				return new AmazonS3Client(new BasicAWSCredentials(_silo.GetAccessKey(), _silo.GetSecretKey()), config);
			});

			// Point the shared StripeClient at stripe-mock instead of the real Stripe API
			var stripeMockUrl = $"http://{_stripeMock.Hostname}:{_stripeMock.GetMappedPublicPort(12111)}";
			services.RemoveAll<IStripeClient>();
			services.AddSingleton<IStripeClient>(_ => new StripeClient(
				apiKey: "sk_test_stub",
				apiBase: stripeMockUrl));
		});
	}

	public override async ValueTask DisposeAsync()
	{
		await base.DisposeAsync();
		await Task.WhenAll(
			_postgres.DisposeAsync().AsTask(),
			_silo.DisposeAsync().AsTask(),
			_stripeMock.DisposeAsync().AsTask());
	}
}
