using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Storage;

namespace Skoleoverblikket.Api.Models;

/// <summary>
/// A document or image attached to a <see cref="ClassChatMessage"/>, uploaded through the
/// same presign+confirm flow as the file archive. Implements <see cref="IStoredFile"/> so its
/// bytes count against the tenant storage quota. Attachments live outside the file-archive
/// folder tree, so <see cref="FolderId"/> is always null.
/// </summary>
public sealed class ClassChatAttachment : ITenantScoped, IStoredFile, IEntityTypeConfiguration<ClassChatAttachment>
{
	/// <summary>25 MB — the per-attachment upload limit for class chat.</summary>
	public const long MaxFileSizeBytes = 25L * 1024 * 1024;

	/// <summary>Max attachments on a single post.</summary>
	public const int MaxPerMessage = 10;

	public Guid Id { get; set; }
	public Guid TenantId { get; set; }

	/// <summary>
	/// Null until the post that owns this attachment is created. A row that stays null past the
	/// presign expiry was abandoned in the composer and is reaped by <see cref="ClassChatAttachmentSweeper"/>.
	/// </summary>
	public Guid? MessageId { get; set; }
	public ClassChatMessage? Message { get; set; }

	/// <summary>Set at presign time so an orphaned upload is still attributable to a klasse.</summary>
	public Guid ClassId { get; set; }

	[StringLength(500, MinimumLength = 1)]
	public required string FileName { get; set; }

	[StringLength(200)]
	public required string ContentType { get; set; }

	public long SizeBytes { get; set; }

	[StringLength(1000)]
	public required string StorageKey { get; set; }

	[StringLength(2000)]
	public required string Url { get; set; }

	public DateTimeOffset UploadedAt { get; init; } = DateTimeOffset.UtcNow;

	/// <summary>Class chat attachments are never filed into the archive folder tree.</summary>
	public Guid? FolderId => null;

	public void Configure(EntityTypeBuilder<ClassChatAttachment> builder)
	{
		builder.Ignore(a => a.FolderId);
		builder.Property(a => a.UploadedAt).HasDefaultValueSql("now()");

		builder.HasOne(a => a.Message)
			   .WithMany(m => m.Attachments)
			   .HasForeignKey(a => a.MessageId)
			   .OnDelete(DeleteBehavior.Cascade);

		// Listing a klasse's attachments and reaping abandoned uploads both filter on MessageId.
		builder.HasIndex(a => new { a.TenantId, a.ClassId, a.MessageId });
	}
}
