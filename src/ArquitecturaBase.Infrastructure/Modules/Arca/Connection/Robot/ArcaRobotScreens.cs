using System.Text;
using System.Text.RegularExpressions;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Robot;

/// <summary>
/// Todo lo que el robot espera ver en las pantallas de ARCA, en un solo lugar (ADR 0026): cuando ARCA cambia una pantalla se
/// corrige acá y en su página simulada de las pruebas. Los textos salen de las guías oficiales (el Administrador de
/// Relaciones, la delegación de web services, los certificados y los puntos de venta); los ids, de la página de ingreso.
/// Los textos se comparan sin mayúsculas ni tildes.
/// </summary>
internal static class ArcaRobotScreens
{
    // Ingreso con clave fiscal: primero la CUIT, después la clave.
    public const string LoginCuit = "[id='F1:username']";
    public const string LoginNext = "[id='F1:btnSiguiente']";
    public const string LoginKey = "[id='F1:password']";
    public const string LoginEnter = "[id='F1:btnIngresar']";
    public const string LoginError = "[id='F1:msg'], .alert-danger, .text-danger";
    public const string Captcha = "[id='F1:captcha'], img[alt*='aptcha'], iframe[src*='captcha'], .g-recaptcha";

    // El portal, con su buscador de servicios.
    public const string PortalSearch = "#buscadorInput";

    // Los servicios del portal.
    public const string RelationsService = "Administrador de Relaciones de Clave Fiscal";
    public const string CertificatesService = "Administración de Certificados Digitales";
    public const string PointsOfSaleService = "Administración de puntos de venta y domicilios";

    // El Administrador de Relaciones.
    public const string NewRelation = "Nueva Relación";
    public const string PendingRelations = "pendientes de aceptación";
    public const string Search = "BUSCAR";
    public const string Confirm = "CONFIRMAR";
    public const string Accept = "Aceptar";
    public const string Organism = "ARCA";
    public const string WebServices = "WebServices";
    public const string RepresentedSelect = "select[name*='epresentado'], select[id*='epresentado']";
    public const string FiscalComputerSelect = "select[name*='omputador'], select[id*='omputador']";
    public const string RepresentativeCuit = "input[name*='uitUsuario'], input[id*='uitUsuario']";
    public const string RelationDone = "constancia|ya existe|ya se encuentra";

    // Los certificados digitales.
    public const string AddAlias = "Agregar alias";
    public const string AddCertificate = "Agregar certificado";
    public const string AliasInput = "input[name*='lias'], input[id*='lias']";
    public const string RequestInput = "input[type='file']";
    public const string View = "Ver";
    public const string Download = "Descargar";

    // Los puntos de venta.
    public const string PointsOfSaleAbm = "A/B/M de Puntos de venta";
    public const string Add = "Agregar";
    public const string PointNumberInput = "input[name*='umero'], input[id*='umero']";
    public const string SystemSelect = "select[name*='istema'], select[id*='istema']";
    public const string AddressSelect = "select[name*='omicilio'], select[id*='omicilio']";
    public const string ConfirmPoint = "Confirmar";
    public const string WebServicesSystem = "web ?services?";
    public const string DroppedPoint = "baja";

    /// <summary>Un trámite que pide otra cosa antes de seguir: cambio de clave, segundo factor o aceptar condiciones.</summary>
    public const string ExtraValidation = "cambi(o|ar) (de |su )?clave|segundo factor|token|validaci[oó]n|verificaci[oó]n|t[eé]rminos y condiciones";

    /// <summary>
    /// Un texto como expresión que no distingue mayúsculas ni tildes, para buscarlo en la pantalla. Playwright pasa la
    /// expresión al navegador y sólo acepta la opción de mayúsculas: nada de <c>CultureInvariant</c>.
    /// </summary>
    public static Regex Loose(string text, bool exact = false)
    {
        var pattern = new StringBuilder(exact ? "^\\s*" : string.Empty);
        foreach (var character in text.Normalize(NormalizationForm.FormD))
        {
            if (char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            pattern.Append(char.ToLowerInvariant(character) switch
            {
                'a' => "[aá]",
                'e' => "[eé]",
                'i' => "[ií]",
                'o' => "[oó]",
                'u' => "[uúü]",
                'n' => "[nñ]",
                ' ' => "\\s+",
                // Playwright escribe la expresión entre barras en su selector: una barra sin escapar lo corta.
                '/' => "\\/",
                var other => Regex.Escape(other.ToString()),
            });
        }

        if (exact) pattern.Append("\\s*$");
        return new Regex(pattern.ToString(), RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }

    public static Regex Pattern(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>Una CUIT como la escribe ARCA en sus listas, con o sin guiones.</summary>
    public static Regex Cuit(string cuit) =>
        new($"{cuit[..2]}-?{cuit[2..10]}-?{cuit[10..]}", RegexOptions.None, TimeSpan.FromSeconds(1));
}
