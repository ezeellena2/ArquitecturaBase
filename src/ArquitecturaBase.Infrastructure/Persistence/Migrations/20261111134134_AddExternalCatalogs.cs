using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalCatalogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormCatalogs_KindFilters",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormCatalogs_ValueType",
                table: "BusinessFormCatalogs");

            migrationBuilder.AddColumn<DateTime>(
                name: "FeedFailedAtUtc",
                table: "BusinessFormCatalogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeedFailureCode",
                table: "BusinessFormCatalogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FeedLeaseUntilUtc",
                table: "BusinessFormCatalogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FeedRefreshedAtUtc",
                table: "BusinessFormCatalogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormCatalogs_Feed",
                table: "BusinessFormCatalogs",
                sql: "\"Kind\" = 'External' OR (\"FeedRefreshedAtUtc\" IS NULL AND \"FeedFailedAtUtc\" IS NULL AND \"FeedFailureCode\" IS NULL AND \"FeedLeaseUntilUtc\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormCatalogs_KindFilters",
                table: "BusinessFormCatalogs",
                sql: "(\"Kind\" IN ('Manual','External') AND \"FiltersJson\" IS NULL) OR (\"Kind\" = 'Filters' AND \"FiltersJson\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormCatalogs_ValueType",
                table: "BusinessFormCatalogs",
                sql: "\"ValueType\" IN ('Text','Integer','Decimal','Date') AND (\"Kind\" IN ('Manual','External') OR \"ValueType\" = 'Text')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormCatalogs_Feed",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormCatalogs_KindFilters",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessFormCatalogs_ValueType",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "FeedFailedAtUtc",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "FeedFailureCode",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "FeedLeaseUntilUtc",
                table: "BusinessFormCatalogs");

            migrationBuilder.DropColumn(
                name: "FeedRefreshedAtUtc",
                table: "BusinessFormCatalogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormCatalogs_KindFilters",
                table: "BusinessFormCatalogs",
                sql: "(\"Kind\" = 'Manual' AND \"FiltersJson\" IS NULL) OR (\"Kind\" = 'Filters' AND \"FiltersJson\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessFormCatalogs_ValueType",
                table: "BusinessFormCatalogs",
                sql: "\"ValueType\" IN ('Text','Integer','Decimal','Date') AND (\"Kind\" = 'Manual' OR \"ValueType\" = 'Text')");
        }
    }
}
