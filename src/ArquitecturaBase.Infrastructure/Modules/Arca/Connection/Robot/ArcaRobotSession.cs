using System.Text.RegularExpressions;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using Microsoft.Playwright;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Robot;

/// <summary>El robot se frenó con un motivo estable; lo traduce a un resultado el adaptador, nunca sale de él.</summary>
internal sealed class ArcaRobotStoppedException(ArcaRobotFailure failure) : Exception($"The ARCA robot stopped: {failure}.")
{
    public ArcaRobotFailure Failure { get; } = failure;
}

/// <summary>
/// Una sesión del navegador en ARCA (ADR 0026): el portal después del ingreso y la página del servicio abierto. Sus acciones
/// esperan lo que dice <see cref="ArcaRobotScreens"/> y traducen lo que no aparece a un motivo: un captcha, una validación
/// extra o una pantalla cambiada. Nunca guarda ni devuelve lo que se escribió.
/// </summary>
internal sealed class ArcaRobotSession
{
    private readonly IPage portal;

    public ArcaRobotSession(IBrowserContext context, IPage portal)
    {
        Context = context;
        this.portal = portal;
        Page = portal;
    }

    public IBrowserContext Context { get; }

    public IPage Page { get; private set; }

    /// <summary>Entra con la CUIT o CUIL y la clave; la clave se escribe en el campo y no queda en ningún otro lado.</summary>
    public async Task LoginAsync(Uri loginUrl, string loginCuit, Func<string> key)
    {
        var response = await Guard(() => portal.GotoAsync(loginUrl.ToString()));
        if (response is null || response.Status >= 500) throw new ArcaRobotStoppedException(ArcaRobotFailure.Unavailable);
        await ThrowIfBlockedAsync();
        await Guard(() => portal.Locator(ArcaRobotScreens.LoginCuit).FillAsync(loginCuit));
        await Guard(() => portal.Locator(ArcaRobotScreens.LoginNext).ClickAsync());
        await WaitForAnyAsync(ArcaRobotScreens.LoginKey);

        await Guard(() => portal.Locator(ArcaRobotScreens.LoginKey).FillAsync(key()));
        await Guard(() => portal.Locator(ArcaRobotScreens.LoginEnter).ClickAsync());
        await WaitForAnyAsync(ArcaRobotScreens.PortalSearch);
        Page = portal;
    }

    /// <summary>Abre un servicio desde el buscador del portal y, si ARCA pregunta a quién se representa, elige la CUIT.</summary>
    public async Task OpenServiceAsync(string service, string representedCuit)
    {
        await Guard(() => portal.Locator(ArcaRobotScreens.PortalSearch).FillAsync(service));
        var link = portal.GetByRole(AriaRole.Link, new() { NameRegex = ArcaRobotScreens.Loose(service) }).First;
        var opened = await Guard(() => portal.RunAndWaitForPopupAsync(() => link.ClickAsync()));
        await Guard(() => opened.WaitForLoadStateAsync(LoadState.DOMContentLoaded));
        Page = opened;
        await ThrowIfBlockedAsync();

        var chooser = Page.GetByText(ArcaRobotScreens.Pattern("represent"));
        var choice = Page.GetByRole(AriaRole.Button, new() { NameRegex = ArcaRobotScreens.Cuit(representedCuit) })
            .Or(Page.GetByRole(AriaRole.Link, new() { NameRegex = ArcaRobotScreens.Cuit(representedCuit) }));
        if (await chooser.CountAsync() > 0 && await choice.CountAsync() > 0) await Guard(() => choice.First.ClickAsync());
    }

    public Task ClickAsync(string text, bool exact = true) => Guard(() => Clickable(text, exact).First.ClickAsync());

    public async Task<bool> ClickIfPresentAsync(string text)
    {
        var target = Clickable(text);
        if (await target.CountAsync() == 0) return false;
        await Guard(() => target.First.ClickAsync());
        return true;
    }

    /// <summary>El botón de una fila de un formulario en tabla, por el rótulo de la fila (el «BUSCAR» de «Servicio»).</summary>
    public Task ClickInRowAsync(Regex row, string text) =>
        Guard(() => Page.Locator("tr").Filter(new() { HasTextRegex = row }).Last
            .GetByRole(AriaRole.Button, new() { NameRegex = ArcaRobotScreens.Loose(text, exact: true) }).First.ClickAsync());

    public ILocator Rows(Regex text) => Page.Locator("tr").Filter(new() { HasTextRegex = text });

    public Task FillAsync(string selector, string value) => Guard(() => Page.Locator(selector).First.FillAsync(value));

    public Task<string> ValueAsync(string selector) => Guard(() => Page.Locator(selector).First.InputValueAsync());

