using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;
using System.ComponentModel.DataAnnotations;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/imports")]
[Authorize(Roles = Roles.Admin)]
public sealed class ImportsController(AppDbContext db, ITenantContext tenant) : ControllerBase
{
	public record ImportWarning([Required] int Row, [Required] string Message);

	public record ImportParentRow(
		string? Name,
		string? Email,
		string? Phone,
		string? Address,
		string? PostalCode,
		string? City);

	public record ImportStudentRow(
		[Required] string ClassName,
		[Required] string StudentName,
		ImportParentRow? Parent1,
		ImportParentRow? Parent2);

	public record ImportStudentsAndParentsRequest(
		[Required] IReadOnlyList<ImportStudentRow> Rows);

	public record ImportStudentsAndParentsResponse(
		[Required] int ClassesCreated,
		[Required] int StudentsCreated,
		[Required] int StudentsSkipped,
		[Required] int ParentsCreated,
		[Required] int ParentsUpdated,
		[Required] int ParentStudentLinksCreated,
		[Required] IReadOnlyList<ImportWarning> Warnings,
		[Required] IReadOnlyList<UninvitedParentDto> UninvitedParents);

	public record UninvitedParentDto([Required] Guid Id, [Required] string Name, [Required] string Email);

	[HttpPost("students-and-parents")]
	[RequiresModule(SubscriptionModule.ParentModule)]
	public async Task<ActionResult<ImportStudentsAndParentsResponse>> ImportStudentsAndParents(
		[FromBody] ImportStudentsAndParentsRequest req,
		CancellationToken cancellationToken)
	{
		var warnings = new List<ImportWarning>();

		int classesCreated = 0, studentsCreated = 0, studentsSkipped = 0,
			parentsCreated = 0, parentsUpdated = 0, linksCreated = 0;
		var touchedParents = new Dictionary<Guid, Parent>();

		// Pre-load existing classes for this tenant
		var classCache = (await db.Classes
			.Where(c => c.ArchivedAt == null)
			.ToListAsync(cancellationToken))
			.GroupBy(c => c.Name.Trim().ToLowerInvariant())
			.ToDictionary(g => g.Key, g => g.First());

		var existingStudentsByClass = (await db.Students
			.Select(s => new { s.Id, s.Name, s.ClassId })
			.ToListAsync(cancellationToken))
			.GroupBy(s => s.ClassId)
			.ToDictionary(g => g.Key, g => g.ToList());

		// Pre-load existing parents by email — skip ambiguous duplicates rather than throwing
		var parentByEmail = (await db.Parents
			.Include(p => p.Students)
			.ToListAsync(cancellationToken))
			.Where(p => !string.IsNullOrWhiteSpace(p.Email))
			.GroupBy(p => p.Email!.Trim().ToLowerInvariant())
			.Where(g => g.Count() == 1)
			.ToDictionary(g => g.Key, g => g.First());

		int rowNum = 0;
		foreach (var row in req.Rows)
		{
			rowNum++;

			if (string.IsNullOrWhiteSpace(row.ClassName) || string.IsNullOrWhiteSpace(row.StudentName))
			{
				warnings.Add(new ImportWarning(rowNum, "Klasse og elevnavn er påkrævet — rækken blev sprunget over"));
				continue;
			}

			// ── Class upsert ────────────────────────────────────────────
			var classKey = row.ClassName.Trim().ToLowerInvariant();
			if (!classCache.TryGetValue(classKey, out var klasse))
			{
				klasse = new Class
				{
					Id = Guid.NewGuid(),
					TenantId = tenant.TenantId,
					Name = row.ClassName.Trim(),
				};
				db.Classes.Add(klasse);
				classCache[classKey] = klasse;
				classesCreated++;
			}

			// ── Student dedup ────────────────────────────────────────────
			var studentName = row.StudentName.Trim();
			if (!existingStudentsByClass.TryGetValue(klasse.Id, out var classStudents))
			{
				classStudents = [];
				existingStudentsByClass[klasse.Id] = classStudents;
			}

			var existingStudentMatch = classStudents.FirstOrDefault(s =>
				string.Equals(s.Name, studentName, StringComparison.OrdinalIgnoreCase));

			Student student;
			if (existingStudentMatch is not null)
			{
				warnings.Add(new ImportWarning(rowNum,
					$"Elev '{studentName}' i klasse '{row.ClassName.Trim()}' findes allerede — forældre behandles stadig"));
				studentsSkipped++;
				var existingId = existingStudentMatch.Id;
				student = await db.Students
					.Include(s => s.Parents)
					.FirstAsync(s => s.Id == existingId, cancellationToken);
			}
			else
			{
				student = new Student
				{
					Id = Guid.NewGuid(),
					TenantId = tenant.TenantId,
					Name = studentName,
					ClassId = klasse.Id,
				};
				db.Students.Add(student);
				classStudents.Add(new { student.Id, student.Name, student.ClassId });
				studentsCreated++;
			}

			// ── Parents ──────────────────────────────────────────────────
			foreach (var (parentRow, slot) in new[] { (row.Parent1, 1), (row.Parent2, 2) })
			{
				if (parentRow is null)
				{
					continue;
				}

				bool hasEmail = !string.IsNullOrWhiteSpace(parentRow.Email);
				bool hasName = !string.IsNullOrWhiteSpace(parentRow.Name);

				if (!hasEmail && !hasName)
				{
					continue; // both blank — skip
				}

				if (hasEmail && !IsValidEmail(parentRow.Email!))
				{
					warnings.Add(new ImportWarning(rowNum,
						$"Forælder {slot} har ugyldig e-mail '{parentRow.Email!.Trim()}' — forælder oprettet uden loginkonto"));
					hasEmail = false;

					if (!hasName)
					{
						continue; // invalid email and no name — nothing to create
					}
				}

				if (!hasEmail && hasName)
				{
					warnings.Add(new ImportWarning(rowNum,
						$"Forælder {slot} mangler e-mail — forælder oprettet uden loginkonto"));
				}

				Parent parent;
				if (hasEmail)
				{
					var emailKey = parentRow.Email!.Trim().ToLowerInvariant();
					if (parentByEmail.TryGetValue(emailKey, out var existing))
					{
						// Upsert — overwrite mutable fields, preserve flags
						if (hasName)
						{
							existing.Name = parentRow.Name!.Trim();
						}

						if (!string.IsNullOrWhiteSpace(parentRow.Phone))
						{
							existing.Phone = parentRow.Phone.Trim();
						}

						if (!string.IsNullOrWhiteSpace(parentRow.Address))
						{
							existing.Address = parentRow.Address.Trim();
						}

						if (!string.IsNullOrWhiteSpace(parentRow.PostalCode))
						{
							existing.PostalCode = parentRow.PostalCode.Trim();
						}

						if (!string.IsNullOrWhiteSpace(parentRow.City))
						{
							existing.City = parentRow.City.Trim();
						}

						parent = existing;
						parentsUpdated++;
					}
					else
					{
						parent = new Parent
						{
							Id = Guid.NewGuid(),
							TenantId = tenant.TenantId,
							Name = hasName ? parentRow.Name!.Trim() : parentRow.Email!.Trim(),
							Email = parentRow.Email!.Trim(),
							Phone = string.IsNullOrWhiteSpace(parentRow.Phone) ? null : parentRow.Phone.Trim(),
							Address = string.IsNullOrWhiteSpace(parentRow.Address) ? null : parentRow.Address.Trim(),
							PostalCode = string.IsNullOrWhiteSpace(parentRow.PostalCode) ? null : parentRow.PostalCode.Trim(),
							City = string.IsNullOrWhiteSpace(parentRow.City) ? null : parentRow.City.Trim(),
							ShareContactInfo = false,
							AdresseBeskyttet = false,
						};
						db.Parents.Add(parent);
						parentByEmail[emailKey] = parent;
						parentsCreated++;
					}
				}
				else
				{
					// No email — always create new parent without login capability
					parent = new Parent
					{
						Id = Guid.NewGuid(),
						TenantId = tenant.TenantId,
						Name = parentRow.Name!.Trim(),
						Email = string.Empty,
						Phone = string.IsNullOrWhiteSpace(parentRow.Phone) ? null : parentRow.Phone.Trim(),
						Address = string.IsNullOrWhiteSpace(parentRow.Address) ? null : parentRow.Address.Trim(),
						PostalCode = string.IsNullOrWhiteSpace(parentRow.PostalCode) ? null : parentRow.PostalCode.Trim(),
						City = string.IsNullOrWhiteSpace(parentRow.City) ? null : parentRow.City.Trim(),
						ShareContactInfo = false,
						AdresseBeskyttet = false,
					};
					db.Parents.Add(parent);
					parentsCreated++;
				}

				// Link parent → student if not already present
				var alreadyLinked = parent.Students.Any(s => s.Id == student.Id);
				if (!alreadyLinked)
				{
					parent.Students.Add(student);
					linksCreated++;
				}

				touchedParents[parent.Id] = parent;
			}
		}

		await db.SaveChangesAsync(cancellationToken);

		var uninvitedParents = touchedParents.Values
			.Where(p => p.KeycloakSubject is null && !string.IsNullOrWhiteSpace(p.Email))
			.Select(p => new UninvitedParentDto(p.Id, p.Name, p.Email))
			.ToList();

		return Ok(new ImportStudentsAndParentsResponse(
			classesCreated, studentsCreated, studentsSkipped,
			parentsCreated, parentsUpdated, linksCreated,
			warnings, uninvitedParents));
	}

