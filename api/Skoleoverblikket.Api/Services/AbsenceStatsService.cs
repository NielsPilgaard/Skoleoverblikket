using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.Services;

public enum AbsenceFlag { None, TenPercent, FifteenPercent }

public sealed record QuarterStatsDto(
	int Year,
	int Quarter,
	DateOnly QuarterStart,
	DateOnly QuarterEnd,
	int SchoolDaysInQuarter,
	int SchoolDaysSoFar,
	IReadOnlyList<StudentQuarterStatsDto> Students,
	IReadOnlyList<ClassQuarterStatsDto> Classes,
	IReadOnlyList<WeekAbsenceDto> Weeks);

public sealed record StudentQuarterStatsDto(
	Guid StudentId,
	string StudentName,
	Guid ClassId,
	string ClassName,
	decimal IllnessDays,
	decimal LeaveDays,
	decimal UnauthorizedDays,
	decimal UnauthorizedPercent,
	AbsenceFlag Flag,
	DateTimeOffset? ParentsInformedAt);

public sealed record ClassQuarterStatsDto(
	Guid ClassId,
	string ClassName,
	int StudentCount,
	decimal IllnessDays,
	decimal LeaveDays,
	decimal UnauthorizedDays,
	decimal AbsencePercent,
	int FlaggedStudents);

public sealed record WeekAbsenceDto(DateOnly WeekStart, int IsoWeek, decimal IllnessDays, decimal LeaveDays, decimal UnauthorizedDays);

/// <summary>
/// Read-only absence arithmetic: per-student and per-class totals for a calendar quarter, the 10% /
/// 15% ulovligt flags, and the downloads built on them. Writes live in <see cref="AbsenceService"/>.
///
/// Counting rules (BEK 1063/2019): only school days count; a half-day record counts 0.5; pending
/// and rejected leave requests count as nothing. When several records cover the same student and
/// day, the school's own full-day registration wins over a parent's report, which wins over a
/// half day — so a day is never counted twice. The ulovligt percentage is taken against all school
/// days in the quarter, so it only ever rises during the quarter and a flag means the threshold is
/// already reached for the quarter as a whole.
/// </summary>
public sealed class AbsenceStatsService(AppDbContext db)
{
	public const decimal WarningShare = 0.10m;
	public const decimal CriticalShare = 0.15m;

	private sealed record RecordRow(
		Guid StudentId, DateOnly Date, DateOnly? EndDate, bool HalfDay, AbsenceCategory Category, bool StaffRegistered);

	private sealed class Tally
	{
		public decimal Illness;
		public decimal Leave;
		public decimal Unauthorized;

		public void Add(AbsenceCategory category, decimal amount)
		{
			switch (category)
			{
				case AbsenceCategory.Illness: Illness += amount; break;
				case AbsenceCategory.ExtraordinaryLeave: Leave += amount; break;
				case AbsenceCategory.Unauthorized: Unauthorized += amount; break;
			}
		}
	}

