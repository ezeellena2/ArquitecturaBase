using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;

/// <summary>Una relación del Administrador de Relaciones: quién representa a quién para un servicio, y si ya se aceptó.</summary>
internal sealed record FakeArcaWebRelation(string Represented, string Service, string Representative, bool Accepted);

/// <summary>Un alias de «Administración de Certificados Digitales» con sus certificados, el más nuevo primero.</summary>
internal sealed record FakeArcaWebAlias(string Owner, string Alias, List<string> Certificates);

/// <summary>Un punto de venta de «Administración de puntos de venta y domicilios».</summary>
internal sealed record FakeArcaWebPoint(string Owner, int Number, string System, bool Dropped);

/// <summary>
/// Las pantallas de ARCA que maneja el robot (ADR 0026), simuladas en un Kestrel local: el ingreso con clave fiscal, el
/// portal, el Administrador de Relaciones, los certificados digitales y los puntos de venta. Siguen lo que esperan
/// <c>ArcaRobotScreens</c> y las guías oficiales; cuando ARCA cambia una pantalla, se cambian las dos. Deja programar un
/// captcha, una validación extra o una pantalla rota, y guarda lo que el robot hizo para revisarlo. Es una por proceso:
/// cada test empieza con <see cref="Reset"/>.
/// </summary>
internal sealed class FakeArcaWeb : IAsyncDisposable
{
    public const string ClientLogin = "20284567898";
    public const string ClientKey = "clave-del-cliente";
    public const string Company = "20000000060";
    public const string Platform = "30719998883";
    public const string PlatformKey = "clave-de-la-plataforma";

    private static readonly Lazy<FakeArcaWeb> SharedInstance = new(() => Start(), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["wsfe"] = "Facturación Electrónica",
        ["ws_sr_constancia_inscripcion"] = "Consulta de Constancia de Inscripción",
    };

    private readonly Lock gate = new();
    private readonly WebApplication app;
    private readonly List<string> logins = [];

    private FakeArcaWeb(WebApplication app) => this.app = app;

    public static FakeArcaWeb Shared => SharedInstance.Value;

    public Uri LoginUrl { get; private set; } = null!;

    public bool Captcha { get; set; }
    public bool ExtraValidation { get; set; }

    /// <summary>Un servicio que responde una pantalla distinta de la esperada: «relations», «certs» o «pos».</summary>
    public string? Broken { get; set; }

    /// <summary>Cómo emite ARCA el certificado de un pedido; sin él, el alta del alias falla.</summary>
    public Func<string, string>? IssueCertificate { get; set; }

