using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.Delivery;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

/// <summary>
/// N16: cada tabla del núcleo con datos personales nombra quién la purga o por qué se conserva (identidad.md, «Retención
/// de los datos del ingreso»). Dato personal es, a los fines de este control, una columna cuyo nombre dice correo,
/// teléfono, destino o identificador de ingreso, IP o dirección, navegador, usuario, nombre de una persona, contacto o
/// destinatario, clave de un proveedor o el id de una persona (<see cref="PersonalColumns"/>), también la autoría de una
/// fila auditable y un contenido cifrado (<c>Protected…</c>), que puede llevar cualquiera de esos datos. Las columnas de
/// una propiedad compleja (<c>ComplexProperty</c>) cuentan como las demás. <c>Name</c> a secas no es una pista: lo tienen
/// los roles, los scopes y las aplicaciones de OpenIddict, que no son de nadie. Una tabla nueva que tenga una de
/// esas columnas hace fallar el control hasta que se sume a <see cref="Retention"/>. Las tablas de los módulos (WhatsApp,
/// Formularios, IA, ARCA) no entran: su retención la decide cada módulo (ARCA, por ejemplo, conserva lo fiscal), y uno que
/// quiera entrar suma sus tablas a esta lista con el mismo criterio.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class PersonalDataRetentionTests(ApiFactory factory)
{
    private static readonly string[] PersonalColumns =
    [
        "Email", "Phone", "Destination", "Identifier", "IpAddress", "Address", "UserAgent", "UserName", "DisplayName", "FullName", "Contact", "Recipient", "ProviderKey", "Subject", "Protected", "UserId", "SentBy", "ConsentConfirmedBy", "CreatedBy", "ModifiedBy", "UpdatedBy", "DeletedBy",
    ];

    /// <summary>Quién purga cada tabla, o por qué se conserva.</summary>
    private static readonly Dictionary<string, Retained> Retention = new(StringComparer.Ordinal)
    {
        ["LoginCodes"] = Purged<IdentityRetentionService>("a los 30 días, con el destino y la dirección de quien lo pidió"),
        ["LoginLinks"] = Purged<IdentityRetentionService>("a los 30 días; sin uso desde B4"),
        ["LoginAudits"] = Purged<IdentityRetentionService>(
            "a los 12 meses quedan sin la IP ni el navegador, y los fallidos también sin lo que la persona escribió; la cuenta, la fecha y el identificador de los exitosos quedan: el último ingreso de cada cuenta"),
        ["UserInvitations"] = Purged<IdentityRetentionService>("a los 30 días, salvo la última de cada cuenta, que muestra «Editar usuario»"),
        ["OpenIddictTokens"] = Purged<IdentityRetentionService>("con la poda de OpenIddict, a los 14 días de vencidos"),
        ["OpenIddictAuthorizations"] = Purged<IdentityRetentionService>("con la poda de OpenIddict, cuando no les queda ningún token"),
        ["OpenIddictApplications"] = Kept("los clientes de OpenIddict, del seed: su DisplayName es el nombre de la aplicación, no de una persona"),
        ["OpenIddictScopes"] = Kept("los scopes de OpenIddict, del seed: su DisplayName es el nombre del scope, no de una persona"),
        ["QueuedDeliveries"] = Purged<DeliveryQueueService>("al salir o al vencer, en un día; el contenido va cifrado"),
        ["AspNetUsers"] = Kept("la cuenta: vive mientras exista; borrar a una persona que lo pide es H14"),
        ["AspNetUserLogins"] = Kept("el vínculo con Google es parte de la cuenta"),
        ["AspNetUserRoles"] = Kept("los roles son parte de la cuenta"),
        ["AspNetUserClaims"] = Kept("parte de la cuenta"),
        ["AspNetUserTokens"] = Kept("parte de la cuenta"),
        ["StoredFiles"] = Kept("los archivos que nombran las respuestas y los documentos de Formularios: viven lo que su registro; la limpieza de archivos borra sólo los objetos sin fila"),
        ["SystemSettings"] = Kept("la configuración general; guarda quién la cambió por última vez"),
        ["AspNetRoles"] = Kept("los roles; guardan quién los creó y quién los cambió"),
        ["AdministrativeChanges"] = Kept(
            "la historia de los cambios administrativos (N13): quién cambió roles, cuentas o el modo de registro; de las personas, sólo ids, sin correos ni números; de un rol, también su nombre y su descripción"),
    };

    [Fact]
    public async Task Every_core_table_with_personal_data_names_who_purges_it_or_why_it_is_kept()
    {
        var personal = await PersonalTablesAsync();

        // El detector ve las columnas que tiene que ver: si dejara de verlas, el control pasaría en silencio.
        Assert.Contains("IpAddress", personal["LoginAudits"]);
        Assert.Contains("RequestedFromAddress", personal["LoginCodes"]);
        Assert.Contains("Subject", personal["OpenIddictTokens"]);
        Assert.Contains("ProtectedPayload", personal["QueuedDeliveries"]);
        Assert.Contains("ProviderDisplayName", personal["AspNetUserLogins"]);

        Assert.Empty(personal.Keys.Except(Retention.Keys, StringComparer.Ordinal).Select(table => $"{table}: {string.Join(", ", personal[table])}"));

        // Y la lista no guarda tablas que ya no existen o que ya no tienen datos personales.
        Assert.Empty(Retention.Keys.Except(personal.Keys, StringComparer.Ordinal));
    }

    private Task<Dictionary<string, string[]>> PersonalTablesAsync() => factory.ExecuteScopeAsync(services =>
    {
        // El modelo de producción: el TestDbContext del arnés suma la tabla de Widgets.
        using var db = new ApplicationDbContext(services.GetRequiredService<DbContextOptions<ApplicationDbContext>>());

        return Task.FromResult(db.Model.GetEntityTypes()
            .Where(entity => entity.GetTableName() is not null && entity.ClrType.Namespace?.Contains(".Modules.", StringComparison.Ordinal) != true)
            .GroupBy(entity => entity.GetTableName()!, StringComparer.Ordinal)
            .Select(table => (Table: table.Key, Columns: table
                .SelectMany(entity => ColumnsOf(entity))
                .Where(name => PersonalColumns.Any(hint => name.Contains(hint, StringComparison.Ordinal)))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray()))
            .Where(table => table.Columns.Length > 0)
            .ToDictionary(table => table.Table, table => table.Columns, StringComparer.Ordinal));
    });

    // Las columnas de las propiedades comunes y de las complejas, también las anidadas, por su nombre en la tabla.
    private static IEnumerable<string> ColumnsOf(ITypeBase type) =>
        type.GetProperties().Select(property => property.GetColumnName())
            .Concat(type.GetComplexProperties().SelectMany(complex => ColumnsOf(complex.ComplexType)));

    private static Retained Purged<TPurger>(string how) => new(typeof(TPurger), how);

    private static Retained Kept(string why) => new(null, why);

    /// <param name="PurgedBy">Quién la purga, o null si se conserva.</param>
    /// <param name="Why">Qué borra o limpia y cuándo, o por qué se conserva.</param>
    private sealed record Retained(Type? PurgedBy, string Why);
}
