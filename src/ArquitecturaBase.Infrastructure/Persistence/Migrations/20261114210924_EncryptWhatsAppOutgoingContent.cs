using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EncryptWhatsAppOutgoingContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // H16 (ADR 0030), part 2: what a send says (its text, its buttons, why it went, its list, its footer, its rows and its
            // document's name) goes encrypted (bytea), and what there was goes empty; the sends stay. It empties more than the
            // retention does: also the sends in queue, which then fail, and the buttons of what was sent.
            // Confirmed by the user on 2026-11-14. Written by hand: PostgreSQL has no cast from text or jsonb to bytea.
            migrationBuilder.Sql("ALTER TABLE \"WhatsAppOutgoingMessages\" ALTER COLUMN \"Body\" TYPE bytea USING NULL, ALTER COLUMN \"QuickRepliesJson\" TYPE bytea USING NULL, ALTER COLUMN \"AssistantReplyJson\" TYPE bytea USING NULL, ALTER COLUMN \"ListButton\" TYPE bytea USING NULL, ALTER COLUMN \"Footer\" TYPE bytea USING NULL, ALTER COLUMN \"SpreadsheetRowsJson\" TYPE bytea USING NULL, ALTER COLUMN \"DocumentName\" TYPE bytea USING NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Encrypted content cannot go back to plain text: going back refuses, before any DDL, while there is any.
            migrationBuilder.Sql("LOCK TABLE \"WhatsAppOutgoingMessages\" IN ACCESS EXCLUSIVE MODE;");
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"WhatsAppOutgoingMessages\" WHERE \"Body\" IS NOT NULL OR \"QuickRepliesJson\" IS NOT NULL OR \"AssistantReplyJson\" IS NOT NULL OR \"ListButton\" IS NOT NULL OR \"Footer\" IS NOT NULL OR \"SpreadsheetRowsJson\" IS NOT NULL OR \"DocumentName\" IS NOT NULL) THEN RAISE EXCEPTION 'The WhatsApp chat content is encrypted: it cannot go back to plain text.'; END IF; END $$;");
            migrationBuilder.Sql("ALTER TABLE \"WhatsAppOutgoingMessages\" ALTER COLUMN \"Body\" TYPE character varying(4096) USING NULL, ALTER COLUMN \"QuickRepliesJson\" TYPE jsonb USING NULL, ALTER COLUMN \"AssistantReplyJson\" TYPE jsonb USING NULL, ALTER COLUMN \"ListButton\" TYPE character varying(20) USING NULL, ALTER COLUMN \"Footer\" TYPE character varying(60) USING NULL, ALTER COLUMN \"SpreadsheetRowsJson\" TYPE jsonb USING NULL, ALTER COLUMN \"DocumentName\" TYPE character varying(255) USING NULL;");
        }
    }
}
