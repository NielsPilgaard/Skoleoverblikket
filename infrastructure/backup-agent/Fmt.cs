using System.Globalization;

namespace Skoleoverblikket.BackupAgent;

/// <summary>Danish formatting for the console. Times are stored in UTC and shown in Europe/Copenhagen.</summary>
public static class Fmt
{
	public static readonly CultureInfo Danish = CultureInfo.GetCultureInfo("da-DK");
	public static readonly TimeZoneInfo Copenhagen = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");

	public static DateTimeOffset Local(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Copenhagen);

	public static DateOnly LocalDate(DateTimeOffset at) => DateOnly.FromDateTime(Local(at).DateTime);

	public static string DateTime(DateTimeOffset? at) =>
		at is { } value ? Local(value).ToString("d. MMM yyyy 'kl.' HH:mm", Danish) : "—";

	public static string DateTimeSeconds(DateTimeOffset? at) =>
		at is { } value ? Local(value).ToString("d. MMM yyyy 'kl.' HH:mm:ss", Danish) : "—";

	public static string Date(DateTimeOffset? at) =>
		at is { } value ? Local(value).ToString("d. MMM yyyy", Danish) : "—";

	public static string Ago(DateTimeOffset? at, DateTimeOffset now)
	{
		if (at is not { } value)
		{
			return "aldrig";
		}

		var span = now - value;
		return span.TotalMinutes switch
		{
			< 1 => "lige nu",
			< 90 => $"for {(int)span.TotalMinutes} min siden",
			< 48 * 60 => $"for {(int)span.TotalHours} t siden",
			_ => $"for {(int)span.TotalDays} dage siden",
		};
	}

	public static string Duration(double seconds) => seconds switch
	{
		< 90 => $"{seconds:0} s",
		< 90 * 60 => $"{seconds / 60:0.#} min",
		_ => $"{seconds / 3600:0.#} t",
	};

	public static string Bytes(long? bytes) => bytes switch
	{
		null => "—",
		< 1024 => $"{bytes} B",
		< 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
		< 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
		_ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB",
	};

	/// <summary>Parses a local Copenhagen time from an &lt;input type="datetime-local"&gt;.</summary>
	public static DateTimeOffset? ParseLocal(string? value)
	{
		if (!System.DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
		{
			return null;
		}

		var unspecified = System.DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
		return new DateTimeOffset(unspecified, Copenhagen.GetUtcOffset(unspecified));
	}

	public static string InputValue(DateTimeOffset at) => Local(at).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

	/// <summary>A target pgBackRest and Postgres both read unambiguously.</summary>
	public static string PgTimestamp(DateTimeOffset at) =>
		at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.ffffff'+00'", CultureInfo.InvariantCulture);
}
