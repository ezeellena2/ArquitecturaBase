using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EncryptWhatsAppSendContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // H16 (ADR 0030): the body and the point of a send, and why a reference ended without a message, go encrypted (bytea),
            // and what there was goes empty, as the retention does at 90 days; the sends and the references stay. Confirmed by the
            // user on 2026-11-14. Written by hand: PostgreSQL has no cast from text, numeric or jsonb to bytea.
            migrationBuilder.Sql("ALTER TABLE \"WhatsAppFlowSessions\" ALTER COLUMN \"FlowBody\" TYPE bytea USING NULL, ALTER COLUMN \"LocationLatitude\" TYPE bytea USING NULL, ALTER COLUMN \"LocationLongitude\" TYPE bytea USING NULL;");
            migrationBuilder.Sql("ALTER TABLE \"WhatsAppAssistantRequests\" ALTER COLUMN \"OutcomeJson\" TYPE bytea USING NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Encrypted content cannot go back to plain text: going back refuses, before any DDL, while there is any.
            migrationBuilder.Sql("LOCK TABLE \"WhatsAppFlowSessions\", \"WhatsAppAssistantRequests\" IN ACCESS EXCLUSIVE MODE;");
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"WhatsAppFlowSessions\" WHERE \"FlowBody\" IS NOT NULL OR \"LocationLatitude\" IS NOT NULL OR \"LocationLongitude\" IS NOT NULL) OR EXISTS (SELECT 1 FROM \"WhatsAppAssistantRequests\" WHERE \"OutcomeJson\" IS NOT NULL) THEN RAISE EXCEPTION 'The WhatsApp chat content is encrypted: it cannot go back to plain text.'; END IF; END $$;");
            migrationBuilder.Sql("ALTER TABLE \"WhatsAppFlowSessions\" ALTER COLUMN \"FlowBody\" TYPE character varying(1024) USING NULL, ALTER COLUMN \"LocationLatitude\" TYPE numeric(9,6) USING NULL, ALTER COLUMN \"LocationLongitude\" TYPE numeric(9,6) USING NULL;");
            migrationBuilder.Sql("ALTER TABLE \"WhatsAppAssistantRequests\" ALTER COLUMN \"OutcomeJson\" TYPE jsonb USING NULL;");
        }
    }
}
