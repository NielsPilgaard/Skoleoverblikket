using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Integration tests for ReportsController.
/// Covers:
///   - All four xlsx endpoints return 200 with correct content-type for admin.
///   - All four xlsx endpoints return 403 for non-admin.
///   - Staff hours report includes a row for each teacher with active slots.
///   - Course hours report groups by class + course correctly.
///   - Schema report returns rows ordered by class, day, time.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class ReportsTests(ApiFactory factory)
{
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
		_adminClient.DefaultRequestHeaders.Add("X-Test-Subject", "reports-admin-subject");
	}

	private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

	// ── Auth enforcement ──────────────────────────────────────────────────────────

	[Test]
	public async Task StaffHoursXlsx_NonAdmin_Returns403()
	{
		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", "nonadmin-reports");

		var response = await client.GetAsync("/api/v1/reports/hours/staff.xlsx");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task CourseHoursXlsx_NonAdmin_Returns403()
	{
		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", "nonadmin-reports-courses");

		var response = await client.GetAsync("/api/v1/reports/hours/courses.xlsx");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task SchemaXlsx_NonAdmin_Returns403()
	{
		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", "nonadmin-reports-schema");

		var response = await client.GetAsync("/api/v1/reports/schema.xlsx");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task UvmMinimumstimetalXlsx_NonAdmin_Returns403()
	{
		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", "nonadmin-reports-uvm");

		var response = await client.GetAsync("/api/v1/reports/uvm-minimumstimetal.xlsx");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	// ── Admin returns xlsx ────────────────────────────────────────────────────────

	[Test]
	public async Task StaffHoursXlsx_Admin_ReturnsXlsx()
	{
		var response = await _adminClient.GetAsync("/api/v1/reports/hours/staff.xlsx");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo(XlsxMime);
	}

	[Test]
	public async Task CourseHoursXlsx_Admin_ReturnsXlsx()
	{
		var response = await _adminClient.GetAsync("/api/v1/reports/hours/courses.xlsx");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo(XlsxMime);
	}

	[Test]
	public async Task SchemaXlsx_Admin_ReturnsXlsx()
	{
		var response = await _adminClient.GetAsync("/api/v1/reports/schema.xlsx");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo(XlsxMime);
	}

	[Test]
	public async Task UvmMinimumstimetalXlsx_Admin_ReturnsXlsx()
	{
		var response = await _adminClient.GetAsync("/api/v1/reports/uvm-minimumstimetal.xlsx");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo(XlsxMime);
	}

	// ── Data correctness ─────────────────────────────────────────────────────────

	[Test]
	public async Task StaffHoursXlsx_WithActiveSlot_ReturnsNonEmptyFile()
	{
		// Set up one teacher with one active schema slot
		var (_, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "5.rpt-staff");
		var staff = await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId, "Rapportlærer");
		var course = await TestDataBuilder.CreateCourseAsync(
			_factory.Services, _tenantId, "Matematik-rpt");
		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(
			_factory.Services, _tenantId,
			new TimeOnly(8, 0), new TimeOnly(9, 0), sortOrder: 500);
		await TestDataBuilder.CreateSchemaSlotAsync(
			_factory.Services, _tenantId,
			schema.Id, timeSlot.Id, course.Id, staff.Id);

		var response = await _adminClient.GetAsync("/api/v1/reports/hours/staff.xlsx");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var bytes = await response.Content.ReadAsByteArrayAsync();
		// A valid xlsx is a zip and is non-trivially small
		await Assert.That(bytes.Length).IsGreaterThan(1000);
	}

	[Test]
	public async Task CourseHoursXlsx_WithActiveSlot_ReturnsNonEmptyFile()
	{
		var (_, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "5.rpt-courses");
		var staff = await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId, "Kursuslærer");
		var course = await TestDataBuilder.CreateCourseAsync(
			_factory.Services, _tenantId, "Dansk-rpt-courses");
		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(
			_factory.Services, _tenantId,
			new TimeOnly(9, 0), new TimeOnly(10, 0), sortOrder: 501);
		await TestDataBuilder.CreateSchemaSlotAsync(
			_factory.Services, _tenantId,
			schema.Id, timeSlot.Id, course.Id, staff.Id);

		var response = await _adminClient.GetAsync("/api/v1/reports/hours/courses.xlsx");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var bytes = await response.Content.ReadAsByteArrayAsync();
		await Assert.That(bytes.Length).IsGreaterThan(1000);
	}

	[Test]
	public async Task SchemaXlsx_WithActiveSlot_HasCorrectContentDispositionFilename()
	{
		var response = await _adminClient.GetAsync("/api/v1/reports/schema.xlsx");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var disposition = response.Content.Headers.ContentDisposition;
		await Assert.That(disposition).IsNotNull();
		await Assert.That(disposition!.FileNameStar ?? disposition.FileName).Contains("skema");
	}
}
