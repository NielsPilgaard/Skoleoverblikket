using System.Text;
using Skoleoverblikket.Api.Controllers;

namespace Skoleoverblikket.Api.Services;

public static class IcsBuilder
{
	public static byte[] Build(IEnumerable<CalendarController.CalendarEntryDto> entries)
	{
		var sb = new StringBuilder();
		sb.AppendLine("BEGIN:VCALENDAR");
		sb.AppendLine("VERSION:2.0");
		sb.AppendLine("PRODID:-//Skoleoverblikket//Skoleoverblikket//DA");
		sb.AppendLine("CALSCALE:GREGORIAN");
		sb.AppendLine("METHOD:PUBLISH");

		foreach (var entry in entries)
		{
			var occurrences = new List<(DateOnly Start, DateOnly End)>();
			if (!CalendarController.IsBaseOccurrenceExcluded(entry))
			{
				occurrences.Add((entry.StartDate, entry.EndDate));
			}

			if (entry.RecurrenceRule is not null)
			{
				var expansionEnd = entry.RecurrenceEnd ?? entry.StartDate.AddYears(2);
				var expanded = CalendarController.ExpandRecurrencePublic(entry, expansionEnd, filterStart: null, filterEnd: null);
				occurrences.AddRange(expanded.Select(o => (o.StartDate, o.EndDate)));
			}

			foreach (var (start, end) in occurrences)
			{
				sb.AppendLine("BEGIN:VEVENT");
				sb.AppendLine($"UID:{entry.Id}-{start:yyyyMMdd}@skoleoverblikket");
				sb.AppendLine($"SUMMARY:{EscapeText(entry.Title)}");
				sb.AppendLine($"DTSTART;VALUE=DATE:{start:yyyyMMdd}");
				sb.AppendLine($"DTEND;VALUE=DATE:{end.AddDays(1):yyyyMMdd}");
				sb.AppendLine("END:VEVENT");
			}
		}

		sb.AppendLine("END:VCALENDAR");
		return Encoding.UTF8.GetBytes(sb.ToString());
	}

	private static string EscapeText(string text) =>
		text.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
}
