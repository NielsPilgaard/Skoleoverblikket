using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Storage;

namespace Skoleoverblikket.Api.Models;

public sealed class BoardFileFolder : ITenantScoped, IFileFolder, IEntityTypeConfiguration<BoardFileFolder>
{
	public Guid Id { get; set; }
	public Guid TenantId { get; set; }

	[StringLength(200, MinimumLength = 1)]
	public required string Name { get; set; }

	public Guid? ParentId { get; set; }
	public BoardFileFolder? Parent { get; set; }

	public ICollection<BoardFileFolder> Children { get; set; } = [];
	public ICollection<BoardFile> Files { get; set; } = [];

	public DateTimeOffset CreatedAt { get; init; }

	public void Configure(EntityTypeBuilder<BoardFileFolder> builder)
	{
		builder.Property(f => f.CreatedAt).HasDefaultValueSql("now()");
		builder.HasOne(f => f.Parent)
			   .WithMany(f => f.Children)
			   .HasForeignKey(f => f.ParentId)
			   .OnDelete(DeleteBehavior.Cascade);
	}
}
