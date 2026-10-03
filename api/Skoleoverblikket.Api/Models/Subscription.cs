using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skoleoverblikket.Api.Models;

public enum SubscriptionStatus
{
	Trialing,
	Active,
	PastDue,
	Canceled,
	Unpaid,
}

/// <summary>
/// Stripe subscription record for a school. Not tenant-scoped (no global query filter) —
/// accessed directly by SchoolId.
/// </summary>
[Index(nameof(StripeSubscriptionId))]
[Index(nameof(StripeCustomerId))]
[Index(nameof(SchoolId), IsUnique = true)]
public sealed class Subscription : IEntityTypeConfiguration<Subscription>
{
	public Guid Id { get; set; }

	public Guid SchoolId { get; set; }

	public SubscriptionStatus Status { get; set; }

	public BillingInterval Interval { get; set; }

	/// <summary>Stripe customer ID (cus_xxx)</summary>
	public string? StripeCustomerId { get; set; }

	/// <summary>Stripe subscription ID (sub_xxx)</summary>
	public string? StripeSubscriptionId { get; set; }

	/// <summary>When the current billing period ends (null during trial)</summary>
	public DateTimeOffset? CurrentPeriodEnd { get; set; }

	/// <summary>When the trial ends (30 days from school creation)</summary>
	public DateTimeOffset TrialEnd { get; set; }

	/// <summary>
	/// When the subscription became canceled. The school's data is permanently deleted
	/// <see cref="Services.SchoolDeletionService.RetentionPeriod"/> after this. Cleared when the
	/// school subscribes again.
	/// </summary>
	public DateTimeOffset? CanceledAt { get; set; }

	/// <summary>When admins were emailed that the school's data is about to be deleted. Cleared with <see cref="CanceledAt"/>.</summary>
	public DateTimeOffset? DeletionWarningSentAt { get; set; }

	public DateTimeOffset CreatedAt { get; init; }
	public DateTimeOffset UpdatedAt { get; set; }

	public ICollection<SubscriptionModuleItem> ActiveModules { get; set; } = [];

	public void Configure(EntityTypeBuilder<Subscription> builder)
	{
		builder.Property(s => s.CreatedAt).HasDefaultValueSql("now()");
		builder.Property(s => s.UpdatedAt)
			.HasDefaultValueSql("now()")
			.ValueGeneratedOnAddOrUpdate();
	}
}
