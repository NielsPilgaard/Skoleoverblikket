using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skoleoverblikket.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFreeAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FreeAccessGrantedAt",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FreeAccessLastDay",
                table: "Subscriptions",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FreeAccessGrantedAt",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "FreeAccessLastDay",
                table: "Subscriptions");
        }
    }
}
