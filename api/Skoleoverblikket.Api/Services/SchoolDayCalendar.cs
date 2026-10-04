using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// Which dates are skoledage: weekdays not covered by a Ferie, Lukkedag or Arbejdsdag calendar entry
/// (recurrence and excluded dates respected). Begivenhed is still a school day. Also holds the date
/// arithmetic absence rules are built on — Danish "today", calendar quarters and school years.
/// </summary>
public sealed class SchoolDayCalendar
{
	private static readonly TimeZoneInfo DanishTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");

	private readonly HashSet<DateOnly> _closedDays;

	private SchoolDayCalendar(HashSet<DateOnly> closedDays) => _closedDays = closedDays;

	/// <summary>Today's date in Denmark — absence is counted in Danish school days, not UTC days.</summary>
	public static DateOnly Today() => DanishDate(DateTimeOffset.UtcNow);

	public static DateOnly DanishDate(DateTimeOffset instant) =>
		DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, DanishTimeZone).DateTime);

	public static TimeOnly DanishTime(DateTimeOffset instant) =>
		TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, DanishTimeZone).DateTime);

	/// <summary>First day of the calendar quarter (Jan–Mar, Apr–Jun, Jul–Sep, Oct–Dec) containing <paramref name="date"/>.</summary>
	public static DateOnly QuarterStart(DateOnly date) => new(date.Year, (date.Month - 1) / 3 * 3 + 1, 1);

	public static DateOnly QuarterStart(int year, int quarter) => new(year, (quarter - 1) * 3 + 1, 1);

	public static DateOnly QuarterEnd(DateOnly quarterStart) => quarterStart.AddMonths(3).AddDays(-1);

	public static int QuarterNumber(DateOnly date) => (date.Month - 1) / 3 + 1;

	/// <summary>The calendar year a school year starts in. The boundary is 1 August: 2025 = skoleåret 2025/26.</summary>
	public static int SchoolYearStart(DateOnly date) => date.Month >= 8 ? date.Year : date.Year - 1;

	public static DateOnly SchoolYearFirstDay(int schoolYearStart) => new(schoolYearStart, 8, 1);

	public static DateOnly SchoolYearLastDay(int schoolYearStart) => new(schoolYearStart + 1, 7, 31);

	/// <summary>"2025/26"</summary>
	public static string SchoolYearLabel(int schoolYearStart) => $"{schoolYearStart}/{(schoolYearStart + 1) % 100:00}";

	/// <summary>Loads the non-school calendar entries that can affect [<paramref name="from"/>, <paramref name="to"/>].</summary>
	public static async Task<SchoolDayCalendar> LoadAsync(
		AppDbContext db, DateOnly from, DateOnly to, CancellationToken cancellationToken)
	{
		var entries = await db.CalendarEntries
			.AsNoTracking()
			.Where(e => e.Type == CalendarEntryType.Ferie
					 || e.Type == CalendarEntryType.Lukkedag
					 || e.Type == CalendarEntryType.Arbejdsdag)
			.Where(e => e.StartDate <= to && (e.EndDate >= from || e.RecurrenceRule != null))
			.ToListAsync(cancellationToken);

		return FromEntries(entries, from, to);
	}

	public static SchoolDayCalendar FromEntries(IEnumerable<CalendarEntry> entries, DateOnly from, DateOnly to)
	{
		var closed = new HashSet<DateOnly>();

		foreach (var entry in entries)
		{
			if (entry.Type is not (CalendarEntryType.Ferie or CalendarEntryType.Lukkedag or CalendarEntryType.Arbejdsdag))
			{
				continue;
			}

			var dto = new CalendarController.CalendarEntryDto(
				entry.Id, entry.Type, entry.Title, entry.StartDate, entry.EndDate,
				entry.RecurrenceRule, entry.RecurrenceEnd, entry.ExcludedDates);

			var occurrences = new List<(DateOnly Start, DateOnly End)> { (entry.StartDate, entry.EndDate) };
			if (entry.RecurrenceRule is not null)
			{
				var expansionEnd = entry.RecurrenceEnd ?? to;
				occurrences.AddRange(CalendarController
					.ExpandRecurrencePublic(dto, expansionEnd, from, to)
					.Select(o => (o.StartDate, o.EndDate)));
			}

			foreach (var (start, end) in occurrences)
			{
				var first = start < from ? from : start;
				var last = end > to ? to : end;
				for (var day = first; day <= last; day = day.AddDays(1))
				{
					closed.Add(day);
				}
			}
		}

		return new SchoolDayCalendar(closed);
	}

	public bool IsSchoolDay(DateOnly date) =>
		date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !_closedDays.Contains(date);

	/// <summary>School days in [<paramref name="from"/>, <paramref name="to"/>], inclusive. Zero when the range is empty.</summary>
	public int CountSchoolDays(DateOnly from, DateOnly to)
	{
		var count = 0;
		for (var day = from; day <= to; day = day.AddDays(1))
		{
			if (IsSchoolDay(day))
			{
				count++;
			}
		}

		return count;
	}
}