    public Dictionary<string, string> Keys { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string[]> Represents { get; } = new(StringComparer.Ordinal);
    public List<FakeArcaWebRelation> Relations { get; } = [];
    public List<FakeArcaWebAlias> Aliases { get; } = [];
    public List<FakeArcaWebPoint> Points { get; } = [];
    public string[] Systems { get; set; } = [];

    public int Logins(string cuit)
    {
        lock (gate) return logins.Count(login => login == cuit);
    }

    public void Reset()
    {
        lock (gate)
        {
            Captcha = false;
            ExtraValidation = false;
            Broken = null;
            IssueCertificate = null;
            logins.Clear();
            Keys.Clear();
            Keys[ClientLogin] = ClientKey;
            Keys[Platform] = PlatformKey;
            Represents.Clear();
            Represents[ClientLogin] = [ClientLogin, Company];
            Relations.Clear();
            Aliases.Clear();
            Points.Clear();
            Points.Add(new FakeArcaWebPoint(Company, 1, "Factura en Línea - Monotributo", false));
            Systems = ["Factura en Línea - Monotributo", "Factura Electrónica - Monotributo - Web Services"];
        }
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();

    private static FakeArcaWeb Start()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var web = new FakeArcaWeb(app);
        web.Reset();
        web.Map(app);
        app.StartAsync().GetAwaiter().GetResult();
        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        web.LoginUrl = new Uri(address.TrimEnd('/') + "/login");
        return web;
    }

    private void Map(WebApplication routes)
    {
        routes.MapGet("/login", () => Page("Ingreso", LoginForm(null)));
        routes.MapPost("/login/next", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var username = Digits(form["username"]);
            if (Captcha) return Page("Ingreso", LoginForm(null) + "<img id=\"F1:captcha\" alt=\"captcha\" width=\"120\" height=\"40\" src=\"data:,\">");
            if (!Keys.ContainsKey(username)) return Page("Ingreso", LoginForm("Número de CUIL/CUIT incorrecto"));
            return Page("Ingreso", $"<form method=\"post\" action=\"/login/enter\"><input type=\"hidden\" name=\"username\" value=\"{username}\">"
                + "<label for=\"F1:password\">Clave</label><input id=\"F1:password\" name=\"password\" type=\"password\">"
                + "<button id=\"F1:btnIngresar\" type=\"submit\">Ingresar</button></form>");
        });
        routes.MapPost("/login/enter", async (HttpRequest request, HttpResponse response) =>
        {
            var form = await request.ReadFormAsync();
            var username = Digits(form["username"]);
            lock (gate) logins.Add(username);
            if (!Keys.TryGetValue(username, out var key) || key != form["password"]) return Page("Ingreso", LoginForm("Clave o usuario incorrecto"));
            if (ExtraValidation) return Page("Cambio de clave", "<h1>Cambio de clave obligatorio</h1><p>Tenés que cambiar su clave para seguir.</p>");
            response.Cookies.Append("fake-arca", username);
            return Results.Redirect("/portal");
        });
        routes.MapGet("/portal", () => Page("Portal", "<input id=\"buscadorInput\" placeholder=\"Buscar\"><ul>"
            + "<li><a href=\"/svc/relations\" target=\"_blank\">Administrador de Relaciones de Clave Fiscal</a></li>"
            + "<li><a href=\"/svc/certs\" target=\"_blank\">Administración de Certificados Digitales</a></li>"
            + "<li><a href=\"/svc/pos\" target=\"_blank\">Administración de puntos de venta y domicilios</a></li></ul>"));
        routes.MapGet("/svc/{service}", (string service, HttpRequest request) =>
        {
            var login = User(request);
            var represented = Represents.GetValueOrDefault(login) ?? [login];
            if (represented.Length == 1) return Results.Redirect($"/svc/{service}/home?rep={login}");
            var links = string.Concat(represented.Select(cuit => $"<a href=\"/svc/{service}/home?rep={cuit}\">{Format(cuit)}</a> "));
            return Page("Representar", $"<h1>Seleccioná a quién vas a representar</h1>{links}");
        });

        MapRelations(routes);
        MapCertificates(routes);
        MapPoints(routes);
    }