	public record ImportStaffRow(
		string? Name,
		string? Email,
		string? Phone,
		string? Role,
		string? Administrator);

	public record ImportStaffRequest(
		[Required] IReadOnlyList<ImportStaffRow> Rows);

	public record ImportStaffResponse(
		[Required] int StaffCreated,
		[Required] int StaffUpdated,
		[Required] int StaffSkipped,
		[Required] IReadOnlyList<ImportWarning> Warnings);

	[HttpPost("staff")]
	public async Task<ActionResult<ImportStaffResponse>> ImportStaff(
		[FromBody] ImportStaffRequest req,
		CancellationToken cancellationToken)
	{
		var warnings = new List<ImportWarning>();
		int staffCreated = 0, staffUpdated = 0, staffSkipped = 0;

		var currentUserSub = User.GetKeycloakSubject();

		var existingByEmail = (await db.Staff
			.Where(s => s.Email != null)
			.ToListAsync(cancellationToken))
			.GroupBy(s => s.Email!.Trim().ToLowerInvariant())
			.Where(g => g.Count() == 1)
			.ToDictionary(g => g.Key, g => g.First());

		// Group by name to detect duplicates — ambiguous matches are skipped with a warning
		var existingByNameGroups = (await db.Staff.ToListAsync(cancellationToken))
			.GroupBy(s => s.Name.Trim().ToLowerInvariant())
			.ToDictionary(g => g.Key, g => g.ToList());

		int rowNum = 0;
		foreach (var row in req.Rows)
		{
			rowNum++;

			bool hasEmail = !string.IsNullOrWhiteSpace(row.Email);
			bool hasName = !string.IsNullOrWhiteSpace(row.Name);

			if (!hasEmail && !hasName)
			{
				staffSkipped++;
				continue;
			}

			if (hasEmail && !IsValidEmail(row.Email!))
			{
				warnings.Add(new ImportWarning(rowNum,
					$"Ugyldig e-mail '{row.Email!.Trim()}' — rækken blev sprunget over"));
				staffSkipped++;
				continue;
			}

			var role = ParseStaffRole(row.Role, out var roleWarning);
			if (roleWarning is not null)
			{
				warnings.Add(new ImportWarning(rowNum, roleWarning));
			}

			bool? isAdmin = ParseBoolStrict(row.Administrator, out var adminWarning);
			if (adminWarning is not null)
			{
				warnings.Add(new ImportWarning(rowNum, adminWarning));
			}

			if (hasEmail)
			{
				var emailKey = row.Email!.Trim().ToLowerInvariant();
				if (existingByEmail.TryGetValue(emailKey, out var existing))
				{
					var oldNameKey = existing.Name.Trim().ToLowerInvariant();
					if (hasName)
					{
						existing.Name = row.Name!.Trim();
						// Keep name cache consistent after rename
						if (existingByNameGroups.TryGetValue(oldNameKey, out var oldGroup))
						{
							oldGroup.Remove(existing);
						}

						var newNameKey = existing.Name.ToLowerInvariant();
						if (!existingByNameGroups.TryGetValue(newNameKey, out var newGroup))
						{
							newGroup = [];
							existingByNameGroups[newNameKey] = newGroup;
						}

						newGroup.Add(existing);
					}

					if (!string.IsNullOrWhiteSpace(row.Phone))
					{
						existing.Phone = row.Phone.Trim();
					}

					existing.Role = role;
					if (isAdmin.HasValue && existing.KeycloakSubject != currentUserSub)
					{
						existing.IsAdmin = isAdmin.Value;
					}

					staffUpdated++;
				}
				else
				{
					var staff = new Staff
					{
						Id = Guid.NewGuid(),
						TenantId = tenant.TenantId,
						Name = hasName ? row.Name!.Trim() : row.Email!.Trim(),
						Email = row.Email!.Trim(),
						Phone = string.IsNullOrWhiteSpace(row.Phone) ? null : row.Phone.Trim(),
						Role = role,
						IsAdmin = isAdmin ?? false,
					};
					db.Staff.Add(staff);
					existingByEmail[emailKey] = staff;
					var nameKey2 = staff.Name.ToLowerInvariant();
					if (!existingByNameGroups.TryGetValue(nameKey2, out var nameGroup))
					{
						nameGroup = [];
						existingByNameGroups[nameKey2] = nameGroup;
					}

					nameGroup.Add(staff);

					staffCreated++;
				}
			}
			else
			{
				// No email — upsert by name only if unambiguous
				var nameKey = row.Name!.Trim().ToLowerInvariant();
				if (existingByNameGroups.TryGetValue(nameKey, out var matches) && matches.Count > 0)
				{
					if (matches.Count > 1)
					{
						warnings.Add(new ImportWarning(rowNum,
							$"Navn '{row.Name!.Trim()}' matcher flere medarbejdere — rækken blev sprunget over (angiv e-mail)"));
						staffSkipped++;
						continue;
					}

					var existing = matches[0];
					if (!string.IsNullOrWhiteSpace(row.Phone))
					{
						existing.Phone = row.Phone.Trim();
					}

					existing.Role = role;
					if (isAdmin.HasValue && existing.KeycloakSubject != currentUserSub)
					{
						existing.IsAdmin = isAdmin.Value;
					}

					staffUpdated++;
				}
				else
				{
					var staff = new Staff
					{
						Id = Guid.NewGuid(),
						TenantId = tenant.TenantId,
						Name = row.Name!.Trim(),
						Email = null,
						Phone = string.IsNullOrWhiteSpace(row.Phone) ? null : row.Phone.Trim(),
						Role = role,
						IsAdmin = isAdmin ?? false,
					};
					db.Staff.Add(staff);
					existingByNameGroups[nameKey] = [staff];
					staffCreated++;
				}
			}
		}

		await db.SaveChangesAsync(cancellationToken);

		return Ok(new ImportStaffResponse(staffCreated, staffUpdated, staffSkipped, warnings));
	}

