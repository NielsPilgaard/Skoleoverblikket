using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

public sealed record ExternalLinkDto(Guid Id, string Title, string Url, string? Description);

public sealed record ExternalLinkClassDto(Guid Id, string Name);

/// <summary>A link as the admin manages it, with who sees it.</summary>
public sealed record ExternalLinkAdminDto(
	Guid Id,
	string Title,
	string Url,
	string? Description,
	bool VisibleToParents,
	bool VisibleToStaff,
	bool VisibleToBoard,
	bool RestrictToClasses,
	IReadOnlyList<ExternalLinkClassDto> Classes);

/// <summary>
/// An empty or missing <see cref="ClassIds"/> means the whole school. Classes only narrow which parents
/// see the link, so they are dropped when <see cref="VisibleToParents"/> is off.
/// </summary>
public sealed record UpsertExternalLinkRequest(
	[Required][StringLength(100, MinimumLength = 1)] string Title,
	[Required][StringLength(2000, MinimumLength = 1)] string Url,
	[StringLength(300)] string? Description,
	bool VisibleToParents,
	bool VisibleToStaff,
	bool VisibleToBoard,
	IReadOnlyList<Guid>? ClassIds);

/// <summary>Every link id at the school, in the new order.</summary>
public sealed record ReorderExternalLinksRequest([Required] IReadOnlyList<Guid> OrderedIds);

/// <summary>The audience a request reads links as: the role it acts as.</summary>
public enum LinkAudience { Parent, Staff, Board }

public enum ExternalLinkSaveResult { Saved, NotFound, InvalidUrl, NoAudience, UnknownClass }

/// <summary>
/// The one writer of <see cref="ExternalLink"/> rows: the school's list of external links (Aula, fravær,
/// buskort, Facebook groups), each shown to parents, staff, the board or any combination.
/// </summary>
public sealed partial class ExternalLinkService(AppDbContext db, ITenantContext tenant)
{
	/// <summary>
	/// Links for the audience the caller acts as, in the admin's order. Staff and board see every link
	/// for their audience; parents see school-wide parent links and links for their children's classes.
	/// </summary>
	public Task<List<ExternalLinkDto>> GetVisibleLinksAsync(
		LinkAudience audience, string? keycloakSubject, CancellationToken cancellationToken)
	{
		var links = audience switch
		{
			LinkAudience.Staff => db.ExternalLinks.Where(l => l.VisibleToStaff),
			LinkAudience.Board => db.ExternalLinks.Where(l => l.VisibleToBoard),
			_ => ParentLinks(keycloakSubject),
		};

		return links
			.AsNoTracking()
			.OrderBy(l => l.SortOrder)
			.Select(l => new ExternalLinkDto(l.Id, l.Title, l.Url, l.Description))
			.ToListAsync(cancellationToken);
	}

	private IQueryable<ExternalLink> ParentLinks(string? keycloakSubject)
	{
		// A null subject would match every parent without a login.
		if (string.IsNullOrEmpty(keycloakSubject))
		{
			return db.ExternalLinks.Where(_ => false);
		}

		var childClassIds = db.Students
			.Where(s => s.Parents.Any(p => p.KeycloakSubject == keycloakSubject))
			.Select(s => s.ClassId);

		return db.ExternalLinks.Where(l => l.VisibleToParents
			&& (!l.RestrictToClasses || l.Classes.Any(c => childClassIds.Contains(c.ClassId))));
	}

	/// <summary>Every link with its audience and classes, for the admin.</summary>
	public async Task<List<ExternalLinkAdminDto>> GetAllLinksAsync(CancellationToken cancellationToken)
	{
		var rows = await db.ExternalLinks
			.AsNoTracking()
			.OrderBy(l => l.SortOrder)
			.Select(l => new
			{
				l.Id,
				l.Title,
				l.Url,
				l.Description,
				l.VisibleToParents,
				l.VisibleToStaff,
				l.VisibleToBoard,
				l.RestrictToClasses,
				// Through Class, so its query filter leaves out archived classes.
				Classes = l.Classes
					.Select(c => c.Class)
					.OrderBy(c => c.Name)
					.Select(c => new ExternalLinkClassDto(c.Id, c.Name))
					.ToList(),
			})
			.ToListAsync(cancellationToken);

		return rows
			.Select(r => new ExternalLinkAdminDto(
				r.Id, r.Title, r.Url, r.Description, r.VisibleToParents, r.VisibleToStaff, r.VisibleToBoard,
				r.RestrictToClasses, r.Classes))
			.ToList();
	}

	/// <summary>Adds a link at the end of the list.</summary>
	public async Task<(ExternalLinkSaveResult Result, Guid? Id)> CreateLinkAsync(
		UpsertExternalLinkRequest req, CancellationToken cancellationToken)
	{
		var (error, url, classes) = await ValidateAsync(req, cancellationToken);
		if (error is { } result)
		{
			return (result, null);
		}

		var lastSortOrder = await db.ExternalLinks.MaxAsync(l => (int?)l.SortOrder, cancellationToken);
		var link = new ExternalLink
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			Title = req.Title.Trim(),
			Url = url!,
			SortOrder = (lastSortOrder ?? -1) + 1,
		};
		Apply(link, req, classes);

