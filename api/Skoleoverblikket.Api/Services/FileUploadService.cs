using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Storage;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// Shared presigned-upload plumbing for <c>FilesController</c> and <c>BoardFilesController</c>:
/// HMAC confirm-token sign/verify, storage-quota accounting, presigned URL generation,
/// uploader-name resolution, and recursive folder-descendant / storage-cleanup helpers.
/// No behaviour is tenant-specific here beyond what the injected <see cref="ITenantContext"/>
/// already scopes through the DbContext global query filter.
/// </summary>
public sealed class FileUploadService(
	AppDbContext db,
	ITenantContext tenant,
	IObjectStorage storage,
	IHttpContextAccessor http,
	IOptions<S3Options> s3Options)
{
	/// <summary>100 GB — the storage quota for the Basis tier.</summary>
	public const long QuotaBytes = 100L * 1024 * 1024 * 1024;

	/// <summary>500 MB — the per-file upload limit.</summary>
	public const long MaxFileSizeBytes = 500L * 1024 * 1024;

	public static readonly TimeSpan PresignExpiry = TimeSpan.FromMinutes(60);

	/// <summary>Maps a file name's extension to a MIME type, defaulting to <c>application/octet-stream</c>.</summary>
	public static string ResolveContentType(string fileName)
	{
		var ext = Path.GetExtension(fileName).ToLowerInvariant();
		return FileExtensions.MimeTypes.GetValueOrDefault(ext, "application/octet-stream");
	}

	/// <summary>Lowercased file extension including the leading dot, e.g. <c>.pdf</c>.</summary>
	public static string ExtensionOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant();

	/// <summary>Sum of <c>SizeBytes</c> across all files of type <typeparamref name="TFile"/> in the current tenant.</summary>
	public async Task<long> GetUsedBytesAsync<TFile>(CancellationToken cancellationToken)
		where TFile : class, IStoredFile =>
		await db.Set<TFile>().SumAsync(f => (long?)f.SizeBytes ?? 0, cancellationToken);

	/// <summary>True if adding <paramref name="additionalBytes"/> to <paramref name="usedBytes"/> would exceed <see cref="QuotaBytes"/>.</summary>
	public static bool WouldExceedQuota(long usedBytes, long additionalBytes) =>
		usedBytes + additionalBytes > QuotaBytes;

	/// <summary>The display name of the currently authenticated uploader, or <c>"Ukendt"</c>.</summary>
	public string ResolveUploaderName() =>
		http.HttpContext?.User.FindFirstValue("name")
		?? http.HttpContext?.User.FindFirstValue("preferred_username")
		?? "Ukendt";

	/// <summary>Generates a presigned PUT URL plus the public URL the file will have after upload.</summary>
	public Task<(string UploadUrl, string PublicUrl)> GeneratePresignedUploadAsync(
		string key, string contentType, long contentLength, CancellationToken cancellationToken) =>
		storage.GeneratePresignedUploadUrlAsync(key, contentType, contentLength, PresignExpiry, cancellationToken);

	/// <summary>Unix-seconds timestamp at which a confirm token minted now should expire.</summary>
	public static long PresignExpiresAt() => DateTimeOffset.UtcNow.Add(PresignExpiry).ToUnixTimeSeconds();

	/// <summary>HMAC-SHA256 signs a serialised payload, producing a <c>base64(json).base64(sig)</c> token.</summary>
	public string SignToken<T>(T payload)
	{
		var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
		var keyBytes = Encoding.UTF8.GetBytes(SigningKey);
		var sig = HMACSHA256.HashData(keyBytes, jsonBytes);
		return Convert.ToBase64String(jsonBytes) + "." + Convert.ToBase64String(sig);
	}

	/// <summary>Verifies a token produced by <see cref="SignToken{T}"/> and deserialises its payload.</summary>
	public bool TryVerifyToken<T>(string token, out T? payload) where T : class
	{
		payload = null;
		var parts = token.Split('.');
		if (parts.Length != 2)
		{
			return false;
		}

		try
		{
			var jsonBytes = Convert.FromBase64String(parts[0]);
			var sig = Convert.FromBase64String(parts[1]);
			var keyBytes = Encoding.UTF8.GetBytes(SigningKey);

			var expected = HMACSHA256.HashData(keyBytes, jsonBytes);
			if (!CryptographicOperations.FixedTimeEquals(sig, expected))
			{
				return false;
			}

			payload = JsonSerializer.Deserialize<T>(jsonBytes);
			return payload is not null;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Returns <paramref name="rootId"/> plus every descendant folder id, walking the tree of
	/// <typeparamref name="TFolder"/> in the current tenant.
	/// </summary>
	public async Task<List<Guid>> CollectFolderAndDescendantIdsAsync<TFolder>(Guid rootId, CancellationToken cancellationToken)
		where TFolder : class, IFileFolder
	{
		var result = new List<Guid> { rootId };
		await CollectDescendantsAsync<TFolder>(rootId, result, cancellationToken);
		return result;
	}

	private async Task CollectDescendantsAsync<TFolder>(Guid parentId, List<Guid> into, CancellationToken cancellationToken)
		where TFolder : class, IFileFolder
	{
		var children = await db.Set<TFolder>()
							   .Where(f => f.ParentId == parentId)
							   .Select(f => f.Id)
							   .ToListAsync(cancellationToken);

		foreach (var childId in children)
		{
			into.Add(childId);
			await CollectDescendantsAsync<TFolder>(childId, into, cancellationToken);
		}
	}

	/// <summary>
	/// Deletes each file's storage object, collecting a Danish warning string for any that fail
	/// so the caller can still remove the DB rows and report partial failure.
	/// </summary>
	public async Task<List<string>> DeleteFilesFromStorageAsync(
		IEnumerable<IStoredFile> files, CancellationToken cancellationToken)
	{
		var warnings = new List<string>();
		foreach (var file in files)
		{
			try
			{
				await storage.DeleteAsync(file.StorageKey, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				// A cancelled request must abort — not fall through to the caller
				// removing DB rows for files whose storage objects still exist.
				throw;
			}
			catch (Exception ex)
			{
				warnings.Add($"Filen '{file.FileName}' kunne ikke slettes fra lageret: {ex.Message}");
			}
		}

		return warnings;
	}

	/// <summary>Deletes a single storage object; used by the plain single-file delete path.</summary>
	public Task DeleteFromStorageAsync(string storageKey, CancellationToken cancellationToken) =>
		storage.DeleteAsync(storageKey, cancellationToken);

	public Guid TenantId => tenant.TenantId;

	private string SigningKey => s3Options.Value.PresignedUploadSigningKey;
}