	public record ImportRoomRow(
		string? Name,
		string? Description,
		string? Capacity);

	public record ImportRoomsRequest(
		[Required] IReadOnlyList<ImportRoomRow> Rows);

	public record ImportRoomsResponse(
		[Required] int RoomsCreated,
		[Required] int RoomsUpdated,
		[Required] int RoomsSkipped,
		[Required] IReadOnlyList<ImportWarning> Warnings);

	[HttpPost("rooms")]
	public async Task<ActionResult<ImportRoomsResponse>> ImportRooms(
		[FromBody] ImportRoomsRequest req,
		CancellationToken cancellationToken)
	{
		var warnings = new List<ImportWarning>();
		int roomsCreated = 0, roomsUpdated = 0, roomsSkipped = 0;

		var existingByName = (await db.Rooms.ToListAsync(cancellationToken))
			.GroupBy(r => r.Name.Trim().ToLowerInvariant())
			.Where(g => g.Count() == 1)
			.ToDictionary(g => g.Key, g => g.First());

		int rowNum = 0;
		foreach (var row in req.Rows)
		{
			rowNum++;

			if (string.IsNullOrWhiteSpace(row.Name))
			{
				roomsSkipped++;
				continue;
			}

			int? capacity = null;
			if (!string.IsNullOrWhiteSpace(row.Capacity))
			{
				if (int.TryParse(row.Capacity.Trim(), out var cap) && cap > 0)
				{
					capacity = cap;
				}
				else
				{
					warnings.Add(new ImportWarning(rowNum,
						$"Kapacitet '{row.Capacity}' er ikke et tal — felt ignoreret"));
				}
			}

			var nameKey = row.Name.Trim().ToLowerInvariant();
			if (existingByName.TryGetValue(nameKey, out var existing))
			{
				if (!string.IsNullOrWhiteSpace(row.Description))
				{
					existing.Description = row.Description.Trim();
				}

				if (capacity.HasValue)
				{
					existing.Capacity = capacity;
				}

				roomsUpdated++;
			}
			else
			{
				var room = new Room
				{
					Id = Guid.NewGuid(),
					TenantId = tenant.TenantId,
					Name = row.Name.Trim(),
					Description = string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim(),
					Capacity = capacity,
				};
				db.Rooms.Add(room);
				existingByName[nameKey] = room;
				roomsCreated++;
			}
		}

		await db.SaveChangesAsync(cancellationToken);

		return Ok(new ImportRoomsResponse(roomsCreated, roomsUpdated, roomsSkipped, warnings));
	}

