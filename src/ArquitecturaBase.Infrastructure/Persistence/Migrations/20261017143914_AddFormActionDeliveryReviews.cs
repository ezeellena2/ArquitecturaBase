using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormActionDeliveryReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DeliveryReviewWasSent",
                table: "WhatsAppOutgoingMessages",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryReviewedAtUtc",
                table: "WhatsAppOutgoingMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryReviewedByUserId",
                table: "WhatsAppOutgoingMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastFormActionControlRevision",
                table: "WhatsAppOutgoingMessages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "DeliveryReviewExpectedRevision",
                table: "BusinessFormActionRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DeliveryReviewRequested",
                table: "BusinessFormActionRuns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DeliveryReviewWasSent",
                table: "BusinessFormActionRuns",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryReviewedAtUtc",
                table: "BusinessFormActionRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryReviewedByUserId",
                table: "BusinessFormActionRuns",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryReviewWasSent",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "DeliveryReviewedAtUtc",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "DeliveryReviewedByUserId",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "LastFormActionControlRevision",
                table: "WhatsAppOutgoingMessages");

            migrationBuilder.DropColumn(
                name: "DeliveryReviewExpectedRevision",
                table: "BusinessFormActionRuns");

            migrationBuilder.DropColumn(
                name: "DeliveryReviewRequested",
                table: "BusinessFormActionRuns");

            migrationBuilder.DropColumn(
                name: "DeliveryReviewWasSent",
                table: "BusinessFormActionRuns");

            migrationBuilder.DropColumn(
                name: "DeliveryReviewedAtUtc",
                table: "BusinessFormActionRuns");

            migrationBuilder.DropColumn(
                name: "DeliveryReviewedByUserId",
                table: "BusinessFormActionRuns");
        }
    }
}
