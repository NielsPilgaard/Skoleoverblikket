namespace Skoleoverblikket.Api.Storage;

/// <summary>
/// A file record backed by object storage. Implemented by <c>SchoolFile</c> and <c>BoardFile</c>
/// so <see cref="FileUploadService"/> can operate on either without knowing the concrete type.
/// </summary>
public interface IStoredFile
{
	Guid Id { get; }
	long SizeBytes { get; }
	string StorageKey { get; }
	string FileName { get; }
	Guid? FolderId { get; }
}

/// <summary>
/// A folder in a file tree. Implemented by <c>SchoolFileFolder</c> and <c>BoardFileFolder</c>
/// so descendant-collection logic can be shared.
/// </summary>
public interface IFileFolder
{
	Guid Id { get; }
	Guid? ParentId { get; }
}
