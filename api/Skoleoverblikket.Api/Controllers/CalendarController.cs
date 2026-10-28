using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/calendar")]
[Authorize]
public sealed class CalendarController(AppDbContext db, ITenantContext tenant) : ControllerBase
{
	public record CalendarEntryDto(Guid Id, CalendarEntryType Type, string Title, DateOnly StartDate, DateOnly EndDate, string? RecurrenceRule = null, DateOnly? RecurrenceEnd = null, string? ExcludedDates = null);
	public record CreateCalendarEntryRequest([Required][MinLength(1)] string Title, CalendarEntryType Type, DateOnly StartDate, DateOnly EndDate, string? RecurrenceRule = null, DateOnly? RecurrenceEnd = null);
	public record UpdateCalendarEntryRequest([Required][MinLength(1)] string Title, CalendarEntryType Type, DateOnly StartDate, DateOnly EndDate, string? RecurrenceRule = null, DateOnly? RecurrenceEnd = null);
	public record DefaultHolidayDto(string Title, CalendarEntryType Type, DateOnly StartDate, DateOnly EndDate);

	[HttpGet]
	public async Task<ActionResult<List<CalendarEntryDto>>> GetAll([FromQuery] int? year, CancellationToken cancellationToken)
	{
		var query = db.CalendarEntries.AsNoTracking();

		if (year.HasValue)
		{
			// Include base entries that fall in the year, plus recurring entries whose range may produce occurrences in the year
			query = query.Where(e =>
				e.StartDate.Year == year.Value || e.EndDate.Year == year.Value ||
				(e.RecurrenceRule != null && (e.RecurrenceEnd == null || e.RecurrenceEnd.Value.Year >= year.Value) && e.StartDate.Year <= year.Value));
		}

		var rawEntries = await query
			.OrderBy(e => e.StartDate)
			.Select(e => new CalendarEntryDto(e.Id, e.Type, e.Title, e.StartDate, e.EndDate, e.RecurrenceRule, e.RecurrenceEnd, e.ExcludedDates))
			.ToListAsync(cancellationToken);

		var result = new List<CalendarEntryDto>(rawEntries.Count);
		var filterStart = year.HasValue ? new DateOnly(year.Value, 1, 1) : (DateOnly?)null;
		var filterEnd = year.HasValue ? new DateOnly(year.Value, 12, 31) : (DateOnly?)null;

		foreach (var entry in rawEntries)
		{
			result.Add(entry);
			if (entry.RecurrenceRule is not null)
			{
				var expansionEnd = entry.RecurrenceEnd ?? entry.StartDate.AddYears(2);
				var occurrences = ExpandRecurrence(entry, expansionEnd, filterStart, filterEnd);
				result.AddRange(occurrences);
			}
		}

		result.Sort((a, b) => a.StartDate.CompareTo(b.StartDate));
		return Ok(result);
	}

	[HttpGet("defaults")]
	public ActionResult<List<DefaultHolidayDto>> GetDefaults([FromQuery] int? year)
	{
		// year is the school start year (e.g. 2025 = 2025/2026)
		// Default to current school start year
		var today = DateOnly.FromDateTime(DateTime.Today);
		var targetYear = year ?? (today.Month >= 8 ? today.Year : today.Year - 1);
		var defaults = ComputeDefaultHolidays(targetYear);
		return Ok(defaults);
	}

