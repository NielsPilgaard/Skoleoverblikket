using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skoleoverblikket.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AbsenceRegisterAndStaffAbsence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AbsenceReports_Parents_ReportedByParentId",
                table: "AbsenceReports");

            migrationBuilder.DropForeignKey(
                name: "FK_AbsenceReports_Staff_ConfirmedByStaffId",
                table: "AbsenceReports");

            migrationBuilder.DropIndex(
                name: "IX_AbsenceReports_TenantId_Status",
                table: "AbsenceReports");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "AbsenceReports",
                newName: "Category");

            migrationBuilder.RenameColumn(
                name: "ConfirmedByStaffId",
                table: "AbsenceReports",
                newName: "RegisteredByStaffId");

            migrationBuilder.RenameColumn(
                name: "ConfirmedAt",
                table: "AbsenceReports",
                newName: "DecidedAt");

            migrationBuilder.RenameIndex(
                name: "IX_AbsenceReports_ConfirmedByStaffId",
                table: "AbsenceReports",
                newName: "IX_AbsenceReports_RegisteredByStaffId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ReportedByParentId",
                table: "AbsenceReports",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "DecidedByStaffId",
                table: "AbsenceReports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HalfDay",
                table: "AbsenceReports",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LeaveStatus",
                table: "AbsenceReports",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "AbsenceReports",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Convert existing parent reports. Old Status: 0 Reported, 1 Confirmed, 2 Dismissed.
            // New Category: 0 Illness, 1 ExtraordinaryLeave, 2 Unauthorized.
            // Reported/Confirmed become parent-reported illness: the confirming staff member is not
            // the registrant, and DecidedAt is only for leave requests, so both are cleared.
            // Dismissed means staff rejected the parent's report, so it becomes staff-registered
            // ulovligt fravær (the dismisser stays as registrant), which staff can still recategorize.
            migrationBuilder.Sql("""
                UPDATE "AbsenceReports"
                SET "Category" = CASE WHEN "Category" = 2 THEN 2 ELSE 0 END,
                    "RegisteredByStaffId" = CASE WHEN "Category" = 2 THEN "RegisteredByStaffId" ELSE NULL END,
                    "DecidedAt" = NULL,
                    "UpdatedAt" = "CreatedAt";
                """);

            migrationBuilder.CreateTable(
                name: "AbsenceFollowUps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuarterStart = table.Column<DateOnly>(type: "date", nullable: false),
                    WarningSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ParentsInformedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ParentsInformedByStaffId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbsenceFollowUps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbsenceFollowUps_Staff_ParentsInformedByStaffId",
                        column: x => x.ParentsInformedByStaffId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AbsenceFollowUps_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AbsenceRetentionWarnings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolYearStart = table.Column<int>(type: "integer", nullable: false),
                    WarnedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbsenceRetentionWarnings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Checkpoint = table.Column<int>(type: "integer", nullable: false),
                    TakenByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    TakenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceChecks_Classes_ClassId",
                        column: x => x.ClassId,
                        principalTable: "Classes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttendanceChecks_Staff_TakenByStaffId",
                        column: x => x.TakenByStaffId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StaffAbsences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportedByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffAbsences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffAbsences_Staff_ReportedByStaffId",
                        column: x => x.ReportedByStaffId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StaffAbsences_Staff_StaffId",
                        column: x => x.StaffId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceReports_DecidedByStaffId",
                table: "AbsenceReports",
                column: "DecidedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceReports_TenantId_LeaveStatus",
                table: "AbsenceReports",
                columns: new[] { "TenantId", "LeaveStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceReports_TenantId_StudentId_Date",
                table: "AbsenceReports",
                columns: new[] { "TenantId", "StudentId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceFollowUps_ParentsInformedByStaffId",
                table: "AbsenceFollowUps",
                column: "ParentsInformedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceFollowUps_StudentId_QuarterStart",
                table: "AbsenceFollowUps",
                columns: new[] { "StudentId", "QuarterStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceRetentionWarnings_TenantId_SchoolYearStart",
                table: "AbsenceRetentionWarnings",
                columns: new[] { "TenantId", "SchoolYearStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceChecks_ClassId_Date_Checkpoint",
                table: "AttendanceChecks",
                columns: new[] { "ClassId", "Date", "Checkpoint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceChecks_TakenByStaffId",
                table: "AttendanceChecks",
                column: "TakenByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceChecks_TenantId_Date",
                table: "AttendanceChecks",
                columns: new[] { "TenantId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAbsences_ReportedByStaffId",
                table: "StaffAbsences",
                column: "ReportedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffAbsences_StaffId",
                table: "StaffAbsences",
                column: "StaffId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffAbsences_TenantId_Date",
                table: "StaffAbsences",
                columns: new[] { "TenantId", "Date" });

            migrationBuilder.AddForeignKey(
                name: "FK_AbsenceReports_Parents_ReportedByParentId",
                table: "AbsenceReports",
                column: "ReportedByParentId",
                principalTable: "Parents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AbsenceReports_Staff_DecidedByStaffId",
                table: "AbsenceReports",
                column: "DecidedByStaffId",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AbsenceReports_Staff_RegisteredByStaffId",
                table: "AbsenceReports",
                column: "RegisteredByStaffId",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AbsenceReports_Parents_ReportedByParentId",
                table: "AbsenceReports");

            migrationBuilder.DropForeignKey(
                name: "FK_AbsenceReports_Staff_DecidedByStaffId",
                table: "AbsenceReports");

            migrationBuilder.DropForeignKey(
                name: "FK_AbsenceReports_Staff_RegisteredByStaffId",
                table: "AbsenceReports");

            migrationBuilder.DropTable(
                name: "AbsenceFollowUps");

            migrationBuilder.DropTable(
                name: "AbsenceRetentionWarnings");

            migrationBuilder.DropTable(
                name: "AttendanceChecks");

            migrationBuilder.DropTable(
                name: "StaffAbsences");

            migrationBuilder.DropIndex(
                name: "IX_AbsenceReports_DecidedByStaffId",
                table: "AbsenceReports");

            migrationBuilder.DropIndex(
                name: "IX_AbsenceReports_TenantId_LeaveStatus",
                table: "AbsenceReports");

            migrationBuilder.DropIndex(
                name: "IX_AbsenceReports_TenantId_StudentId_Date",
                table: "AbsenceReports");

            migrationBuilder.DropColumn(
                name: "DecidedByStaffId",
                table: "AbsenceReports");

            migrationBuilder.DropColumn(
                name: "HalfDay",
                table: "AbsenceReports");

            migrationBuilder.DropColumn(
                name: "LeaveStatus",
                table: "AbsenceReports");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "AbsenceReports");

            migrationBuilder.RenameColumn(
                name: "RegisteredByStaffId",
                table: "AbsenceReports",
                newName: "ConfirmedByStaffId");

            migrationBuilder.RenameColumn(
                name: "DecidedAt",
                table: "AbsenceReports",
                newName: "ConfirmedAt");

            migrationBuilder.RenameColumn(
                name: "Category",
                table: "AbsenceReports",
                newName: "Status");

            migrationBuilder.RenameIndex(
                name: "IX_AbsenceReports_RegisteredByStaffId",
                table: "AbsenceReports",
                newName: "IX_AbsenceReports_ConfirmedByStaffId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ReportedByParentId",
                table: "AbsenceReports",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceReports_TenantId_Status",
                table: "AbsenceReports",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_AbsenceReports_Parents_ReportedByParentId",
                table: "AbsenceReports",
                column: "ReportedByParentId",
                principalTable: "Parents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AbsenceReports_Staff_ConfirmedByStaffId",
                table: "AbsenceReports",
                column: "ConfirmedByStaffId",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
