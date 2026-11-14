using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EncryptWhatsAppMessageContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // H16 (ADR 0030), part 3: what a message says (its text, the titles of its buttons, why it went, its rows, the place, the
            // shared contact, the names of its files, what was prepared and what was read) goes encrypted (bytea), and what there was
            // goes empty, as the retention does; the messages stay, with their states, files and references. It empties more than
            // the retention does: also what the assistant has not read yet and a spreadsheet or a document in course, which then
            // has nothing to prepare or read. Confirmed by the user on 2026-11-14. Written by hand: PostgreSQL has no cast from text,
            // numeric or jsonb to bytea.
            migrationBuilder.Sql("ALTER TABLE \"WhatsAppMessages\" ALTER COLUMN \"Body\" TYPE bytea USING NULL, ALTER COLUMN \"QuickReplyTitlesJson\" TYPE bytea USING NULL, ALTER COLUMN \"AssistantReplyJson\" TYPE bytea USING NULL, ALTER COLUMN \"SpreadsheetRowsJson\" TYPE bytea USING NULL, ALTER COLUMN \"Latitude\" TYPE bytea USING NULL, ALTER COLUMN \"Longitude\" TYPE bytea USING NULL, ALTER COLUMN \"LocationAddress\" TYPE bytea USING NULL, ALTER COLUMN \"ContactName\" TYPE bytea USING NULL, ALTER COLUMN \"ContactPhonesJson\" TYPE bytea USING NULL, ALTER COLUMN \"SpreadsheetFileName\" TYPE bytea USING NULL, ALTER COLUMN \"SpreadsheetPreparedJson\" TYPE bytea USING NULL, ALTER COLUMN \"DocumentFileName\" TYPE bytea USING NULL, ALTER COLUMN \"DocumentReadingJson\" TYPE bytea USING NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Encrypted content cannot go back to plain text: going back refuses, before any DDL, while there is any.
            migrationBuilder.Sql("LOCK TABLE \"WhatsAppMessages\" IN ACCESS EXCLUSIVE MODE;");
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"WhatsAppMessages\" WHERE \"Body\" IS NOT NULL OR \"QuickReplyTitlesJson\" IS NOT NULL OR \"AssistantReplyJson\" IS NOT NULL OR \"SpreadsheetRowsJson\" IS NOT NULL OR \"Latitude\" IS NOT NULL OR \"Longitude\" IS NOT NULL OR \"LocationAddress\" IS NOT NULL OR \"ContactName\" IS NOT NULL OR \"ContactPhonesJson\" IS NOT NULL OR \"SpreadsheetFileName\" IS NOT NULL OR \"SpreadsheetPreparedJson\" IS NOT NULL OR \"DocumentFileName\" IS NOT NULL OR \"DocumentReadingJson\" IS NOT NULL) THEN RAISE EXCEPTION 'The WhatsApp chat content is encrypted: it cannot go back to plain text.'; END IF; END $$;");
            migrationBuilder.Sql("ALTER TABLE \"WhatsAppMessages\" ALTER COLUMN \"Body\" TYPE character varying(4096) USING NULL, ALTER COLUMN \"QuickReplyTitlesJson\" TYPE jsonb USING NULL, ALTER COLUMN \"AssistantReplyJson\" TYPE jsonb USING NULL, ALTER COLUMN \"SpreadsheetRowsJson\" TYPE jsonb USING NULL, ALTER COLUMN \"Latitude\" TYPE numeric(9,6) USING NULL, ALTER COLUMN \"Longitude\" TYPE numeric(9,6) USING NULL, ALTER COLUMN \"LocationAddress\" TYPE character varying(256) USING NULL, ALTER COLUMN \"ContactName\" TYPE character varying(256) USING NULL, ALTER COLUMN \"ContactPhonesJson\" TYPE jsonb USING NULL, ALTER COLUMN \"SpreadsheetFileName\" TYPE character varying(255) USING NULL, ALTER COLUMN \"SpreadsheetPreparedJson\" TYPE character varying(16384) USING NULL, ALTER COLUMN \"DocumentFileName\" TYPE character varying(255) USING NULL, ALTER COLUMN \"DocumentReadingJson\" TYPE character varying(131072) USING NULL;");
        }
    }
}
