using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;

namespace Skoleoverblikket.Api.Models;

/// <summary>
/// One post in a klasse's group chat — the in-platform replacement for the
/// per-klasse Facebook group. Membership is derived, not stored: every parent of
/// an enrolled student plus every teacher/aide on the klasse's schema can post.
/// Soft-deleted via <see cref="DeletedAt"/> so moderation keeps an audit trail.
/// </summary>
public sealed class ClassChatMessage : ITenantScoped, IEntityTypeConfiguration<ClassChatMessage>
{
	/// <summary>Max characters in a single post.</summary>
	public const int MaxBodyLength = 4000;

	public Guid Id { get; set; }
	public Guid TenantId { get; set; }

	public Guid ClassId { get; set; }
	public Class Class { get; set; } = null!;

	/// <summary>Which table <see cref="SenderId"/> points at — mirrors <see cref="ContactMessage"/>.</summary>
	public SenderType SenderType { get; set; }

	/// <summary>Parent.Id or Staff.Id depending on <see cref="SenderType"/>.</summary>
	public Guid SenderId { get; set; }

	[MaxLength(MaxBodyLength)]
	public required string Body { get; set; }

	public ICollection<ClassChatAttachment> Attachments { get; set; } = [];

	public DateTimeOffset SentAt { get; set; }

	/// <summary>Set when the sender or a moderator deletes the post. Null = visible.</summary>
	public DateTimeOffset? DeletedAt { get; set; }

	public void Configure(EntityTypeBuilder<ClassChatMessage> builder)
	{
		builder.HasOne(m => m.Class)
			   .WithMany()
			   .HasForeignKey(m => m.ClassId)
			   .OnDelete(DeleteBehavior.Cascade);

		// The thread view pages by SentAt within one klasse.
		builder.HasIndex(m => new { m.TenantId, m.ClassId, m.SentAt });
	}
}
