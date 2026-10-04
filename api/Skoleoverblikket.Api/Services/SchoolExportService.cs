using System.Collections;
using System.Globalization;
using System.IO.Compression;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Storage;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// "Download everything": one ZIP with a CSV per table and every uploaded file, so a school that
/// leaves (or asks for GDPR art. 20 portability) gets all its data in one click. The ZIP is written
/// straight to the response stream, so nothing is buffered or stored.
///
/// Tables come from the EF model, not a hand-kept list, so a new table is exported without anyone
/// remembering to add it. <see cref="ExportedTables"/> fails for a table it cannot scope to one school.
/// </summary>
public sealed class SchoolExportService(
	AppDbContext db,
	ITenantContext tenant,
	IObjectStorage storage,
	ILogger<SchoolExportService> logger)
{
	/// <summary>Billing state kept per subscription, not per school, and holding only Stripe ids.</summary>
	private static readonly HashSet<Type> NotExported = [typeof(Subscription), typeof(SubscriptionModuleItem)];

	/// <summary>
	/// Invitation tokens are live credentials: anyone holding one can accept the invitation.
	/// Everything else, including Keycloak subjects (opaque ids other tables refer to), is exported.
	/// </summary>
	private const string TokenColumn = "Token";

	private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

	private static readonly System.Reflection.MethodInfo WriteTableMethod =
		typeof(SchoolExportService).GetMethod(nameof(WriteTableAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

	public static string ZipFileName(string schoolName, DateTimeOffset now) =>
		$"{SafeName(schoolName)}-alle-data-{SchoolDayCalendar.DanishDate(now):yyyy-MM-dd}.zip";

	public async Task<string> GetSchoolNameAsync(CancellationToken cancellationToken) =>
		// School's query filter cannot be translated (its TenantId is not mapped), so match on Id.
		await db.Schools.AsNoTracking().IgnoreQueryFilters()
			.Where(s => s.Id == tenant.TenantId)
			.Select(s => s.Name)
			.FirstAsync(cancellationToken);

	public async Task WriteZipAsync(Stream output, CancellationToken cancellationToken)
	{
		var body = new AsyncOnlyStream(output);
		await using (var zip = await ZipArchive.CreateAsync(body, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken))
		{
			await WriteTextEntryAsync(zip, "LÆS MIG.txt", ReadMe, cancellationToken);

			foreach (var entityType in ExportedTables(db.Model))
			{
				await (Task)WriteTableMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [zip, entityType, cancellationToken])!;
			}

			await WriteFilesAsync(zip, cancellationToken);
		}

		await body.FlushAsync(cancellationToken);
	}

	/// <summary>
	/// Every table that holds school data. A table needs a TenantId column to be scoped to one school;
	/// one without it (other than billing) throws, so the export tests fail instead of skipping data.
	/// </summary>
	public static List<IEntityType> ExportedTables(IModel model)
	{
		var tables = new List<IEntityType>();
		foreach (var entityType in model.GetEntityTypes().OrderBy(t => t.GetTableName(), StringComparer.Ordinal))
		{
			if (NotExported.Contains(entityType.ClrType))
			{
				continue;
			}

			if (entityType.ClrType != typeof(School) && entityType.FindProperty(nameof(ITenantScoped.TenantId)) is null)
			{
				throw new InvalidOperationException(
					$"{entityType.DisplayName()} has no TenantId, so the school export cannot tell which school its rows belong to.");
			}

			tables.Add(entityType);
		}

		return tables;
	}

	private async Task WriteTableAsync<TEntity>(ZipArchive zip, IEntityType entityType, CancellationToken cancellationToken)
		where TEntity : class
	{
		var schoolId = tenant.TenantId;
		var columns = entityType.GetProperties().Where(p => p.Name != TokenColumn).ToList();

		// IgnoreQueryFilters so archived rows are exported too; the explicit TenantId (or Id for the
		// school itself) keeps the query inside this one school. Isolation is covered by SchoolExportTests.
		var keyColumn = entityType.ClrType == typeof(School) ? nameof(School.Id) : nameof(ITenantScoped.TenantId);
		// Join tables like ParentStudents are shared-type entities and can only be reached by name.
		var set = entityType.HasSharedClrType ? db.Set<TEntity>(entityType.Name) : db.Set<TEntity>();
		var rows = set.IgnoreQueryFilters().AsNoTracking()
			.Where(e => EF.Property<Guid>(e, keyColumn) == schoolId)
			.Select(RowSelector<TEntity>(columns))
			.AsAsyncEnumerable();

		var entry = zip.CreateEntry($"data/{entityType.GetTableName()}.csv", CompressionLevel.Optimal);
		await using var writer = new StreamWriter(await entry.OpenAsync(cancellationToken), Utf8WithBom);
		await writer.WriteLineAsync(string.Join(';', columns.Select(c => CsvField(c.Name))));
		await foreach (var row in rows.WithCancellation(cancellationToken))
		{
			await writer.WriteLineAsync(string.Join(';', row.Select(v => CsvField(FormatValue(v)))));
		}
	}

	/// <summary><c>e =&gt; new object?[] { EF.Property(e, "A"), EF.Property(e, "B"), ... }</c>, so shadow properties are included too.</summary>
	private static Expression<Func<TEntity, object?[]>> RowSelector<TEntity>(IReadOnlyList<IProperty> columns)
	{
		var entity = Expression.Parameter(typeof(TEntity), "e");
		var values = columns.Select(c => Expression.Convert(
			Expression.Call(typeof(EF), nameof(EF.Property), [c.ClrType], entity, Expression.Constant(c.Name)),
			typeof(object)));
		return Expression.Lambda<Func<TEntity, object?[]>>(Expression.NewArrayInit(typeof(object), values), entity);
	}

	/// <summary>
	/// Uploaded files under readable names: the file archive and board files keep their folder tree,
	/// class chat attachments go in a folder per class. Anything else the school owns in storage
	/// (avatars, logo, schedule backups) goes under "andre-filer" by its storage key.
	/// </summary>
	private async Task WriteFilesAsync(ZipArchive zip, CancellationToken cancellationToken)
	{
		var schoolId = tenant.TenantId;
		var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var exportedKeys = new HashSet<string>(StringComparer.Ordinal);

		var schoolFolders = await db.SchoolFileFolders.AsNoTracking()
			.ToDictionaryAsync(f => f.Id, f => (f.Name, f.ParentId), cancellationToken);
		var schoolFiles = await db.SchoolFiles.AsNoTracking()
			.Select(f => new { f.StorageKey, f.FileName, f.FolderId })
			.ToListAsync(cancellationToken);
		foreach (var f in schoolFiles)
		{
			await WriteFileAsync(zip, f.StorageKey, $"filer/{FolderPath(schoolFolders, f.FolderId)}{SafeName(f.FileName)}", usedPaths, exportedKeys, cancellationToken);
		}

		var boardFolders = await db.BoardFileFolders.AsNoTracking()
			.ToDictionaryAsync(f => f.Id, f => (f.Name, f.ParentId), cancellationToken);
		var boardFiles = await db.BoardFiles.AsNoTracking()
			.Select(f => new { f.StorageKey, f.FileName, f.FolderId })
			.ToListAsync(cancellationToken);
		foreach (var f in boardFiles)
		{
			await WriteFileAsync(zip, f.StorageKey, $"bestyrelse/{FolderPath(boardFolders, f.FolderId)}{SafeName(f.FileName)}", usedPaths, exportedKeys, cancellationToken);
		}

		// IgnoreQueryFilters to name archived classes too; the explicit TenantId keeps it to this school.
		var classNames = await db.Classes.IgnoreQueryFilters().AsNoTracking()
			.Where(c => c.TenantId == schoolId)
			.ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
		var chatFiles = await db.ClassChatAttachments.AsNoTracking()
			.Where(a => a.MessageId != null)
			.Select(a => new { a.StorageKey, a.FileName, a.ClassId })
			.ToListAsync(cancellationToken);
		foreach (var f in chatFiles)
		{
			var className = SafeName(classNames.GetValueOrDefault(f.ClassId, "Ukendt klasse"));
			await WriteFileAsync(zip, f.StorageKey, $"klassechat/{className}/{SafeName(f.FileName)}", usedPaths, exportedKeys, cancellationToken);
		}

		foreach (var prefix in SchoolDeletionService.StoragePrefixes(schoolId))
		{
			await foreach (var key in storage.ListKeysAsync(prefix, cancellationToken))
			{
				// Posted chat attachments are exported by name above. The rest were uploaded in the
				// composer but never posted, are not school content, and are reaped by ClassChatAttachmentSweeper.
				if (!exportedKeys.Contains(key) && !key.StartsWith("class-chat/", StringComparison.Ordinal))
				{
					var path = string.Join('/', key.Replace(schoolId.ToString(), "skole", StringComparison.Ordinal).Split('/').Select(SafeName));
					await WriteFileAsync(zip, key, $"andre-filer/{path}", usedPaths, exportedKeys, cancellationToken);
				}
			}
		}
	}

	private async Task WriteFileAsync(
		ZipArchive zip, string storageKey, string path, HashSet<string> usedPaths, HashSet<string> exportedKeys, CancellationToken cancellationToken)
	{
		if (!exportedKeys.Add(storageKey))
		{
			return;
		}

		await using var content = await storage.OpenReadAsync(storageKey, cancellationToken);
		if (content is null)
		{
			// A row whose upload never landed. Nothing to export; the CSV still lists the row.
			logger.LogWarning("Export skipped missing object {StorageKey} for school {SchoolId}", storageKey, tenant.TenantId);
			return;
		}

		// Files are mostly PDFs, images and Office documents, which are compressed already.
		var entry = zip.CreateEntry(UniquePath(path, usedPaths), CompressionLevel.NoCompression);
		await using var target = await entry.OpenAsync(cancellationToken);
		await content.CopyToAsync(target, cancellationToken);
	}

	private static async Task WriteTextEntryAsync(ZipArchive zip, string name, string text, CancellationToken cancellationToken)
	{
		var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
		await using var writer = new StreamWriter(await entry.OpenAsync(cancellationToken), Utf8WithBom);
		await writer.WriteAsync(text.AsMemory(), cancellationToken);
	}

	private static string FolderPath(Dictionary<Guid, (string Name, Guid? ParentId)> folders, Guid? folderId)
	{
		var parts = new List<string>();
		// The depth cap guards against a parent cycle in bad data.
		for (var id = folderId; id is { } current && folders.TryGetValue(current, out var folder) && parts.Count < 50; id = folder.ParentId)
		{
			parts.Add(SafeName(folder.Name));
		}

		parts.Reverse();
		return parts.Count == 0 ? string.Empty : string.Join('/', parts) + "/";
	}

	/// <summary>"rapport.pdf" taken → "rapport (2).pdf". Two files may share a name in the same folder.</summary>
	private static string UniquePath(string path, HashSet<string> usedPaths)
	{
		if (usedPaths.Add(path))
		{
			return path;
		}

		var extension = Path.GetExtension(path);
		var stem = path[..^extension.Length];
		for (var n = 2; ; n++)
		{
			var candidate = $"{stem} ({n}){extension}";
			if (usedPaths.Add(candidate))
			{
				return candidate;
			}
		}
	}

	/// <summary>A single path segment that is valid on Windows, macOS and Linux.</summary>
	private static string SafeName(string name)
	{
		var invalid = "<>:\"/\\|?*";
		var cleaned = new string([.. name.Select(c => char.IsControl(c) || invalid.Contains(c) ? '_' : c)]).Trim().TrimEnd('.');
		return cleaned.Length == 0 ? "unavngivet" : cleaned;
	}

	private static string FormatValue(object? value) => value switch
	{
		null => string.Empty,
		string s => s,
		DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
		DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
		DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
		TimeOnly t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
		bool b => b ? "true" : "false",
		Enum e => e.ToString(),
		IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
		IEnumerable list => JsonSerializer.Serialize(list),
		_ => value.ToString() ?? string.Empty,
	};

	/// <summary>
	/// Semicolon-separated like the other CSV exports, so Danish Excel opens it in columns. Text that
	/// Excel would run as a formula (parents and staff type free text) is prefixed with an apostrophe.
	/// </summary>
	private static string CsvField(string value)
	{
		if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
		{
			value = "'" + value;
		}

		return value.IndexOfAny([';', '"', '\n', '\r']) >= 0
			? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
			: value;
	}

	/// <summary>
	/// Response bodies only allow async writes, but ZipArchive still writes synchronously when it
	/// closes an entry (the last deflate block and the data descriptor). Those writes are small, so
	/// they are held here and sent with the next async write or flush. File content goes straight through.
	/// </summary>
	private sealed class AsyncOnlyStream(Stream inner) : Stream
	{
		private readonly MemoryStream _pending = new();
		private long _position;

		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => _position;
			set => throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

		public override void Write(ReadOnlySpan<byte> buffer)
		{
			_pending.Write(buffer);
			_position += buffer.Length;
		}

		public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
			WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

		public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			await SendPendingAsync(cancellationToken);
			await inner.WriteAsync(buffer, cancellationToken);
			_position += buffer.Length;
		}

		/// <summary>Sync flush is a no-op: pending bytes go out with the next async write or flush.</summary>
		public override void Flush()
		{
		}

		public override async Task FlushAsync(CancellationToken cancellationToken)
		{
			await SendPendingAsync(cancellationToken);
			await inner.FlushAsync(cancellationToken);
		}

		private async Task SendPendingAsync(CancellationToken cancellationToken)
		{
			if (_pending.Length > 0)
			{
				await inner.WriteAsync(_pending.GetBuffer().AsMemory(0, (int)_pending.Length), cancellationToken);
				_pending.SetLength(0);
			}
		}

		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
	}

	private const string ReadMe = """
		Alle skolens data fra Skoleoverblikket

		Mappen "data" har én CSV-fil pr. tabel i databasen, med alle rækker for skolen,
		også arkiverede. Filerne er semikolonseparerede og UTF-8, så de kan åbnes i Excel.
		Rækkerne henviser til hinanden med id'er (fx ClassId i Students.csv er Id i Classes.csv).
		Tekst, der starter med =, +, - eller @, har fået en apostrof foran, så Excel ikke
		opfatter den som en formel.

		Mappen "filer" er skolens filarkiv med samme mapper som i Skoleoverblikket.
		Mappen "bestyrelse" er bestyrelsens filer.
		Mappen "klassechat" har vedhæftede filer fra klassechatten, en mappe pr. klasse.
		Mappen "andre-filer" har profilbilleder, skolens logo og andre gemte filer.

		Spørgsmål? Skriv til kontakt@skoleoverblikket.dk.
		""";
}