		db.ExternalLinks.Add(link);
		await db.SaveChangesAsync(cancellationToken);
		return (ExternalLinkSaveResult.Saved, link.Id);
	}

	public async Task<ExternalLinkSaveResult> UpdateLinkAsync(
		Guid id, UpsertExternalLinkRequest req, CancellationToken cancellationToken)
	{
		var link = await db.ExternalLinks
			.Include(l => l.Classes)
			.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
		if (link is null)
		{
			return ExternalLinkSaveResult.NotFound;
		}

		var (error, url, classes) = await ValidateAsync(req, cancellationToken);
		if (error is { } result)
		{
			return result;
		}

		link.Title = req.Title.Trim();
		link.Url = url!;
		link.UpdatedAt = DateTimeOffset.UtcNow;
		Apply(link, req, classes);

		await db.SaveChangesAsync(cancellationToken);
		return ExternalLinkSaveResult.Saved;
	}

	/// <summary>False when the link doesn't exist.</summary>
	public async Task<bool> DeleteLinkAsync(Guid id, CancellationToken cancellationToken) =>
		await db.ExternalLinks.Where(l => l.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

	/// <summary>
	/// Sets the order of every link. False when <paramref name="orderedIds"/> isn't exactly the school's
	/// links, for example because another admin added or deleted one meanwhile.
	/// </summary>
	public async Task<bool> ReorderAsync(IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken)
	{
		var links = await db.ExternalLinks.ToListAsync(cancellationToken);
		if (orderedIds.Count != links.Count || !orderedIds.ToHashSet().SetEquals(links.Select(l => l.Id)))
		{
			return false;
		}

		var byId = links.ToDictionary(l => l.Id);
		for (var i = 0; i < orderedIds.Count; i++)
		{
			byId[orderedIds[i]].SortOrder = i;
		}

		await db.SaveChangesAsync(cancellationToken);
		return true;
	}

	private static void Apply(ExternalLink link, UpsertExternalLinkRequest req, List<Class> classes)
	{
		link.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
		link.VisibleToParents = req.VisibleToParents;
		link.VisibleToStaff = req.VisibleToStaff;
		link.VisibleToBoard = req.VisibleToBoard;
		link.RestrictToClasses = classes.Count > 0;

		// Diff rather than clear and re-add, so a kept class isn't tracked as both deleted and added.
		var classIds = classes.Select(c => c.Id).ToHashSet();
		foreach (var removed in link.Classes.Where(c => !classIds.Contains(c.ClassId)).ToList())
		{
			link.Classes.Remove(removed);
		}

		var keptIds = link.Classes.Select(c => c.ClassId).ToHashSet();
		foreach (var classId in classIds.Where(id => !keptIds.Contains(id)))
		{
			link.Classes.Add(new ExternalLinkClass { TenantId = link.TenantId, ExternalLinkId = link.Id, ClassId = classId });
		}
	}

	/// <summary>The normalized URL and the classes to restrict to, or the reason the request is invalid.</summary>
	private async Task<(ExternalLinkSaveResult? Error, string? Url, List<Class> Classes)> ValidateAsync(
		UpsertExternalLinkRequest req, CancellationToken cancellationToken)
	{
		if (!req.VisibleToParents && !req.VisibleToStaff && !req.VisibleToBoard)
		{
			return (ExternalLinkSaveResult.NoAudience, null, []);
		}

		if (NormalizeUrl(req.Url) is not { } url)
		{
			return (ExternalLinkSaveResult.InvalidUrl, null, []);
		}

		var classIds = req.VisibleToParents ? (req.ClassIds ?? []).Distinct().ToList() : [];
		var classes = classIds.Count == 0
			? []
			: await db.Classes.Where(c => classIds.Contains(c.Id)).ToListAsync(cancellationToken);
		if (classes.Count != classIds.Count)
		{
			return (ExternalLinkSaveResult.UnknownClass, null, []);
		}

		return (null, url, classes);
	}

	/// <summary>
	/// An absolute http(s) URL, with https:// added when the admin typed none ("aula.dk"). Anything with
	/// another scheme (javascript:, mailto:, tel:) is null, so a link can never run script or open an app.
	/// </summary>
	private static string? NormalizeUrl(string input)
	{
		var candidate = input.Trim();
		if (!SchemePrefix().IsMatch(candidate))
		{
			candidate = "https://" + candidate;
		}

		return candidate.Length <= 2000
			&& Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
			&& (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
			&& !string.IsNullOrEmpty(uri.Host)
			? candidate
			: null;
	}

	/// <summary>
	/// "javascript:", "https:", "mailto:". No dot allowed, so "aula.dk:8080/x" counts as a host with a port.
	/// </summary>
	[GeneratedRegex("^[a-zA-Z][a-zA-Z0-9+-]*:")]
	private static partial Regex SchemePrefix();
}
