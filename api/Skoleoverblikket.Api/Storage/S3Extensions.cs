using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Options;

namespace Skoleoverblikket.Api.Storage;

public static class S3Extensions
{
	public static IServiceCollection AddObjectStorage(this IServiceCollection services)
	{
		services.AddOptions<S3Options>()
				.BindConfiguration(S3Options.SectionName)
				.ValidateDataAnnotations();

		services.AddSingleton<IAmazonS3>(sp =>
		{
			var opts = sp.GetRequiredService<IOptions<S3Options>>().Value;
			return CreateClient(opts, opts.ServiceUrl);
		});

		services.AddScoped<IObjectStorage, S3ObjectStorage>();

		return services;
	}

	public static AmazonS3Client CreateClient(S3Options opts, string serviceUrl) =>
		new(new BasicAWSCredentials(opts.AccessKey, opts.SecretKey), new AmazonS3Config
		{
			ServiceURL = serviceUrl,
			ForcePathStyle = true,
		});

	/// <summary>Ensures the configured S3 bucket exists and has a CORS policy that allows browser PUT uploads.</summary>
	public static async Task EnsureS3BucketAsync(this IServiceProvider services)
	{
		var s3 = services.GetRequiredService<IAmazonS3>();
		var opts = services.GetRequiredService<IOptions<S3Options>>().Value;
		var applicationOptions = services.GetRequiredService<IOptions<ApplicationOptions>>().Value;

		var exists = await AmazonS3Util.DoesS3BucketExistV2Async(s3, opts.DefaultBucketName);
		if (!exists)
		{
			await s3.PutBucketAsync(new PutBucketRequest
			{
				BucketName = opts.DefaultBucketName,
			});
		}

		await s3.PutCORSConfigurationAsync(new PutCORSConfigurationRequest
		{
			BucketName = opts.DefaultBucketName,
			Configuration = new CORSConfiguration
			{
				Rules =
				[
					new CORSRule
					{
						AllowedMethods = ["PUT", "GET", "HEAD"],
						AllowedOrigins = [applicationOptions.SanitizedBaseUrl],
						AllowedHeaders = ["*"],
						MaxAgeSeconds = 3600,
					}
				]
			}
		});
	}
}
