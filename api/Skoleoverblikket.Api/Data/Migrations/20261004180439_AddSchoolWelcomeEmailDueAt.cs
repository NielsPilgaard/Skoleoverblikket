using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skoleoverblikket.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolWelcomeEmailDueAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WelcomeEmailDueAt",
                table: "Schools",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WelcomeEmailDueAt",
                table: "Schools");
        }
    }
}
