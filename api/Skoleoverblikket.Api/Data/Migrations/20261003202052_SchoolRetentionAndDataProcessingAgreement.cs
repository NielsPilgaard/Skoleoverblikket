using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skoleoverblikket.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SchoolRetentionAndDataProcessingAgreement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CanceledAt",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletionWarningSentAt",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            // Schools canceled before this column existed: start the retention clock at their last
            // status change. Deletion still waits for the 7-day warning email (SchoolDeletionService).
            migrationBuilder.Sql(
                "UPDATE \"Subscriptions\" SET \"CanceledAt\" = \"UpdatedAt\" WHERE \"Status\" = 3;"); // SubscriptionStatus.Canceled

            migrationBuilder.CreateTable(
                name: "DataProcessingAgreementAcceptances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AcceptedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AcceptedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AcceptedByEmail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProcessingAgreementAcceptances", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataProcessingAgreementAcceptances_TenantId_Version",
                table: "DataProcessingAgreementAcceptances",
                columns: new[] { "TenantId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProcessingAgreementAcceptances");

            migrationBuilder.DropColumn(
                name: "CanceledAt",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "DeletionWarningSentAt",
                table: "Subscriptions");
        }
    }
}