	public record ImportClassRow(
		string? Name,
		string? Description,
		string? GradeLevel);

	public record ImportClassesRequest(
		[Required] IReadOnlyList<ImportClassRow> Rows);

	public record ImportClassesResponse(
		[Required] int ClassesCreated,
		[Required] int ClassesUpdated,
		[Required] int ClassesSkipped,
		[Required] IReadOnlyList<ImportWarning> Warnings);

	[HttpPost("classes")]
	public async Task<ActionResult<ImportClassesResponse>> ImportClasses(
		[FromBody] ImportClassesRequest req,
		CancellationToken cancellationToken)
	{
		var warnings = new List<ImportWarning>();
		int classesCreated = 0, classesUpdated = 0, classesSkipped = 0;

		var classGroups = (await db.Classes
			.Where(c => c.ArchivedAt == null)
			.ToListAsync(cancellationToken))
			.GroupBy(c => c.Name.Trim().ToLowerInvariant())
			.ToList();

		var existingByName = classGroups
			.Where(g => g.Count() == 1)
			.ToDictionary(g => g.Key, g => g.First());

		var ambiguousNames = classGroups
			.Where(g => g.Count() > 1)
			.Select(g => g.Key)
			.ToHashSet();

		int rowNum = 0;
		foreach (var row in req.Rows)
		{
			rowNum++;

			if (string.IsNullOrWhiteSpace(row.Name))
			{
				classesSkipped++;
				continue;
			}

			int? gradeLevel = null;
			if (!string.IsNullOrWhiteSpace(row.GradeLevel))
			{
				if (int.TryParse(row.GradeLevel.Trim(), out var grade) && grade is >= 0 and <= 10)
				{
					gradeLevel = grade;
				}
				else
				{
					warnings.Add(new ImportWarning(rowNum,
						$"Klassetrin '{row.GradeLevel}' er ikke et tal mellem 0 og 10 — felt ignoreret"));
				}
			}

			var nameKey = row.Name.Trim().ToLowerInvariant();
			if (ambiguousNames.Contains(nameKey))
			{
				classesSkipped++;
				warnings.Add(new ImportWarning(rowNum,
					$"Klasse '{row.Name}' findes flere gange — spring over for at undgå fejlmatch"));
			}
			else if (existingByName.TryGetValue(nameKey, out var existing))
			{
				if (!string.IsNullOrWhiteSpace(row.Description))
				{
					existing.Description = row.Description.Trim();
				}

				if (gradeLevel.HasValue)
				{
					existing.GradeLevel = gradeLevel;
				}

				classesUpdated++;
			}
			else
			{
				var klasse = new Class
				{
					Id = Guid.NewGuid(),
					TenantId = tenant.TenantId,
					Name = row.Name.Trim(),
					Description = string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim(),
					GradeLevel = gradeLevel,
				};
				db.Classes.Add(klasse);
				existingByName[nameKey] = klasse;
				classesCreated++;
			}
		}

		await db.SaveChangesAsync(cancellationToken);

		return Ok(new ImportClassesResponse(classesCreated, classesUpdated, classesSkipped, warnings));
	}