	public async Task<QuarterStatsDto> GetQuarterStatsAsync(
		int year, int quarter, Guid? classId, IReadOnlyCollection<Guid>? allowedClassIds, CancellationToken cancellationToken)
	{
		var quarterStart = SchoolDayCalendar.QuarterStart(year, quarter);
		var quarterEnd = SchoolDayCalendar.QuarterEnd(quarterStart);
		var today = SchoolDayCalendar.Today();
		var countedTo = quarterEnd < today ? quarterEnd : today;

		var calendar = await SchoolDayCalendar.LoadAsync(db, quarterStart, quarterEnd, cancellationToken);
		var schoolDaysInQuarter = calendar.CountSchoolDays(quarterStart, quarterEnd);
		var schoolDaysSoFar = calendar.CountSchoolDays(quarterStart, countedTo);

		var studentsQuery = db.Students.AsNoTracking();
		if (classId.HasValue)
		{
			studentsQuery = studentsQuery.Where(s => s.ClassId == classId.Value);
		}

		if (allowedClassIds is not null)
		{
			studentsQuery = studentsQuery.Where(s => allowedClassIds.Contains(s.ClassId));
		}

		var students = await studentsQuery
			.Select(s => new { s.Id, s.Name, s.ClassId, ClassName = s.Class.Name })
			.ToListAsync(cancellationToken);
		var studentIds = students.Select(s => s.Id).ToList();

		var rows = await LoadRowsAsync(studentIds, quarterStart, countedTo, cancellationToken);
		var days = ResolveDays(rows, calendar, quarterStart, countedTo);

		var followUps = await db.AbsenceFollowUps
			.AsNoTracking()
			.Where(f => f.QuarterStart == quarterStart && studentIds.Contains(f.StudentId))
			.ToDictionaryAsync(f => f.StudentId, f => f.ParentsInformedAt, cancellationToken);

		var studentStats = students
			.Select(s =>
			{
				var tally = Sum(days.GetValueOrDefault(s.Id));
				var percent = Percent(tally.Unauthorized, schoolDaysInQuarter);
				return new StudentQuarterStatsDto(
					s.Id, s.Name, s.ClassId, s.ClassName,
					tally.Illness, tally.Leave, tally.Unauthorized,
					percent, FlagFor(tally.Unauthorized, schoolDaysInQuarter),
					followUps.GetValueOrDefault(s.Id));
			})
			.OrderByDescending(s => s.UnauthorizedDays)
			.ThenByDescending(s => s.IllnessDays + s.LeaveDays)
			.ThenBy(s => s.ClassName)
			.ThenBy(s => s.StudentName)
			.ToList();

		var classStats = studentStats
			.GroupBy(s => new { s.ClassId, s.ClassName })
			.Select(g =>
			{
				var studentCount = g.Count();
				var total = g.Sum(s => s.IllnessDays + s.LeaveDays + s.UnauthorizedDays);
				return new ClassQuarterStatsDto(
					g.Key.ClassId, g.Key.ClassName, studentCount,
					g.Sum(s => s.IllnessDays), g.Sum(s => s.LeaveDays), g.Sum(s => s.UnauthorizedDays),
					Percent(total, studentCount * schoolDaysSoFar),
					g.Count(s => s.Flag != AbsenceFlag.None));
			})
			.OrderBy(c => c.ClassName, StringComparer.Create(new CultureInfo("da-DK"), CompareOptions.None))
			.ToList();

		var weeks = new List<WeekAbsenceDto>();
		var weekStart = quarterStart.AddDays(-(((int)quarterStart.DayOfWeek + 6) % 7));
		for (; weekStart <= countedTo; weekStart = weekStart.AddDays(7))
		{
			var weekEnd = weekStart.AddDays(6);
			var tally = new Tally();
			foreach (var studentDays in days.Values)
			{
				foreach (var (day, entry) in studentDays)
				{
					if (day >= weekStart && day <= weekEnd)
					{
						tally.Add(entry.Category, entry.Amount);
					}
				}
			}

			weeks.Add(new WeekAbsenceDto(
				weekStart, ISOWeek.GetWeekOfYear(weekStart.ToDateTime(TimeOnly.MinValue)),
				tally.Illness, tally.Leave, tally.Unauthorized));
		}

		return new QuarterStatsDto(
			year, quarter, quarterStart, quarterEnd, schoolDaysInQuarter, schoolDaysSoFar,
			studentStats, classStats, weeks);
	}

	/// <summary>
	/// Ulovligt fravær for one student in the quarter starting <paramref name="quarterStart"/>, and the
	/// number of school days in that whole quarter. Used to decide the 10% follow-up notification.
	/// </summary>
	public async Task<(decimal UnauthorizedDays, int SchoolDaysInQuarter)> GetUnauthorizedShareAsync(
		Guid studentId, DateOnly quarterStart, CancellationToken cancellationToken)
	{
		var quarterEnd = SchoolDayCalendar.QuarterEnd(quarterStart);
		var calendar = await SchoolDayCalendar.LoadAsync(db, quarterStart, quarterEnd, cancellationToken);
		var rows = await LoadRowsAsync([studentId], quarterStart, quarterEnd, cancellationToken);
		var days = ResolveDays(rows, calendar, quarterStart, quarterEnd);
		return (Sum(days.GetValueOrDefault(studentId)).Unauthorized, calendar.CountSchoolDays(quarterStart, quarterEnd));
	}

	public static AbsenceFlag FlagFor(decimal unauthorizedDays, int schoolDaysInQuarter)
	{
		if (schoolDaysInQuarter == 0)
		{
			return AbsenceFlag.None;
		}

		var share = unauthorizedDays / schoolDaysInQuarter;
		return share >= CriticalShare ? AbsenceFlag.FifteenPercent
			: share >= WarningShare ? AbsenceFlag.TenPercent
			: AbsenceFlag.None;
	}

