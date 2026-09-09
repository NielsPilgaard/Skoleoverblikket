using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Storage;

namespace Skoleoverblikket.Api.Models;

/// <summary>Fil — a file uploaded by staff or admin, optionally linked to a course.</summary>
public sealed class SchoolFile : ITenantScoped, IStoredFile, IEntityTypeConfiguration<SchoolFile>
{
	public Guid Id { get; set; }
	public Guid TenantId { get; set; }

	[StringLength(500, MinimumLength = 1)]
	public required string FileName { get; set; }

	[StringLength(200)]
	public required string ContentType { get; set; }

	public long SizeBytes { get; set; }

	/// <summary>Key in object storage.</summary>
	[StringLength(1000)]
	public required string StorageKey { get; set; }

	/// <summary>Public URL returned by object storage.</summary>
	[StringLength(2000)]
	public required string Url { get; set; }

	/// <summary>Optional link to a course (fag).</summary>
	public Guid? CourseId { get; set; }
	public Course? Course { get; set; }

	/// <summary>Optional folder this file lives in. Null = root.</summary>
	public Guid? FolderId { get; set; }
	public SchoolFileFolder? Folder { get; set; }

	[StringLength(200)]
	public required string UploadedBy { get; set; }

	public DateTimeOffset UploadedAt { get; init; } = DateTimeOffset.UtcNow;

	public void Configure(EntityTypeBuilder<SchoolFile> builder)
	{
		builder.Property(f => f.UploadedAt).HasDefaultValueSql("now()");
		builder.HasOne(f => f.Course)
			   .WithMany()
			   .HasForeignKey(f => f.CourseId)
			   .OnDelete(DeleteBehavior.SetNull);
		builder.HasOne(f => f.Folder)
			   .WithMany(fo => fo.Files)
			   .HasForeignKey(f => f.FolderId)
			   .OnDelete(DeleteBehavior.SetNull);
	}
}