	public record ImportBoardMemberRow(
		string? Name,
		string? Email,
		string? CanAccessTeacherData);

	public record ImportBoardMembersRequest(
		[Required] IReadOnlyList<ImportBoardMemberRow> Rows);

	public record ImportBoardMembersResponse(
		[Required] int BoardMembersCreated,
		[Required] int BoardMembersUpdated,
		[Required] int BoardMembersSkipped,
		[Required] IReadOnlyList<ImportWarning> Warnings);

	[HttpPost("board-members")]
	[RequiresModule(SubscriptionModule.BoardModule)]
	public async Task<ActionResult<ImportBoardMembersResponse>> ImportBoardMembers(
		[FromBody] ImportBoardMembersRequest req,
		CancellationToken cancellationToken)
	{
		var warnings = new List<ImportWarning>();
		int created = 0, updated = 0, skipped = 0;

		var existingByEmail = (await db.BoardMembers.ToListAsync(cancellationToken))
			.ToDictionary(b => b.Email.Trim().ToLowerInvariant());

		int rowNum = 0;
		foreach (var row in req.Rows)
		{
			rowNum++;

			if (string.IsNullOrWhiteSpace(row.Email) || string.IsNullOrWhiteSpace(row.Name))
			{
				skipped++;
				continue;
			}

			if (!IsValidEmail(row.Email))
			{
				warnings.Add(new ImportWarning(rowNum,
					$"Ugyldig e-mail '{row.Email.Trim()}' — rækken blev sprunget over"));
				skipped++;
				continue;
			}

			bool? canAccess = ParseBoolStrict(row.CanAccessTeacherData, out var canAccessWarning);
			if (canAccessWarning is not null)
			{
				warnings.Add(new ImportWarning(rowNum, canAccessWarning));
			}

			var emailKey = row.Email.Trim().ToLowerInvariant();

			if (existingByEmail.TryGetValue(emailKey, out var existing))
			{
				existing.Name = row.Name.Trim();
				if (canAccess.HasValue)
				{
					existing.CanAccessTeacherData = canAccess.Value;
				}

				updated++;
			}
			else
			{
				var member = new BoardMember
				{
					Id = Guid.NewGuid(),
					TenantId = tenant.TenantId,
					Name = row.Name.Trim(),
					Email = row.Email.Trim(),
					CanAccessTeacherData = canAccess ?? false,
				};
				db.BoardMembers.Add(member);
				existingByEmail[emailKey] = member;
				created++;
			}
		}

		await db.SaveChangesAsync(cancellationToken);

		return Ok(new ImportBoardMembersResponse(created, updated, skipped, warnings));
	}