	/// <summary>
	/// The full absence register for one school year as an Excel workbook: one row per record, plus
	/// a per-student summary per calendar quarter.
	/// </summary>
	public async Task<XLWorkbook> BuildSchoolYearWorkbookAsync(int schoolYearStart, CancellationToken cancellationToken)
	{
		var first = SchoolDayCalendar.SchoolYearFirstDay(schoolYearStart);
		var last = SchoolDayCalendar.SchoolYearLastDay(schoolYearStart);

		var records = await db.AbsenceReports
			.AsNoTracking()
			.Where(a => a.Date <= last && (a.EndDate ?? a.Date) >= first)
			.Where(a => a.LeaveStatus == null || a.LeaveStatus != LeaveStatus.Rejected)
			.OrderBy(a => a.Date)
			.ThenBy(a => a.Student.Class.Name)
			.ThenBy(a => a.Student.Name)
			.Select(a => new
			{
				StudentName = a.Student.Name,
				ClassName = a.Student.Class.Name,
				a.Date,
				a.EndDate,
				a.HalfDay,
				a.Category,
				a.LeaveStatus,
				Source = a.RegisteredByStaffId != null ? "Personale" : "Forælder",
				a.Reason,
			})
			.ToListAsync(cancellationToken);

		var wb = new XLWorkbook();
		var sheet = wb.Worksheets.Add("Fravær");
		string[] headers = ["Elev", "Klasse", "Fra", "Til", "Halv dag", "Kategori", "Status", "Registreret af", "Årsag"];
		for (var i = 0; i < headers.Length; i++)
		{
			sheet.Cell(1, i + 1).Value = headers[i];
		}

		ExcelReportBuilder.StyleHeader(sheet.Row(1));

		var row = 2;
		foreach (var r in records)
		{
			sheet.Cell(row, 1).Value = r.StudentName;
			sheet.Cell(row, 2).Value = r.ClassName;
			sheet.Cell(row, 3).Value = r.Date.ToDateTime(TimeOnly.MinValue);
			sheet.Cell(row, 3).Style.DateFormat.Format = "dd-mm-yyyy";
			sheet.Cell(row, 4).Value = (r.EndDate ?? r.Date).ToDateTime(TimeOnly.MinValue);
			sheet.Cell(row, 4).Style.DateFormat.Format = "dd-mm-yyyy";
			sheet.Cell(row, 5).Value = r.HalfDay ? "Ja" : "";
			sheet.Cell(row, 6).Value = CategoryLabel(r.Category);
			sheet.Cell(row, 7).Value = r.LeaveStatus == LeaveStatus.Pending ? "Afventer godkendelse" : "";
			sheet.Cell(row, 8).Value = r.Source;
			sheet.Cell(row, 9).Value = r.Reason ?? "";
			row++;
		}

		sheet.Columns().AdjustToContents();

		var summary = wb.Worksheets.Add("Opsummering");
		string[] summaryHeaders = ["Kvartal", "Elev", "Klasse", "Skoledage", "Sygdom", "Ekstraordinær frihed", "Ulovligt fravær", "Ulovligt %"];
		for (var i = 0; i < summaryHeaders.Length; i++)
		{
			summary.Cell(1, i + 1).Value = summaryHeaders[i];
		}

		ExcelReportBuilder.StyleHeader(summary.Row(1));

		var today = SchoolDayCalendar.Today();
		var summaryRow = 2;
		for (var quarterStart = SchoolDayCalendar.QuarterStart(first); quarterStart <= last; quarterStart = quarterStart.AddMonths(3))
		{
			var from = quarterStart < first ? first : quarterStart;
			if (from > today)
			{
				break;
			}

			var stats = await GetQuarterStatsAsync(
				quarterStart.Year, SchoolDayCalendar.QuarterNumber(quarterStart), null, null, cancellationToken);
			var label = $"{SchoolDayCalendar.QuarterNumber(quarterStart)}. kvartal {quarterStart.Year}";

			// A quarter that straddles the school-year boundary (Jul–Sep) is reported whole: the
			// legal percentage is per calendar quarter, not per school year.
			foreach (var s in stats.Students.OrderBy(s => s.ClassName).ThenBy(s => s.StudentName))
			{
				summary.Cell(summaryRow, 1).Value = label;
				summary.Cell(summaryRow, 2).Value = s.StudentName;
				summary.Cell(summaryRow, 3).Value = s.ClassName;
				summary.Cell(summaryRow, 4).Value = stats.SchoolDaysInQuarter;
				summary.Cell(summaryRow, 5).Value = s.IllnessDays;
				summary.Cell(summaryRow, 6).Value = s.LeaveDays;
				summary.Cell(summaryRow, 7).Value = s.UnauthorizedDays;
				summary.Cell(summaryRow, 8).Value = s.UnauthorizedPercent;
				summaryRow++;
			}
		}

		summary.Columns().AdjustToContents();
		return wb;
	}

