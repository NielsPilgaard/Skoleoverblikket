using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Skoleoverblikket.Api.Models;

/// <summary>
/// A school that <see cref="Services.SchoolDeletionService"/> deleted. Kept while the school can
/// still be in a database backup, so the backup agent's console can show which schools a restore
/// would bring back. Not tenant-scoped: the school it names no longer exists. Holds the school's
/// name and dates only, no personal data. The backup agent reads this table over its socket and
/// mirrors it to its ops bucket, because a restore rewinds the table but not the bucket.
/// </summary>
[Index(nameof(DeletedAt))]
public sealed class SchoolDeletionRecord
{
	/// <summary>How long a deleted school can still be restored from a backup (BACKUP_RETENTION_DAYS in dataProcessing.ts).</summary>
	public static readonly TimeSpan BackupRetention = TimeSpan.FromDays(14);

	public Guid Id { get; set; }

	public Guid SchoolId { get; set; }

	[StringLength(200)]
	public required string SchoolName { get; set; }

	public DateTimeOffset DeletedAt { get; set; }
}