    private void MapRelations(WebApplication routes)
    {
        routes.MapGet("/svc/relations/home", (string rep) => Broken == "relations"
            ? Page("Mantenimiento", "<p>El servicio está en mantenimiento.</p>")
            : Page("Administrador de Relaciones", $"<a href=\"/rel/new?rep={rep}&representado={rep}\">Nueva Relación</a> "
                + $"<a href=\"/rel/pending?rep={rep}\">Consultar autorizaciones pendientes de aceptación</a>"));

        routes.MapGet("/rel/new", (HttpRequest request) =>
        {
            var state = State(request);
            if (request.Query["computador"] is { Count: > 0 } computer && computer[0] is { Length: > 0 } alias) state = state with { Repr = alias };
            else if (request.Query["cuitUsuario"] is { Count: > 0 } user && Digits(user) is { Length: 11 } cuit) state = state with { Repr = cuit };
            var options = string.Concat(Representable(state.Rep).Select(cuit =>
                $"<option value=\"{cuit}\"{(cuit == state.Represented ? " selected" : string.Empty)}>{Format(cuit)}</option>"));
            return Page("Incorporar nueva Relación", "<form method=\"get\" action=\"/rel/new\">" + Hidden(state, includeRepresented: false)
                + "<table><tr><td>Representado</td><td><select name=\"representado\">" + options + "</select></td></tr>"
                + $"<tr><td>Servicio</td><td>{Encode(state.Service is null ? "-" : Labels[state.Service])} <button type=\"submit\" formaction=\"/rel/pick-org\">BUSCAR</button></td></tr>"
                + $"<tr><td>Representante</td><td>{Encode(state.Repr ?? "-")} <button type=\"submit\" formaction=\"/rel/pick-rep\">BUSCAR</button></td></tr>"
                + "</table><button type=\"submit\" formaction=\"/rel/confirm\" formmethod=\"post\">CONFIRMAR</button></form>");
        });
        routes.MapGet("/rel/pick-org", (HttpRequest request) =>
            Page("Organismo", $"<a href=\"/rel/pick-kind?{Query(State(request))}\">ARCA</a> <a href=\"#\">ANSES</a>"));
        routes.MapGet("/rel/pick-kind", (HttpRequest request) =>
            Page("Tipo", $"<a href=\"#\">Servicios Interactivos</a> <a href=\"/rel/pick-service?{Query(State(request))}\">WebServices</a>"));
        routes.MapGet("/rel/pick-service", (HttpRequest request) =>
        {
            var state = State(request);
            var services = state.Represented == state.Rep
                ? Labels.Keys
                : Relations.Where(relation => relation.Represented == state.Represented && relation.Representative == state.Rep && relation.Accepted)
                    .Select(relation => relation.Service).Distinct();
            return Page("Servicios", string.Concat(services.Select(service =>
                $"<a href=\"/rel/new?{Query(state with { Service = service })}\">{Encode(Labels[service])}</a> ")));
        });
        routes.MapGet("/rel/pick-rep", (HttpRequest request) =>
        {
            var state = State(request);
            var aliases = string.Concat(Aliases.Where(alias => alias.Owner == state.Rep).Select(alias => $"<option>{Encode(alias.Alias)}</option>"));
            return Page("Selección del Representante a autorizar", "<form method=\"get\" action=\"/rel/new\">" + Hidden(state, includeRepresented: true)
                + $"<label>Computador Fiscal</label><select name=\"computador\"><option value=\"\"></option>{aliases}</select>"
                + "<label>CUIT/CUIL/CDI Usuario</label><input name=\"cuitUsuario\"><button type=\"submit\">BUSCAR</button></form>");
        });
        routes.MapPost("/rel/confirm", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var state = new RelationState(form["rep"]!, form["representado"]!, Empty(form["svc"]), Empty(form["repr"]));
            if (state.Service is null || state.Repr is null) return Page("Error", "<p>Faltan datos.</p>");
            lock (gate)
            {
                if (Relations.Any(relation => relation.Represented == state.Represented && relation.Service == state.Service && relation.Representative == state.Repr))
                    return Page("Relación", "<p>La relación ya existe.</p>");
                var computer = Aliases.Any(alias => alias.Owner == state.Rep && alias.Alias == state.Repr);
                Relations.Add(new FakeArcaWebRelation(state.Represented, state.Service, state.Repr, Accepted: computer));
            }

            return Page("Relación", "<p>Se generó la constancia F3283/E.</p>");
        });
        routes.MapGet("/rel/pending", (string rep) =>
        {
            var rows = Relations.Select((relation, index) => (relation, index)).Where(item => item.relation.Representative == rep && !item.relation.Accepted)
                .Select(item => $"<tr><td>{Format(item.relation.Represented)}</td><td>{Encode(Labels[item.relation.Service])}</td><td>"
                    + $"<form method=\"post\" action=\"/rel/accept\"><input type=\"hidden\" name=\"index\" value=\"{item.index}\"><input type=\"hidden\" name=\"rep\" value=\"{rep}\">"
                    + "<button type=\"submit\">Aceptar</button></form></td></tr>");
            return Page("Mis Relaciones Pendientes", $"<table><tr><th>Representado</th><th>Servicio</th><th></th></tr>{string.Concat(rows)}</table>");
        });
        routes.MapPost("/rel/accept", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var index = int.Parse(form["index"]!, CultureInfo.InvariantCulture);
            lock (gate) Relations[index] = Relations[index] with { Accepted = true };
            return Page("Aceptación", $"<p>Relación aceptada. Se generó la constancia.</p><a href=\"/rel/pending?rep={form["rep"]}\">Volver</a>");
        });
    }

    private void MapCertificates(WebApplication routes)
    {
        routes.MapGet("/svc/certs/home", (string rep) =>
        {
            if (Broken == "certs") return Page("Mantenimiento", "<p>El servicio está en mantenimiento.</p>");
            var rows = Aliases.Where(alias => alias.Owner == rep)
                .Select(alias => $"<tr><td>{Encode(alias.Alias)}</td><td><a href=\"/certs/alias?rep={rep}&alias={alias.Alias}\">Ver</a></td></tr>");
            return Page("Certificados", $"<table>{string.Concat(rows)}</table><a href=\"/certs/new?rep={rep}\">Agregar alias</a>");
        });
        routes.MapGet("/certs/new", (string rep) => Page("Agregar alias", "<form method=\"post\" action=\"/certs/new\" enctype=\"multipart/form-data\">"
            + $"<input type=\"hidden\" name=\"rep\" value=\"{rep}\"><label>Nombre simbólico del DN</label><input name=\"alias\">"
            + "<input type=\"file\" name=\"csr\"><button type=\"submit\">Agregar alias</button></form>"));
        routes.MapPost("/certs/new", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var (rep, alias) = (form["rep"].ToString(), form["alias"].ToString());
            if (await IssueAsync(form) is not { } certificate) return Page("Error", "<p>No se pudo emitir el certificado.</p>");
            lock (gate) Aliases.Add(new FakeArcaWebAlias(rep, alias, [certificate]));
            return Results.Redirect($"/certs/alias?rep={rep}&alias={alias}");
        }).DisableAntiforgery();
        routes.MapGet("/certs/alias", (string rep, string alias) =>
        {
            var found = Aliases.Single(item => item.Owner == rep && item.Alias == alias);
            var links = string.Concat(found.Certificates.Select((_, index) =>
                $"<li><a href=\"/certs/download?rep={rep}&alias={alias}&n={index}\">Descargar</a></li>"));
            return Page(alias, $"<ul>{links}</ul><form method=\"post\" action=\"/certs/add\" enctype=\"multipart/form-data\">"
                + $"<input type=\"hidden\" name=\"rep\" value=\"{rep}\"><input type=\"hidden\" name=\"alias\" value=\"{alias}\">"
                + "<input type=\"file\" name=\"csr\"><button type=\"submit\">Agregar certificado</button></form>");
        });
        routes.MapPost("/certs/add", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var (rep, alias) = (form["rep"].ToString(), form["alias"].ToString());
            if (await IssueAsync(form) is not { } certificate) return Page("Error", "<p>No se pudo emitir el certificado.</p>");
            lock (gate) Aliases.Single(item => item.Owner == rep && item.Alias == alias).Certificates.Insert(0, certificate);
            return Results.Redirect($"/certs/alias?rep={rep}&alias={alias}");
        }).DisableAntiforgery();
        routes.MapGet("/certs/download", (string rep, string alias, int n) =>
            Results.File(Encoding.ASCII.GetBytes(Aliases.Single(item => item.Owner == rep && item.Alias == alias).Certificates[n]),
                "application/x-x509-ca-cert", $"{alias}.crt"));
    }

    private void MapPoints(WebApplication routes)
    {
        routes.MapGet("/svc/pos/home", (string rep) => Page("Puntos de venta", $"<a href=\"/pos/abm?rep={rep}\">A/B/M de Puntos de venta</a>"));
        routes.MapGet("/pos/abm", (string rep) =>
        {
            if (Broken == "pos") return Page("Mantenimiento", "<p>El servicio está en mantenimiento.</p>");
            var rows = Points.Where(point => point.Owner == rep).Select(point =>
                $"<tr><td>{point.Number:0000}</td><td>{Encode(point.System)}</td><td>{(point.Dropped ? "Baja" : "Vigente")}</td></tr>");
            return Page("Puntos de venta", $"<table><tr><th>Número</th><th>Sistema</th><th>Estado</th></tr>{string.Concat(rows)}</table>"
                + $"<a href=\"/pos/new?rep={rep}\">Agregar</a>");
        });
        routes.MapGet("/pos/new", (string rep) =>
        {
            var next = Points.Where(point => point.Owner == rep).Select(point => point.Number).DefaultIfEmpty(0).Max() + 1;
            var systems = string.Concat(Systems.Select(system => $"<option>{Encode(system)}</option>"));
            return Page("Alta de Punto de Venta", $"<form method=\"post\" action=\"/pos/new\"><input type=\"hidden\" name=\"rep\" value=\"{rep}\">"
                + $"<label>Número</label><input name=\"numero\" value=\"{next}\"><label>Nombre Fantasía</label><input name=\"fantasia\">"
                + $"<label>Sistema</label><select name=\"sistema\">{systems}</select>"
                + "<label>Domicilio</label><select name=\"domicilio\"><option>Ruta 9 km 400</option></select><button type=\"submit\">Aceptar</button></form>");
        });
        routes.MapPost("/pos/new", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            return Page("Confirmar", "<form method=\"post\" action=\"/pos/confirm\">"
                + string.Concat(new[] { "rep", "numero", "sistema" }.Select(name => $"<input type=\"hidden\" name=\"{name}\" value=\"{Encode(form[name]!)}\">"))
                + "<button type=\"submit\">Confirmar</button></form>");
        }).DisableAntiforgery();
        routes.MapPost("/pos/confirm", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var rep = form["rep"].ToString();
            lock (gate) Points.Add(new FakeArcaWebPoint(rep, int.Parse(form["numero"]!, CultureInfo.InvariantCulture), form["sistema"]!, false));
            return Results.Redirect($"/pos/abm?rep={rep}");
        }).DisableAntiforgery();
    }

    private async Task<string?> IssueAsync(IFormCollection form)
    {
        if (IssueCertificate is null || form.Files["csr"] is not { } file) return null;
        using var reader = new StreamReader(file.OpenReadStream());
        return IssueCertificate(await reader.ReadToEndAsync());
    }

    /// <summary>La CUIT propia y las empresas que delegaron en ella algún servicio ya aceptado.</summary>
    private IEnumerable<string> Representable(string rep) =>
        new[] { rep }.Concat(Relations.Where(relation => relation.Representative == rep && relation.Accepted).Select(relation => relation.Represented)).Distinct();

    private static string LoginForm(string? error) =>
        (error is null ? string.Empty : $"<div id=\"F1:msg\" class=\"alert-danger\">{Encode(error)}</div>")
        + "<form method=\"post\" action=\"/login/next\"><label for=\"F1:username\">CUIT/CUIL</label><input id=\"F1:username\" name=\"username\">"
        + "<button id=\"F1:btnSiguiente\" type=\"submit\">Siguiente</button></form>";

    private static IResult Page(string title, string body) => Results.Content(
        $"<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>{Encode(title)}</title></head><body>{body}</body></html>",
        "text/html; charset=utf-8");

    private static string User(HttpRequest request) => request.Cookies["fake-arca"] ?? string.Empty;

    private static RelationState State(HttpRequest request) =>
        new(request.Query["rep"]!, request.Query["representado"].ToString() is { Length: > 0 } represented ? represented : request.Query["rep"]!,
            Empty(request.Query["svc"]), Empty(request.Query["repr"]));

    private static string Hidden(RelationState state, bool includeRepresented) =>
        $"<input type=\"hidden\" name=\"rep\" value=\"{state.Rep}\">"
        + (includeRepresented ? $"<input type=\"hidden\" name=\"representado\" value=\"{state.Represented}\">" : string.Empty)
        + $"<input type=\"hidden\" name=\"svc\" value=\"{state.Service}\"><input type=\"hidden\" name=\"repr\" value=\"{Encode(state.Repr ?? string.Empty)}\">";

    private static string Query(RelationState state) =>
        $"rep={state.Rep}&representado={state.Represented}&svc={state.Service}&repr={WebUtility.UrlEncode(state.Repr)}";

    private static string? Empty(Microsoft.Extensions.Primitives.StringValues value) => value.ToString() is { Length: > 0 } text ? text : null;

    private static string Digits(Microsoft.Extensions.Primitives.StringValues value) => new([.. value.ToString().Where(char.IsAsciiDigit)]);

    private static string Format(string cuit) => $"{cuit[..2]}-{cuit[2..10]}-{cuit[10..]}";

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    private sealed record RelationState(string Rep, string Represented, string? Service, string? Repr);
}
