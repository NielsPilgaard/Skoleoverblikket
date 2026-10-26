using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skoleoverblikket.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    VisibleToParents = table.Column<bool>(type: "boolean", nullable: false),
                    VisibleToStaff = table.Column<bool>(type: "boolean", nullable: false),
                    VisibleToBoard = table.Column<bool>(type: "boolean", nullable: false),
                    RestrictToClasses = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalLinkClasses",
                columns: table => new
                {
                    ExternalLinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalLinkClasses", x => new { x.ExternalLinkId, x.ClassId });
                    table.ForeignKey(
                        name: "FK_ExternalLinkClasses_Classes_ClassId",
                        column: x => x.ClassId,
                        principalTable: "Classes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExternalLinkClasses_ExternalLinks_ExternalLinkId",
                        column: x => x.ExternalLinkId,
                        principalTable: "ExternalLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalLinkClasses_ClassId",
                table: "ExternalLinkClasses",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalLinks_TenantId_SortOrder",
                table: "ExternalLinks",
                columns: new[] { "TenantId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalLinkClasses");

            migrationBuilder.DropTable(
                name: "ExternalLinks");
        }
    }
}
