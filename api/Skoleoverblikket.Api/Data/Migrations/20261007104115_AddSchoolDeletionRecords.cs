using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skoleoverblikket.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolDeletionRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchoolDeletionRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolDeletionRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchoolDeletionRecords_DeletedAt",
                table: "SchoolDeletionRecords",
                column: "DeletedAt");

            // The backup agent (task 60) mirrors the deletion ledger and the migration times. These two
            // tables are the only rows it may read. The role only exists on the compose Postgres.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'backup_agent') THEN
                        GRANT SELECT ON "SchoolDeletionRecords", "__EFMigrationsHistory" TO backup_agent;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchoolDeletionRecords");
        }
    }
}
