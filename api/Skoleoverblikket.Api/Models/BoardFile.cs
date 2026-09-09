using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Storage;

namespace Skoleoverblikket.Api.Models;

public sealed class BoardFile : ITenantScoped, IStoredFile, IEntityTypeConfiguration<BoardFile>
{
	public Guid Id { get; set; }
	public Guid TenantId { get; set; }

	[StringLength(500, MinimumLength = 1)]
	public required string FileName { get; set; }

	[StringLength(200)]
	public required string ContentType { get; set; }

	public long SizeBytes { get; set; }

	[StringLength(1000)]
	public required string StorageKey { get; set; }

	[StringLength(2000)]
	public required string Url { get; set; }

	public Guid? FolderId { get; set; }
	public BoardFileFolder? Folder { get; set; }

	[StringLength(200)]
	public required string UploadedBy { get; set; }

	public DateTimeOffset UploadedAt { get; init; }

	public void Configure(EntityTypeBuilder<BoardFile> builder)
	{
		builder.Property(f => f.UploadedAt).HasDefaultValueSql("now()");
		builder.HasOne(f => f.Folder)
			   .WithMany(fo => fo.Files)
			   .HasForeignKey(f => f.FolderId)
			   .OnDelete(DeleteBehavior.SetNull);
	}
}