    public Task SetFileAsync(string selector, string name, string mimeType, byte[] content) =>
        Guard(() => Page.Locator(selector).First.SetInputFilesAsync(new FilePayload { Name = name, MimeType = mimeType, Buffer = content }));

    /// <summary>Elige la primera opción cuyo texto coincide; sin ninguna, la pantalla no es la esperada.</summary>
    public async Task SelectAsync(string selector, Regex option)
    {
        var select = Page.Locator(selector).First;
        var labels = await Guard(() => select.Locator("option").AllInnerTextsAsync());
        var match = labels.FirstOrDefault(option.IsMatch) ?? throw new ArcaRobotStoppedException(ArcaRobotFailure.ScreenChanged);
        await Guard(() => select.SelectOptionAsync(new SelectOptionValue { Label = match }));
    }

    public async Task<bool> SelectIfPresentAsync(string selector, Regex option)
    {
        if (await Page.Locator(selector).CountAsync() == 0) return false;
        await SelectAsync(selector, option);
        return true;
    }

    public async Task<bool> HasAsync(Regex text) => await Page.GetByText(text).CountAsync() > 0;

    /// <summary>Espera un texto que confirma el paso; si no llega, la pantalla cambió.</summary>
    public Task ExpectAsync(Regex text) => Guard(() => Page.GetByText(text).First.WaitForAsync());

    public async Task<string> DownloadTextAsync(string linkText)
    {
        var download = await Guard(() => Page.RunAndWaitForDownloadAsync(() => Clickable(linkText).First.ClickAsync()));
        try
        {
            await using var stream = await Guard(() => download.CreateReadStreamAsync());
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
        finally
        {
            await download.DeleteAsync();
        }
    }

    public Task CloseServiceAsync() => Page == portal ? Task.CompletedTask : Page.CloseAsync();

    private ILocator Clickable(string text, bool exact = true)
    {
        var name = ArcaRobotScreens.Loose(text, exact);
        return Page.GetByRole(AriaRole.Button, new() { NameRegex = name }).Or(Page.GetByRole(AriaRole.Link, new() { NameRegex = name }));
    }

    /// <summary>
    /// Espera lo esperado. Si en cambio aparece un captcha, un error de ingreso o un trámite extra, se frena con ese motivo;
    /// si no aparece nada conocido, la pantalla cambió.
    /// </summary>
    private async Task WaitForAnyAsync(string expected)
    {
        var any = portal.Locator($"{expected}, {ArcaRobotScreens.LoginError}, {ArcaRobotScreens.Captcha}").First;
        try
        {
            await any.WaitForAsync();
        }
        catch (TimeoutException)
        {
            await ThrowIfBlockedAsync();
            throw new ArcaRobotStoppedException(ArcaRobotFailure.ScreenChanged);
        }

        if (await portal.Locator(expected).CountAsync() > 0 && await portal.Locator(expected).First.IsVisibleAsync()) return;
        await ThrowIfBlockedAsync();
        if (await portal.Locator(ArcaRobotScreens.LoginError).CountAsync() > 0) throw new ArcaRobotStoppedException(ArcaRobotFailure.LoginRejected);
        throw new ArcaRobotStoppedException(ArcaRobotFailure.ScreenChanged);
    }

    private async Task ThrowIfBlockedAsync()
    {
        if (await Page.Locator(ArcaRobotScreens.Captcha).CountAsync() > 0 || await portal.Locator(ArcaRobotScreens.Captcha).CountAsync() > 0)
            throw new ArcaRobotStoppedException(ArcaRobotFailure.Captcha);
        if (await Page.GetByText(ArcaRobotScreens.Pattern(ArcaRobotScreens.ExtraValidation)).CountAsync() > 0)
            throw new ArcaRobotStoppedException(ArcaRobotFailure.ExtraValidation);
    }

    /// <summary>
    /// Traduce las fallas de Playwright a un motivo, sin su mensaje (puede repetir lo escrito): lo que no aparece es una
    /// pantalla cambiada, salvo un captcha o un trámite extra; una red caída es ARCA sin responder.
    /// </summary>
    private async Task<T> Guard<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (TimeoutException)
        {
            await ThrowIfBlockedAsync();
            throw new ArcaRobotStoppedException(ArcaRobotFailure.ScreenChanged);
        }
        catch (PlaywrightException exception) when (exception.Message.Contains("net::", StringComparison.Ordinal))
        {
            throw new ArcaRobotStoppedException(ArcaRobotFailure.Unavailable);
        }
        catch (PlaywrightException)
        {
            await ThrowIfBlockedAsync();
            throw new ArcaRobotStoppedException(ArcaRobotFailure.ScreenChanged);
        }
    }

    private Task<bool> Guard(Func<Task> action) => Guard(async () =>
    {
        await action();
        return true;
    });

}
