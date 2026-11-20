using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FoldersAsMenus : Migration
    {
        // hashtextextended('forms:folders', 0): the key every folder write locks, FormAdvisoryLockKeys.Folders, as
        // AcquireAdvisoryLocksAsync computes it. FormFolderMenuMigrationTests compares them.
        internal const long FoldersLockKey = 7878212791369740238;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The first of two deployments (docs/guides/migracion.md, "Convivencia de versiones" and the third case): the previous version
            // still reads and writes MenuPlacement, so the DropCheckConstraint of CK_FormFolders_MenuPlacement and the DropColumn of
            // MenuPlacement that EF generated were taken out of Up, and their inverses out of Down. The next migration, in the next
            // deployment, folds again what the previous version wrote in between, adds the CHECK that ties the icon to the first level and
            // drops both (docs/plans/verificacion-y-produccion.md). Dropping the CHECK that tied the icon to the own menu only loosens: it
            // lets this version save a folder of the first level, which carries its icon; the default lets it insert one without the column.
            // First of all it takes the lock every folder write takes, inside the transaction of the migration: what the previous version
            // writes while the bundle runs waits for the commit.
            migrationBuilder.Sql(AdvisoryLockExtensions.TransactionLockStatement(FoldersLockKey));

            migrationBuilder.DropCheckConstraint(
                name: "CK_FormFolders_MenuIcon",
                table: "FormFolders");

            migrationBuilder.AlterColumn<string>(
                name: "MenuPlacement",
                table: "FormFolders",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            // CM1 to CM6: folders go up to two levels (a menu and its groups). Written by hand, as single-line statements so no line
            // break of the checkout reaches the SQL; FormFolderMenuMigrationTests migrates stored rows.
            // The folders of the third level or below, each with its ancestor of the second level.
            migrationBuilder.Sql(
                "CREATE TEMP TABLE \"FolderMenusDeep\" AS WITH RECURSIVE \"Tree\" AS ("
                + " SELECT f.\"Id\", f.\"Id\" AS \"SecondId\", 2 AS \"Level\" FROM \"FormFolders\" f"
                + " JOIN \"FormFolders\" p ON p.\"Id\" = f.\"ParentId\" WHERE p.\"ParentId\" IS NULL"
                + " UNION ALL SELECT c.\"Id\", t.\"SecondId\", t.\"Level\" + 1 FROM \"FormFolders\" c JOIN \"Tree\" t ON c.\"ParentId\" = t.\"Id\")"
                + " SELECT t.\"Id\", t.\"SecondId\", t.\"Level\", f.\"SystemKey\" FROM \"Tree\" t JOIN \"FormFolders\" f ON f.\"Id\" = t.\"Id\""
                + " WHERE t.\"Level\" >= 3;");

            // 1. Their memberships go to that ancestor, once per item (the pair is unique); the repeated ones leave with their folder.
            migrationBuilder.Sql(
                "WITH \"Moved\" AS (SELECT DISTINCT ON (d.\"SecondId\", m.\"ItemKind\", m.\"ItemId\") m.\"Id\", d.\"SecondId\""
                + " FROM \"FormFolderMemberships\" m JOIN \"FolderMenusDeep\" d ON d.\"Id\" = m.\"FolderId\""
                + " WHERE NOT EXISTS (SELECT 1 FROM \"FormFolderMemberships\" k"
                + " WHERE k.\"FolderId\" = d.\"SecondId\" AND k.\"ItemKind\" = m.\"ItemKind\" AND k.\"ItemId\" = m.\"ItemId\")"
                + " ORDER BY d.\"SecondId\", m.\"ItemKind\", m.\"ItemId\", m.\"Id\")"
                + " UPDATE \"FormFolderMemberships\" m SET \"FolderId\" = v.\"SecondId\" FROM \"Moved\" v WHERE m.\"Id\" = v.\"Id\";");

            // 3. They are deleted (the third level takes the rest along) before 2, so their system keys are free to move.
            migrationBuilder.Sql(
                "DELETE FROM \"FormFolders\" f USING \"FolderMenusDeep\" d WHERE f.\"Id\" = d.\"Id\" AND d.\"Level\" = 3;");

            // 2. An ancestor without a system key takes the one of the first of them (the oldest id) that had one.
            migrationBuilder.Sql(
                "UPDATE \"FormFolders\" s SET \"SystemKey\" = k.\"SystemKey\""
                + " FROM (SELECT DISTINCT ON (\"SecondId\") \"SecondId\", \"SystemKey\" FROM \"FolderMenusDeep\""
                + " WHERE \"SystemKey\" IS NOT NULL ORDER BY \"SecondId\", \"Id\") k"
                + " WHERE s.\"Id\" = k.\"SecondId\" AND s.\"SystemKey\" IS NULL;");

            migrationBuilder.Sql("DROP TABLE \"FolderMenusDeep\";");

            // 4. Every folder of the first level is a menu with its icon, or the folder. A subfolder keeps the icon it had: the previous
            // version still shows it as its own menu and this one does not read it; the next migration takes it away. MenuPlacement stays
            // as it was, so the previous version keeps showing what it showed.
            migrationBuilder.Sql("UPDATE \"FormFolders\" SET \"MenuIcon\" = 'folder' WHERE \"ParentId\" IS NULL AND \"MenuIcon\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "MenuPlacement",
                table: "FormFolders",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldDefaultValue: "None");

            // Up leaves MenuPlacement as it was, so it still says what is a menu of the previous version: only its own menus keep an
            // icon, or take the folder, as the old CHECK asks. The folders deleted on the way up do not come back.
            migrationBuilder.Sql("UPDATE \"FormFolders\" SET \"MenuIcon\" = NULL WHERE \"MenuPlacement\" <> 'Own';");
            migrationBuilder.Sql("UPDATE \"FormFolders\" SET \"MenuIcon\" = 'folder' WHERE \"MenuPlacement\" = 'Own' AND \"MenuIcon\" IS NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FormFolders_MenuIcon",
                table: "FormFolders",
                sql: "(\"MenuPlacement\" = 'Own') = (\"MenuIcon\" IS NOT NULL)");
        }
    }
}
