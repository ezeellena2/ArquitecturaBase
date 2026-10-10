using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormVersionPresentationAndContactLinkRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LinkRevision",
                table: "WhatsAppContacts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Culture",
                table: "BusinessFormVersions",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameJson",
                table: "BusinessFormVersions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuestionCount",
                table: "BusinessFormVersions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmitLabelJson",
                table: "BusinessFormVersions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayNameEn",
                table: "BusinessFormVersions",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                computedColumnSql: "\"NameJson\" ->> 'en'",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayNameEs",
                table: "BusinessFormVersions",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                computedColumnSql: "\"NameJson\" ->> 'es'",
                stored: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayNameEn",
                table: "BusinessFormVersions");

            migrationBuilder.DropColumn(
                name: "DisplayNameEs",
                table: "BusinessFormVersions");

            migrationBuilder.DropColumn(
                name: "LinkRevision",
                table: "WhatsAppContacts");

            migrationBuilder.DropColumn(
                name: "Culture",
                table: "BusinessFormVersions");

            migrationBuilder.DropColumn(
                name: "NameJson",
                table: "BusinessFormVersions");

            migrationBuilder.DropColumn(
                name: "QuestionCount",
                table: "BusinessFormVersions");

            migrationBuilder.DropColumn(
                name: "SubmitLabelJson",
                table: "BusinessFormVersions");
        }
    }
}