	/// <summary>
	/// Students at or above 15% ulovligt fravær in a quarter, as a semicolon CSV that opens directly in
	/// Danish Excel. For the principal's own notification to the kommune — we send nothing ourselves.
	/// </summary>
	public async Task<byte[]> BuildFlaggedCsvAsync(int year, int quarter, CancellationToken cancellationToken)
	{
		var stats = await GetQuarterStatsAsync(year, quarter, null, null, cancellationToken);
		var sb = new StringBuilder();
		sb.AppendLine("Elev;Klasse;Ulovligt fravær (dage);Skoledage i kvartalet;Ulovligt %;Forældre orienteret");
		foreach (var s in stats.Students.Where(s => s.Flag == AbsenceFlag.FifteenPercent).OrderBy(s => s.ClassName).ThenBy(s => s.StudentName))
		{
			sb.Append(Csv(s.StudentName)).Append(';')
				.Append(Csv(s.ClassName)).Append(';')
				.Append(s.UnauthorizedDays.ToString("0.#", CultureInfo.GetCultureInfo("da-DK"))).Append(';')
				.Append(stats.SchoolDaysInQuarter).Append(';')
				.Append(s.UnauthorizedPercent.ToString("0.#", CultureInfo.GetCultureInfo("da-DK"))).Append(';')
				.Append(s.ParentsInformedAt is { } informed ? informed.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) : "")
				.AppendLine();
		}

		return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
	}

	public static string CategoryLabel(AbsenceCategory category) => category switch
	{
		AbsenceCategory.Illness => "Sygdom",
		AbsenceCategory.ExtraordinaryLeave => "Ekstraordinær frihed",
		AbsenceCategory.Unauthorized => "Ulovligt fravær",
		_ => category.ToString(),
	};

	private async Task<List<RecordRow>> LoadRowsAsync(
		List<Guid> studentIds, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
		await db.AbsenceReports
			.AsNoTracking()
			.Where(a => studentIds.Contains(a.StudentId))
			.Where(a => a.Date <= to && (a.EndDate ?? a.Date) >= from)
			.Where(a => a.LeaveStatus == null || a.LeaveStatus == LeaveStatus.Approved)
			.Select(a => new RecordRow(a.StudentId, a.Date, a.EndDate, a.HalfDay, a.Category, a.RegisteredByStaffId != null))
			.ToListAsync(cancellationToken);

	/// <summary>Per student, per school day: the one category that counts and its amount (1 or 0.5).</summary>
	private static Dictionary<Guid, Dictionary<DateOnly, (AbsenceCategory Category, decimal Amount)>> ResolveDays(
		List<RecordRow> rows, SchoolDayCalendar calendar, DateOnly from, DateOnly to)
	{
		var result = new Dictionary<Guid, Dictionary<DateOnly, (AbsenceCategory Category, decimal Amount, int Rank)>>();

		foreach (var row in rows)
		{
			// Approved leave beats a staff mark made while the request was still pending.
			var rank = row.HalfDay ? 1
				: row.Category == AbsenceCategory.ExtraordinaryLeave && !row.StaffRegistered ? 4
				: row.StaffRegistered ? 3
				: 2;
			var amount = row.HalfDay ? 0.5m : 1m;
			var first = row.Date < from ? from : row.Date;
			var lastDay = row.HalfDay ? row.Date : row.EndDate ?? row.Date;
			var last = lastDay > to ? to : lastDay;

			if (!result.TryGetValue(row.StudentId, out var studentDays))
			{
				studentDays = [];
				result[row.StudentId] = studentDays;
			}

			for (var day = first; day <= last; day = day.AddDays(1))
			{
				if (!calendar.IsSchoolDay(day))
				{
					continue;
				}

				if (!studentDays.TryGetValue(day, out var existing) || existing.Rank < rank)
				{
					studentDays[day] = (row.Category, amount, rank);
				}
			}
		}

		return result.ToDictionary(
			kv => kv.Key,
			kv => kv.Value.ToDictionary(d => d.Key, d => (d.Value.Category, d.Value.Amount)));
	}

	private static Tally Sum(Dictionary<DateOnly, (AbsenceCategory Category, decimal Amount)>? days)
	{
		var tally = new Tally();
		if (days is null)
		{
			return tally;
		}

		foreach (var (category, amount) in days.Values)
		{
			tally.Add(category, amount);
		}

		// Absence is always rounded up to the nearest half day.
		tally.Illness = RoundUpToHalf(tally.Illness);
		tally.Leave = RoundUpToHalf(tally.Leave);
		tally.Unauthorized = RoundUpToHalf(tally.Unauthorized);
		return tally;
	}

	private static decimal RoundUpToHalf(decimal days) => Math.Ceiling(days * 2) / 2;

	private static decimal Percent(decimal part, int whole) =>
		whole == 0 ? 0 : Math.Round(part / whole * 100, 1, MidpointRounding.AwayFromZero);

	private static string Csv(string value) =>
		value.Contains(';') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
