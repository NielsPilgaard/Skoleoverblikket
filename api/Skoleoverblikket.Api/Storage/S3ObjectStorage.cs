using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using System.Net;

namespace Skoleoverblikket.Api.Storage;

public sealed class S3ObjectStorage(IAmazonS3 s3, IOptions<S3Options> opts) : IObjectStorage
{
	private readonly S3Options _options = opts.Value;

	public async Task UploadAsync(string key, string contentType, Stream content, CancellationToken cancellationToken = default)
	{
		var request = new PutObjectRequest
		{
			BucketName = _options.DefaultBucketName,
			Key = key,
			InputStream = content,
			ContentType = contentType,
			CannedACL = S3CannedACL.NoACL,
		};

		await s3.PutObjectAsync(request, cancellationToken);
	}

	public async Task<string> UploadPublicAsync(string key, string contentType, Stream content, CancellationToken cancellationToken = default)
	{
		var request = new PutObjectRequest
		{
			BucketName = _options.DefaultBucketName,
			Key = key,
			InputStream = content,
			ContentType = contentType,
			CannedACL = S3CannedACL.PublicRead,
		};

		await s3.PutObjectAsync(request, cancellationToken);

		return BuildPublicUrl(key);
	}

	public Task<(string UploadUrl, string PublicUrl)> GeneratePresignedUploadUrlAsync(
		string key, string contentType, long contentLength, TimeSpan expiry, CancellationToken cancellationToken = default)
	{
		var request = new GetPreSignedUrlRequest
		{
			BucketName = _options.DefaultBucketName,
			Key = key,
			Verb = HttpVerb.PUT,
			Expires = DateTime.UtcNow.Add(expiry),
			ContentType = contentType,
		};

		var uploadUrl = RewriteOrigin(s3.GetPreSignedURL(request), _options.PublicEndpoint);
		var publicUrl = BuildPublicUrl(key);

		return Task.FromResult((uploadUrl, publicUrl));
	}

	public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
	{
		await s3.DeleteObjectAsync(_options.DefaultBucketName, key, cancellationToken);
	}

	public async Task<int> DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
	{
		// An empty prefix would match the whole bucket.
		ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

		var deleted = 0;
		var request = new ListObjectsV2Request { BucketName = _options.DefaultBucketName, Prefix = prefix };
		ListObjectsV2Response page;
		do
		{
			page = await s3.ListObjectsV2Async(request, cancellationToken);
			if (page.S3Objects is { Count: > 0 } objects)
			{
				var response = await s3.DeleteObjectsAsync(new DeleteObjectsRequest
				{
					BucketName = _options.DefaultBucketName,
					Objects = objects.Select(o => new KeyVersion { Key = o.Key }).ToList(),
				}, cancellationToken);

				if (response.DeleteErrors is { Count: > 0 } errors)
				{
					throw new InvalidOperationException(
						$"Could not delete {errors.Count} object(s) under '{prefix}', first: {errors[0].Key} ({errors[0].Code})");
				}

				deleted += objects.Count;
			}

			request.ContinuationToken = page.NextContinuationToken;
		}
		while (page.IsTruncated == true);

		return deleted;
	}

	public async Task<long?> GetObjectSizeAsync(string key, CancellationToken cancellationToken = default)
	{
		try
		{
			var response = await s3.GetObjectMetadataAsync(_options.DefaultBucketName, key, cancellationToken);
			return response.ContentLength;
		}
		catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	public string? GetKeyFromPublicUrl(string publicUrl)
	{
		var prefix = $"{_options.SanitizedPublicEndpoint}/{_options.DefaultBucketName}/";

		return !publicUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
				? null
				: WebUtility.UrlDecode(publicUrl[prefix.Length..]);
	}

	private static string RewriteOrigin(string url, string serviceUrl)
	{
		var generated = new Uri(url);
		var target = new Uri(serviceUrl);
		var rewritten = new UriBuilder(generated) { Scheme = target.Scheme, Host = target.Host, Port = target.Port };
		return rewritten.Uri.ToString();
	}

	private string BuildPublicUrl(string key)
	{
		var encoded = string.Join("/", key.TrimStart('/').Split('/').Select(Uri.EscapeDataString));
		return $"{_options.SanitizedPublicEndpoint}/{_options.DefaultBucketName}/{encoded}";
	}
}
