namespace Skoleoverblikket.Api.Storage;

public interface IObjectStorage
{
	Task UploadAsync(string key, string contentType, Stream content, CancellationToken cancellationToken = default);

	Task<string> UploadPublicAsync(string key, string contentType, Stream content, CancellationToken cancellationToken = default);

	/// <summary>
	/// Returns a presigned PUT URL the client can use to upload directly to S3,
	/// and the public URL the file will have after upload.
	/// </summary>
	Task<(string UploadUrl, string PublicUrl)> GeneratePresignedUploadUrlAsync(
		string key, string contentType, long contentLength, TimeSpan expiry, CancellationToken cancellationToken = default);

	Task DeleteAsync(string key, CancellationToken cancellationToken = default);

	/// <summary>
	/// Returns the size in bytes of the object at <paramref name="key"/>, or null if it does not exist.
	/// Used to verify a presigned client upload actually landed before trusting its metadata.
	/// </summary>
	Task<long?> GetObjectSizeAsync(string key, CancellationToken cancellationToken = default);

	/// <summary>
	/// Derives the storage key from a public URL previously returned by UploadPublicAsync.
	/// Returns null if the URL does not belong to this storage backend.
	/// </summary>
	string? GetKeyFromPublicUrl(string publicUrl);
}