	[HttpPost]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<CalendarEntryDto>> Create([FromBody] CreateCalendarEntryRequest req, CancellationToken cancellationToken)
	{
		if (req.StartDate > req.EndDate)
		{
			return Problem("StartDate skal være før eller lig EndDate", statusCode: 400);
		}

		var entry = new CalendarEntry
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			Type = req.Type,
			Title = req.Title,
			StartDate = req.StartDate,
			EndDate = req.EndDate,
			RecurrenceRule = req.RecurrenceRule,
			RecurrenceEnd = req.RecurrenceEnd,
		};
		db.CalendarEntries.Add(entry);
		await db.SaveChangesAsync(cancellationToken);
		return CreatedAtAction(nameof(GetAll), new CalendarEntryDto(entry.Id, entry.Type, entry.Title, entry.StartDate, entry.EndDate, entry.RecurrenceRule, entry.RecurrenceEnd, entry.ExcludedDates));
	}

	[HttpPut("{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<CalendarEntryDto>> Update(Guid id, [FromBody] UpdateCalendarEntryRequest req, CancellationToken cancellationToken)
	{
		if (req.StartDate > req.EndDate)
		{
			return Problem("StartDate skal være før eller lig EndDate", statusCode: 400);
		}

		var entry = await db.CalendarEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
		if (entry is null)
		{
			return NotFound();
		}

		entry.Type = req.Type;
		entry.Title = req.Title;
		entry.StartDate = req.StartDate;
		entry.EndDate = req.EndDate;
		entry.RecurrenceRule = req.RecurrenceRule;
		entry.RecurrenceEnd = req.RecurrenceEnd;
		await db.SaveChangesAsync(cancellationToken);
		return Ok(new CalendarEntryDto(entry.Id, entry.Type, entry.Title, entry.StartDate, entry.EndDate, entry.RecurrenceRule, entry.RecurrenceEnd, entry.ExcludedDates));
	}

	[HttpDelete("{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
	{
		var entry = await db.CalendarEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
		if (entry is null)
		{
			return NotFound();
		}

		db.CalendarEntries.Remove(entry);
		await db.SaveChangesAsync(cancellationToken);
		return NoContent();
	}

	// Exclude a single occurrence of a recurring event (adds date to ExcludedDates).
	[HttpDelete("{id:guid}/occurrences/{date}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult> DeleteOccurrence(Guid id, DateOnly date, CancellationToken cancellationToken)
	{
		var entry = await db.CalendarEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
		if (entry is null)
		{
			return NotFound();
		}

		if (entry.RecurrenceRule is null)
		{
			return Problem("Begivenheden gentages ikke.", statusCode: 400);
		}

		var existing = entry.ExcludedDates?
			.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.ToHashSet() ?? [];
		existing.Add(date.ToString("yyyy-MM-dd"));
		entry.ExcludedDates = string.Join(',', existing.OrderBy(d => d));
		await db.SaveChangesAsync(cancellationToken);
		return NoContent();
	}

	// Truncate a recurring event so it ends before the given date (delete this and all subsequent).
	[HttpDelete("{id:guid}/from/{date}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult> DeleteFrom(Guid id, DateOnly date, CancellationToken cancellationToken)
	{
		var entry = await db.CalendarEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
		if (entry is null)
		{
			return NotFound();
		}

		if (entry.RecurrenceRule is null)
		{
			return Problem("Begivenheden gentages ikke.", statusCode: 400);
		}

		// If the base occurrence itself is being cut, delete the entire entry.
		if (date <= entry.StartDate)
		{
			db.CalendarEntries.Remove(entry);
		}
		else
		{
			// Set RecurrenceEnd to one day before the cut date so occurrences from `date` onward are excluded.
			entry.RecurrenceEnd = date.AddDays(-1);
			// Remove any excluded dates that are now beyond the new end
			if (entry.ExcludedDates is not null)
			{
				var kept = entry.ExcludedDates
					.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
					.Where(d => DateOnly.TryParse(d, out var parsed) && parsed < date)
					.ToList();
				entry.ExcludedDates = kept.Count > 0 ? string.Join(',', kept) : null;
			}
		}

		await db.SaveChangesAsync(cancellationToken);
		return NoContent();
	}

	[HttpGet("export.ics")]
	public async Task<IActionResult> ExportIcs(CancellationToken cancellationToken)
	{
		var entries = await db.CalendarEntries
			.AsNoTracking()
			.OrderBy(e => e.StartDate)
			.Select(e => new CalendarEntryDto(e.Id, e.Type, e.Title, e.StartDate, e.EndDate, e.RecurrenceRule, e.RecurrenceEnd, e.ExcludedDates))
			.ToListAsync(cancellationToken);

		var bytes = IcsBuilder.Build(entries);
		Response.Headers.Append("Content-Disposition", "attachment; filename=\"skoleoverblikket-kalender.ics\"");
		return File(bytes, "text/calendar; charset=utf-8");
	}

	// Expands a recurring entry into additional occurrences after its base occurrence.
	// Returns only the extra occurrences (not the original). Each occurrence keeps the same Id.
	internal static List<CalendarEntryDto> ExpandRecurrencePublic(
		CalendarEntryDto entry,
		DateOnly expansionEnd,
		DateOnly? filterStart,
		DateOnly? filterEnd) => ExpandRecurrence(entry, expansionEnd, filterStart, filterEnd);

	private static List<CalendarEntryDto> ExpandRecurrence(
		CalendarEntryDto entry,
		DateOnly expansionEnd,
		DateOnly? filterStart,
		DateOnly? filterEnd)
	{
		var result = new List<CalendarEntryDto>();
		var rule = entry.RecurrenceRule ?? string.Empty;

		// Parse FREQ and INTERVAL from rule string, e.g. "FREQ=WEEKLY;INTERVAL=2"
		var freq = string.Empty;
		var interval = 1;
		foreach (var part in rule.Split(';'))
		{
			if (part.StartsWith("FREQ=", StringComparison.OrdinalIgnoreCase))
			{
				freq = part[5..].Trim().ToUpperInvariant();
			}
			else if (part.StartsWith("INTERVAL=", StringComparison.OrdinalIgnoreCase) &&
					 int.TryParse(part[9..].Trim(), out var parsed))
			{
				interval = parsed;
			}
		}

		// Treat BIWEEKLY as WEEKLY with interval 2
		if (freq == "BIWEEKLY")
		{
			freq = "WEEKLY";
			interval = 2;
		}

		if (freq is not ("WEEKLY" or "MONTHLY"))
		{
			return result;
		}

		var duration = entry.EndDate.DayNumber - entry.StartDate.DayNumber;

		var excluded = entry.ExcludedDates?
			.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(d => DateOnly.TryParse(d, out var pd) ? pd : (DateOnly?)null)
			.Where(d => d.HasValue)
			.Select(d => d!.Value)
			.ToHashSet() ?? [];

		// Each occurrence is computed from the start date, not the previous occurrence, so a monthly
		// event on the 31st clamps to Feb 28 once and returns to the 31st in March instead of drifting.
		for (var n = 1; n <= 500; n++)
		{
			var current = freq == "WEEKLY"
				? entry.StartDate.AddDays(7 * interval * n)
				: entry.StartDate.AddMonths(interval * n);

			if (current > expansionEnd)
			{
				break;
			}

			if (excluded.Contains(current))
			{
				continue;
			}

			var occEnd = DateOnly.FromDayNumber(current.DayNumber + duration);

			// Apply year filter clamping if requested
			if (filterEnd.HasValue && current > filterEnd.Value)
			{
				break;
			}

			if (filterStart.HasValue && occEnd < filterStart.Value)
			{
				continue;
			}

			result.Add(entry with { StartDate = current, EndDate = occEnd });
		}

		return result;
	}

	private static List<DefaultHolidayDto> ComputeDefaultHolidays(int year)
	{
		// year is treated as the school start year (e.g. 2025 = school year 2025/2026)
		var schoolStartYear = year;
		var schoolEndYear = schoolStartYear + 1;

		var easter = ComputeEaster(schoolEndYear);

		// Palm Sunday = Easter - 7, Easter Monday = Easter + 1
		var palmSunday = easter.AddDays(-7);
		var easterMonday = easter.AddDays(1);

		// Kristi Himmelfartsdag = Easter + 39
		var ascension = easter.AddDays(39);

		// Pinse: Whit Sunday = Easter + 49, Whit Monday = Easter + 50
		// Show Fri before + Mon as Ferie
		var whitFriday = easter.AddDays(48); // Friday before Whit Sunday
		var whitMonday = easter.AddDays(50);

		// Efterårsferie: ISO week 42 of school start year
		var efteraarStart = DateOnly.FromDateTime(ISOWeek.ToDateTime(schoolStartYear, 42, DayOfWeek.Monday));
		var efteraarEnd = efteraarStart.AddDays(4);

		// Juleferie: Dec 22 through Jan 2
		var juleStart = new DateOnly(schoolStartYear, 12, 22);
		var juleEnd = new DateOnly(schoolEndYear, 1, 2);

		// Vinterferie: ISO week 7 of school end year
		var vinterStart = DateOnly.FromDateTime(ISOWeek.ToDateTime(schoolEndYear, 7, DayOfWeek.Monday));
		var vinterEnd = vinterStart.AddDays(4);

		// Sommerferie: Jun 26 through Aug 7
		var sommerStart = new DateOnly(schoolEndYear, 6, 26);
		var sommerEnd = new DateOnly(schoolEndYear, 8, 7);

		// Grundlovsdag: June 5
		var grundlovsdag = new DateOnly(schoolEndYear, 6, 5);

		return
		[
			new DefaultHolidayDto("Efterårsferie", CalendarEntryType.Ferie, efteraarStart, efteraarEnd),
			new DefaultHolidayDto("Juleferie", CalendarEntryType.Ferie, juleStart, juleEnd),
			new DefaultHolidayDto("Vinterferie", CalendarEntryType.Ferie, vinterStart, vinterEnd),
			new DefaultHolidayDto("Påskeferie", CalendarEntryType.Ferie, palmSunday, easterMonday),
			new DefaultHolidayDto("Kristi Himmelfartsdag", CalendarEntryType.Lukkedag, ascension, ascension),
			new DefaultHolidayDto("Pinse", CalendarEntryType.Ferie, whitFriday, whitMonday),
			new DefaultHolidayDto("Grundlovsdag", CalendarEntryType.Lukkedag, grundlovsdag, grundlovsdag),
			new DefaultHolidayDto("Sommerferie", CalendarEntryType.Ferie, sommerStart, sommerEnd),
		];
	}

	private static DateOnly ComputeEaster(int year)
	{
		// Anonymous Gregorian algorithm
		var a = year % 19;
		var b = year / 100;
		var c = year % 100;
		var d = b / 4;
		var e = b % 4;
		var f = (b + 8) / 25;
		var g = (b - f + 1) / 3;
		var h = (19 * a + b - d - g + 15) % 30;
		var i = c / 4;
		var k = c % 4;
		var l = (32 + 2 * e + 2 * i - h - k) % 7;
		var m = (a + 11 * h + 22 * l) / 451;
		var month = (h + l - 7 * m + 114) / 31;
		var day = (h + l - 7 * m + 114) % 31 + 1;
		return new DateOnly(year, month, day);
	}
}