	private static bool? ParseBoolStrict(string? value, out string? warning)
	{
		warning = null;

		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		return value.Trim().ToLowerInvariant() switch
		{
			"ja" or "true" or "1" => true,
			"nej" or "false" or "0" => false,
			_ => Warn(value, out warning),
		};

		static bool? Warn(string v, out string? w)
		{
			w = $"Ukendt værdi '{v}' for ja/nej-felt — feltet blev ikke ændret";
			return null;
		}
	}

	private static bool IsValidEmail(string email)
	{
		try
		{
			var address = new System.Net.Mail.MailAddress(email.Trim());
			return address.Address == email.Trim();
		}
		catch (FormatException)
		{
			return false;
		}
	}

	private static StaffRole ParseStaffRole(string? value, out string? warning)
	{
		warning = null;

		if (string.IsNullOrWhiteSpace(value))
		{
			return StaffRole.Teacher;
		}

		return value.Trim().ToLowerInvariant() switch
		{
			"lærer" or "laerer" or "teacher" => StaffRole.Teacher,
			"pædagog" or "paedagog" or "aide" => StaffRole.Aide,
			"vikar" or "substitute" => StaffRole.Substitute,
			_ => SetWarningAndDefault(value, out warning),
		};

		static StaffRole SetWarningAndDefault(string role, out string? warning)
		{
			warning = $"Ukendt rolle '{role}' — sat til 'Lærer'";
			return StaffRole.Teacher;
		}
	}
}
