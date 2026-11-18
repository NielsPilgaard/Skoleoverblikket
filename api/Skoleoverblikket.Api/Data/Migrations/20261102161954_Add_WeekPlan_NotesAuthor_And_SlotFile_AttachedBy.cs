using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skoleoverblikket.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add_WeekPlan_NotesAuthor_And_SlotFile_AttachedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AttachedAt",
                table: "WeekPlanSlotFiles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "AttachedBySubject",
                table: "WeekPlanSlotFiles",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NotesUpdatedAt",
                table: "WeekPlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotesUpdatedBy",
                table: "WeekPlans",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeekPlanSlotFiles_TenantId_AttachedBySubject_AttachedAt",
                table: "WeekPlanSlotFiles",
                columns: new[] { "TenantId", "AttachedBySubject", "AttachedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WeekPlanSlotFiles_TenantId_AttachedBySubject_AttachedAt",
                table: "WeekPlanSlotFiles");

            migrationBuilder.DropColumn(
                name: "AttachedAt",
                table: "WeekPlanSlotFiles");

            migrationBuilder.DropColumn(
                name: "AttachedBySubject",
                table: "WeekPlanSlotFiles");

            migrationBuilder.DropColumn(
                name: "NotesUpdatedAt",
                table: "WeekPlans");

            migrationBuilder.DropColumn(
                name: "NotesUpdatedBy",
                table: "WeekPlans");
        }
    }
}
