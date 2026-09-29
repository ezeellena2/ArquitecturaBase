using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArquitecturaBase.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Migración de datos, escrita a mano adentro de la generada, que salió vacía: el canal se guarda como texto
    /// (HasConversion&lt;string&gt;), así que renombrar LoginCodeChannel.WhatsApp a Phone no cambia el modelo, pero las
    /// filas guardadas dicen 'WhatsApp' y el enum ya no tiene ese nombre. Solo toca LoginCodes: el canal de las
    /// invitaciones (UserInvitationChannel) sigue siendo WhatsApp. Lo prueba StoredValuesMigrationTests.
    /// </summary>
    public partial class LoginCodePhoneChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "LoginCodes" SET "Channel" = 'Phone' WHERE "Channel" = 'WhatsApp';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "LoginCodes" SET "Channel" = 'WhatsApp' WHERE "Channel" = 'Phone';""");
        }
    }
}
