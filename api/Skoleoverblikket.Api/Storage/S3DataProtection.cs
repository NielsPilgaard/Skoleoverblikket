using System.Xml.Linq;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.OpenApi;

namespace Skoleoverblikket.Api.Storage;

public static class S3DataProtectionExtensions
{
	/// <summary>
	/// Keeps the data protection key ring in the private object storage bucket, so tokens signed
	/// before a deploy (export download links) still validate after the container is replaced.
	/// </summary>
	public static IServiceCollection AddS3DataProtection(this IServiceCollection services)
	{
		// Fixed name: the default discriminator is the content root path, which must not decide whether keys match.
		services.AddDataProtection().SetApplicationName("Skoleoverblikket");

		// The key ring is read on host start, and object storage isn't running during build-time generation.
		if (OpenApiGeneration.IsRunning)
		{
			return services;
		}

		services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
			new ConfigureOptions<KeyManagementOptions>(options =>
				options.XmlRepository = new S3XmlRepository(
					sp.GetRequiredService<IAmazonS3>(),
					sp.GetRequiredService<IOptions<S3Options>>())));

		return services;
	}
}

/// <summary>Stores each data protection key as a private XML object under <see cref="Prefix"/>.</summary>
public sealed class S3XmlRepository(IAmazonS3 s3, IOptions<S3Options> opts) : IXmlRepository
{
	// Outside every per-school prefix, so school export and deletion never touch it.
	public const string Prefix = "data-protection/keys/";

	private readonly string _bucket = opts.Value.DefaultBucketName;

	// IXmlRepository is synchronous. It runs on first use and when the key ring refreshes (about daily), not per request.
	public IReadOnlyCollection<XElement> GetAllElements() => GetAllElementsAsync().GetAwaiter().GetResult();

	public void StoreElement(XElement element, string friendlyName) =>
		StoreElementAsync(element, friendlyName).GetAwaiter().GetResult();

	private async Task<IReadOnlyCollection<XElement>> GetAllElementsAsync()
	{
		var elements = new List<XElement>();
		var request = new ListObjectsV2Request { BucketName = _bucket, Prefix = Prefix };
		ListObjectsV2Response page;
		do
		{
			page = await s3.ListObjectsV2Async(request);
			foreach (var o in page.S3Objects ?? [])
			{
				using var response = await s3.GetObjectAsync(_bucket, o.Key);
				elements.Add(await XElement.LoadAsync(response.ResponseStream, LoadOptions.None, CancellationToken.None));
			}

			request.ContinuationToken = page.NextContinuationToken;
		}
		while (page.IsTruncated == true);

		return elements;
	}

	private async Task StoreElementAsync(XElement element, string friendlyName)
	{
		await s3.PutObjectAsync(new PutObjectRequest
		{
			BucketName = _bucket,
			Key = $"{Prefix}{friendlyName}.xml",
			ContentBody = element.ToString(SaveOptions.DisableFormatting),
			ContentType = "application/xml",
			CannedACL = S3CannedACL.NoACL,
		});
	}
}
