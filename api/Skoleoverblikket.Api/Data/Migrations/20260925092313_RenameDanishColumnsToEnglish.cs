using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skoleoverblikket.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameDanishColumnsToEnglish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "StaaMaalMedSnapshots",
                newName: "ComplianceCoverageSnapshots");

            migrationBuilder.RenameColumn(
                name: "Beskrivelse",
                table: "WeekPlanSlots",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "Generelt",
                table: "WeekPlans",
                newName: "Notes");

            migrationBuilder.RenameColumn(
                name: "Beskrivelse",
                table: "SfoWeekPlanShifts",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "Generelt",
                table: "SfoWeekPlans",
                newName: "Notes");

            migrationBuilder.Sql(
                "ALTER TABLE \"ComplianceCoverageSnapshots\" RENAME CONSTRAINT \"PK_StaaMaalMedSnapshots\" TO \"PK_ComplianceCoverageSnapshots\";");

            migrationBuilder.Sql(
                "ALTER TABLE \"ComplianceCoverageSnapshots\" RENAME CONSTRAINT \"FK_StaaMaalMedSnapshots_Staff_CreatedByStaffId\" TO \"FK_ComplianceCoverageSnapshots_Staff_CreatedByStaffId\";");

            migrationBuilder.RenameIndex(
                name: "IX_StaaMaalMedSnapshots_CreatedByStaffId",
                table: "ComplianceCoverageSnapshots",
                newName: "IX_ComplianceCoverageSnapshots_CreatedByStaffId");

            migrationBuilder.RenameIndex(
                name: "IX_StaaMaalMedSnapshots_TenantId_CreatedAt",
                table: "ComplianceCoverageSnapshots",
                newName: "IX_ComplianceCoverageSnapshots_TenantId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_StaaMaalMedSnapshots_TenantId_SchoolYear",
                table: "ComplianceCoverageSnapshots",
                newName: "IX_ComplianceCoverageSnapshots_TenantId_SchoolYear");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Description",
                table: "WeekPlanSlots",
                newName: "Beskrivelse");

            migrationBuilder.RenameColumn(
                name: "Notes",
                table: "WeekPlans",
                newName: "Generelt");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "SfoWeekPlanShifts",
                newName: "Beskrivelse");

            migrationBuilder.RenameColumn(
                name: "Notes",
                table: "SfoWeekPlans",
                newName: "Generelt");

            migrationBuilder.RenameIndex(
                name: "IX_ComplianceCoverageSnapshots_CreatedByStaffId",
                table: "ComplianceCoverageSnapshots",
                newName: "IX_StaaMaalMedSnapshots_CreatedByStaffId");

            migrationBuilder.RenameIndex(
                name: "IX_ComplianceCoverageSnapshots_TenantId_CreatedAt",
                table: "ComplianceCoverageSnapshots",
                newName: "IX_StaaMaalMedSnapshots_TenantId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_ComplianceCoverageSnapshots_TenantId_SchoolYear",
                table: "ComplianceCoverageSnapshots",
                newName: "IX_StaaMaalMedSnapshots_TenantId_SchoolYear");

            migrationBuilder.Sql(
                "ALTER TABLE \"ComplianceCoverageSnapshots\" RENAME CONSTRAINT \"PK_ComplianceCoverageSnapshots\" TO \"PK_StaaMaalMedSnapshots\";");

            migrationBuilder.Sql(
                "ALTER TABLE \"ComplianceCoverageSnapshots\" RENAME CONSTRAINT \"FK_ComplianceCoverageSnapshots_Staff_CreatedByStaffId\" TO \"FK_StaaMaalMedSnapshots_Staff_CreatedByStaffId\";");

            migrationBuilder.RenameTable(
                name: "ComplianceCoverageSnapshots",
                newName: "StaaMaalMedSnapshots");
        }
    }
}
