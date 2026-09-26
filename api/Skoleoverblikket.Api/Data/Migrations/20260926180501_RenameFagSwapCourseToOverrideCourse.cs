using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skoleoverblikket.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameFagSwapCourseToOverrideCourse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WeekPlanSlots_Courses_FagSwapCourseId",
                table: "WeekPlanSlots");

            migrationBuilder.RenameColumn(
                name: "FagSwapCourseId",
                table: "WeekPlanSlots",
                newName: "OverrideCourseId");

            migrationBuilder.RenameIndex(
                name: "IX_WeekPlanSlots_FagSwapCourseId",
                table: "WeekPlanSlots",
                newName: "IX_WeekPlanSlots_OverrideCourseId");

            migrationBuilder.AddForeignKey(
                name: "FK_WeekPlanSlots_Courses_OverrideCourseId",
                table: "WeekPlanSlots",
                column: "OverrideCourseId",
                principalTable: "Courses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WeekPlanSlots_Courses_OverrideCourseId",
                table: "WeekPlanSlots");

            migrationBuilder.RenameColumn(
                name: "OverrideCourseId",
                table: "WeekPlanSlots",
                newName: "FagSwapCourseId");

            migrationBuilder.RenameIndex(
                name: "IX_WeekPlanSlots_OverrideCourseId",
                table: "WeekPlanSlots",
                newName: "IX_WeekPlanSlots_FagSwapCourseId");

            migrationBuilder.AddForeignKey(
                name: "FK_WeekPlanSlots_Courses_FagSwapCourseId",
                table: "WeekPlanSlots",
                column: "FagSwapCourseId",
                principalTable: "Courses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
