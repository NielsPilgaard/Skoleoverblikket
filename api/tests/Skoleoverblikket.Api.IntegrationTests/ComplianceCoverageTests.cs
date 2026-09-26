using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Integration tests for ComplianceCoverageController.
/// Covers:
///   - Coverage calc: class with matching slots shows green/yellow/red/missing status.
///   - Classes with no slots appear with all subjects missing.
///   - Classes without a GradeLevel are excluded.
///   - Slots with SubjectCategory.Fri are excluded from coverage.
///   - Only admin/board roles can access the endpoint.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class ComplianceCoverageTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private readonly ApiFactory _factory = factory;
	private readonly Guid _tenantId = Guid.NewGuid();
	private HttpClient _adminClient = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, _tenantId);
		_adminClient = _factory.CreateClient();
		_adminClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		_adminClient.DefaultRequestHeaders.Add("X-Test-Roles", "admin");
		_adminClient.DefaultRequestHeaders.Add("X-Test-Subject", "staamaal-admin-subject");
	}

	// ── Private helpers ──────────────────────────────────────────────────────────

	private async Task<(Class klass, Schema schema)> CreateGradedClassWithActiveSchemaAsync(int gradeLevel, string name)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		var klass = new Class
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = name,
			GradeLevel = gradeLevel,
		};
		db.Classes.Add(klass);

		var schema = new Schema
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			ClassId = klass.Id,
			Name = $"Skema {name}",
			StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1),
			EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(11),
		};
		db.Schemas.Add(schema);
		await db.SaveChangesAsync();

		return (klass, schema);
	}

	private async Task<Course> CreateCourseWithCategoryAsync(SubjectCategory category, string name)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var course = new Course
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = name,
			Category = category,
		};
		db.Courses.Add(course);
		await db.SaveChangesAsync();
		return course;
	}

	// ── GET /api/v1/compliance-coverage/coverage ───────────────────────────────────────

	[Test]
	public async Task GetCoverage_NonAdmin_Returns403()
	{
		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", "nonadmin-staamaal");

		var response = await client.GetAsync("/api/v1/compliance-coverage/coverage");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task GetCoverage_BoardRole_Returns200()
	{
		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "board");
		client.DefaultRequestHeaders.Add("X-Test-Subject", "board-staamaal");

		var response = await client.GetAsync("/api/v1/compliance-coverage/coverage");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task GetCoverage_ClassWithNoSlots_AllSubjectsMissing()
	{
		// Class with GradeLevel but no schema slots → all subjects "missing"
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var klass = new Class
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = "0.a-noschema",
			GradeLevel = 0,
		};
		db.Classes.Add(klass);
		await db.SaveChangesAsync();

		var response = await _adminClient.GetAsync("/api/v1/compliance-coverage/coverage");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<ComplianceCoverageController.CoverageResponseDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();

		var classDto = dto!.Classes.FirstOrDefault(c => c.ClassId == klass.Id);
		await Assert.That(classDto).IsNotNull();
		await Assert.That(classDto!.Subjects.All(s => s.Status == "missing")).IsTrue();
	}

	[Test]
	public async Task GetCoverage_ClassWithoutGradeLevel_Excluded()
	{
		// Class with no GradeLevel should not appear in coverage at all
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var klass = new Class
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = "no-grade-class",
			GradeLevel = null,
		};
		db.Classes.Add(klass);
		await db.SaveChangesAsync();

		var response = await _adminClient.GetAsync("/api/v1/compliance-coverage/coverage");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<ComplianceCoverageController.CoverageResponseDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.Classes.Any(c => c.ClassId == klass.Id)).IsFalse();
	}

	[Test]
	public async Task GetCoverage_SlotWithFriCategory_ExcludedFromCoverage()
	{
		// Slots with SubjectCategory.Fri must not count toward any UVM subject
		var (klass, schema) = await CreateGradedClassWithActiveSchemaAsync(3, "3.fri-test");
		var friCourse = await CreateCourseWithCategoryAsync(SubjectCategory.Fri, "Fri leg");
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(
			_factory.Services, _tenantId,
			new TimeOnly(8, 0), new TimeOnly(9, 0));
		await TestDataBuilder.CreateSchemaSlotAsync(
			_factory.Services, _tenantId,
			schema.Id, timeSlot.Id, friCourse.Id, staff.Id);

		var response = await _adminClient.GetAsync("/api/v1/compliance-coverage/coverage");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<ComplianceCoverageController.CoverageResponseDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();

		var classDto = dto!.Classes.FirstOrDefault(c => c.ClassId == klass.Id);
		await Assert.That(classDto).IsNotNull();
		// All subjects should be missing — the Fri slot contributed nothing
		await Assert.That(classDto!.Subjects.All(s => s.Status == "missing")).IsTrue();
	}

	[Test]
	public async Task GetCoverage_FullHoursForSubject_ReturnsGreenStatus()
	{
		// Grade 1 Dansk vejledende = 8.25 h/week. Give >8.25h/week → green.
		var (klass, schema) = await CreateGradedClassWithActiveSchemaAsync(1, "1.green-test");
		var danskCourse = await CreateCourseWithCategoryAsync(SubjectCategory.Dansk, "Dansk grøn");
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);

		// 9 × 1-hour slots on different weekdays (Mon–Fri, then wrap) → 9 h/week
		var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
		for (var i = 0; i < 9; i++)
		{
			var start = new TimeOnly(8, 0).AddHours(i);
			var end = start.AddHours(1);
			var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(
				_factory.Services, _tenantId, start, end, sortOrder: 100 + i);
			await TestDataBuilder.CreateSchemaSlotAsync(
				_factory.Services, _tenantId,
				schema.Id, timeSlot.Id, danskCourse.Id, staff.Id,
				weekdays[i % weekdays.Length]);
		}

		var response = await _adminClient.GetAsync("/api/v1/compliance-coverage/coverage");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<ComplianceCoverageController.CoverageResponseDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();

		var classDto = dto!.Classes.FirstOrDefault(c => c.ClassId == klass.Id);
		await Assert.That(classDto).IsNotNull();
		var danskSubject = classDto!.Subjects.FirstOrDefault(s => s.Category == SubjectCategory.Dansk.ToString());
		await Assert.That(danskSubject).IsNotNull();
		await Assert.That(danskSubject!.Status).IsEqualTo("green");
	}

	[Test]
	public async Task GetCoverage_LowHoursForSubject_ReturnsRedStatus()
	{
		// Grade 1 Dansk vejledende = 7 h/week. Give <75% (< 5.25h) → red.
		var (klass, schema) = await CreateGradedClassWithActiveSchemaAsync(1, "1.red-test");
		var danskCourse = await CreateCourseWithCategoryAsync(SubjectCategory.Dansk, "Dansk rød");
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);

		// 2 × 1-hour slots → 2 h/week (< 75% of 7)
		for (var i = 0; i < 2; i++)
		{
			var start = new TimeOnly(10, 0).AddHours(i);
			var end = start.AddHours(1);
			var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(
				_factory.Services, _tenantId, start, end, sortOrder: 200 + i);
			await TestDataBuilder.CreateSchemaSlotAsync(
				_factory.Services, _tenantId,
				schema.Id, timeSlot.Id, danskCourse.Id, staff.Id,
				i == 0 ? DayOfWeek.Monday : DayOfWeek.Tuesday);
		}

		var response = await _adminClient.GetAsync("/api/v1/compliance-coverage/coverage");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<ComplianceCoverageController.CoverageResponseDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();

		var classDto = dto!.Classes.FirstOrDefault(c => c.ClassId == klass.Id);
		await Assert.That(classDto).IsNotNull();
		var danskSubject = classDto!.Subjects.FirstOrDefault(s => s.Category == SubjectCategory.Dansk.ToString());
		await Assert.That(danskSubject).IsNotNull();
		await Assert.That(danskSubject!.Status).IsEqualTo("red");
	}

	[Test]
	public async Task GetCoverage_ResultsSortedByGradeLevelThenName()
	{
		// Create grade 5 before grade 2 — result must be ordered grade asc
		await CreateGradedClassWithActiveSchemaAsync(5, "5.sort-test");
		await CreateGradedClassWithActiveSchemaAsync(2, "2.sort-test");

		var response = await _adminClient.GetAsync("/api/v1/compliance-coverage/coverage");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<ComplianceCoverageController.CoverageResponseDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();

		var sortTestClasses = dto!.Classes
			.Where(c => c.ClassName.EndsWith(".sort-test"))
			.ToList();
		await Assert.That(sortTestClasses.Count).IsGreaterThanOrEqualTo(2);
		await Assert.That(sortTestClasses[0].GradeLevel).IsLessThanOrEqualTo(sortTestClasses[1].GradeLevel);
	}

	[Test]
	public async Task GetCoverage_CategoryTaughtOutsideUvmGradeRange_FlaggedAsUnexpected()
	{
		// Tysk only starts 6. klasse per UVM timetal — scheduling it in 3. klasse
		// should be flagged as an unexpected-grade category, not silently ignored.
		var (klass, schema) = await CreateGradedClassWithActiveSchemaAsync(3, "3.tysk-test");
		var tyskCourse = await CreateCourseWithCategoryAsync(SubjectCategory.Tysk, "Tysk for tidligt");
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(
			_factory.Services, _tenantId,
			new TimeOnly(8, 0), new TimeOnly(9, 0));
		await TestDataBuilder.CreateSchemaSlotAsync(
			_factory.Services, _tenantId,
			schema.Id, timeSlot.Id, tyskCourse.Id, staff.Id);

		var response = await _adminClient.GetAsync("/api/v1/compliance-coverage/coverage");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<ComplianceCoverageController.CoverageResponseDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();

		var classDto = dto!.Classes.FirstOrDefault(c => c.ClassId == klass.Id);
		await Assert.That(classDto).IsNotNull();
		await Assert.That(classDto!.UnexpectedGradeCategories).Contains(SubjectCategory.Tysk.ToString());
	}
}
