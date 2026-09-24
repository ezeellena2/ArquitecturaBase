using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Domain.WhatsApp;
using ArquitecturaBase.Infrastructure.WhatsApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>
/// La retención de los mensajes (sección 6.5 del spec y "Cuánto tiempo los guardamos" de la política de privacidad): a
/// los 90 días se borra el texto y queda el registro del mensaje, con su fecha, su tipo y su estado. En el arnés no corre
/// sola: los tests la llaman con <see cref="WhatsAppMessageRetentionService.ClearExpiredTextsAsync"/>. Todos los tests
/// comparten la base y una corrida vacía los mensajes viejos de todos, así que cada test mira solo sus filas.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppMessageRetentionTests(ApiFactory factory)
{
    private const int RetentionDays = 90;

    private const string CategoryName = "ArquitecturaBase.Infrastructure.WhatsApp.WhatsAppMessageRetentionService";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTime Now => factory.Clock.GetUtcNow().UtcDateTime;

    private DateTime Expired => Now.AddDays(-RetentionDays).AddMinutes(-1);

    [Fact]
    public async Task Inbound_and_outbound_messages_older_than_the_retention_lose_their_text_and_keep_the_rest_of_the_row()
    {
        var contact = await AddContactAsync();
        var text = Inbound(contact, "Hola, quiero entrar", Expired);
        text.MarkProcessed(Expired.AddSeconds(5));
        var button = WhatsAppMessage.Inbound(
            contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.ButtonReply, "Crear cuenta", "CREATE_ACCOUNT", Expired);
        button.MarkProcessed(Expired.AddSeconds(6));
        var read = WhatsAppMessage.Outbound(
            contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Interactive, "[enlace de ingreso] Entrar", Expired);
        read.ApplyStatus(WhatsAppMessageStatus.Read, Expired.AddMinutes(1), errorCode: null);

        // Un código mandado a un número que nunca le escribió al bot: saliente y sin contacto.
        var failed = WhatsAppMessage.Outbound(
            contactId: null, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Template, "[código]", Expired);
        failed.ApplyStatus(WhatsAppMessageStatus.Failed, Expired.AddSeconds(2), errorCode: 131026);
        await AddAsync(text, button, read, failed);
        var before = await RowsAsync(text, button, read, failed);

        await ClearExpiredTextsAsync();

        var after = await RowsAsync(text, button, read, failed);
        Assert.All(before, row => Assert.NotNull(row.Body));
        Assert.Equal(before.Select(row => row with { Body = null }), after);
    }

    [Fact]
    public async Task Messages_younger_than_the_retention_keep_their_text()
    {
        var contact = await AddContactAsync();
        var almost = Inbound(contact, "Casi viejo", Now.AddDays(-RetentionDays).AddMinutes(1));
        var recent = Inbound(contact, "De ayer", Now.AddDays(-1));
        var answer = WhatsAppMessage.Outbound(
            contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Text, "Por ahora este chat solo sirve para entrar.", Now.AddDays(-89));
        var old = Inbound(contact, "Viejo", Expired);
        await AddAsync(almost, recent, answer, old);

        await ClearExpiredTextsAsync();

        var after = await RowsAsync(almost, recent, answer, old);
        Assert.Equal(["Casi viejo", "De ayer", "Por ahora este chat solo sirve para entrar.", null], after.Select(row => row.Body));
    }

    /// <summary>La política conserva el contacto (número, identificador y nombre de perfil) hasta que la persona pida borrarlo.</summary>
    [Fact]
    public async Task Contacts_are_not_touched()
    {
        var contact = await AddContactAsync();
        await AddAsync(Inbound(contact, "Hola", Expired));
        var before = await ContactRowAsync(contact);

        await ClearExpiredTextsAsync();

        Assert.Equal(before, await ContactRowAsync(contact));
    }

    /// <summary>Deja afuera los que ya no tienen texto: la segunda corrida no encuentra nada ni cambia nada.</summary>
    [Fact]
    public async Task Running_it_twice_changes_nothing_the_second_time()
    {
        var contact = await AddContactAsync();
        var old = Inbound(contact, "Hola", Expired);
        var photo = WhatsAppMessage.Inbound(
            contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Media, body: null, replyId: null, Expired);
        await AddAsync(old, photo);

        var first = await ClearExpiredTextsAsync();
        var afterFirst = await RowsAsync(old, photo);
        var second = await ClearExpiredTextsAsync();

        Assert.True(first >= 1);
        Assert.Equal(0, second);
        Assert.Equal(afterFirst, await RowsAsync(old, photo));
    }

    [Fact]
    public async Task The_retention_days_are_configurable()
    {
        var contact = await AddContactAsync();
        var older = Inbound(contact, "Hace 31 días", Now.AddDays(-31));
        var younger = Inbound(contact, "Hace 29 días", Now.AddDays(-29));
        await AddAsync(older, younger);
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("WhatsApp:MessageRetentionDays", "30"));

        await api.Services.GetRequiredService<WhatsAppMessageRetentionService>().ClearExpiredTextsAsync(Ct);

        Assert.Equal([null, "Hace 29 días"], (await RowsAsync(older, younger)).Select(row => row.Body));
    }

    /// <summary>
    /// Es una obligación de la política de privacidad: la tabla puede tener mensajes de antes de apagar WhatsApp, y su
    /// texto se tiene que borrar igual. Con el ciclo en segundo plano prendido, como fuera de los tests: no alcanza con
    /// que el servicio exista y funcione si alguien lo llama, tiene que correr solo aunque WhatsApp esté apagado.
    /// </summary>
    [Fact]
    public async Task With_whatsapp_off_the_retention_still_runs_in_the_background_and_clears_old_texts()
    {
        var contact = await AddContactAsync();
        var old = Inbound(contact, "De cuando estaba prendido", Expired);
        await AddAsync(old);
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("WhatsApp:PhoneNumberId", "")
            .UseSetting("WhatsApp:ApplyMessageRetentionInBackground", "true"));

        Assert.False(api.Services.GetRequiredService<IWhatsAppAvailability>().IsEnabled);

        // Nadie llama a ClearExpiredTextsAsync: el texto lo vacía la corrida del arranque, la del host.
        await WaitUntilAsync(async () => (await RowsAsync(old))[0].Body is null);

        var retention = api.Services.GetRequiredService<WhatsAppMessageRetentionService>();
        Assert.Contains(api.Services.GetServices<IHostedService>(), service => ReferenceEquals(service, retention));
    }

    /// <summary>
    /// Con WhatsApp prendido o apagado: la retención corre siempre, así que su opción se valida siempre. Corre el mismo
    /// validador que el host antes de arrancar (<see cref="IStartupValidator"/>) y no una Api entera: un arranque que
    /// falla tan rápido a veces ya desechó el host cuando WebApplicationFactory lo espera, y el test ve un
    /// ObjectDisposedException en lugar del error.
    /// </summary>
    [Theory]
    [InlineData("0", true)]
    [InlineData("3651", true)]
    [InlineData("0", false)]
    [InlineData("-5", false)]
    public void Api_does_not_start_with_the_retention_days_out_of_range(string value, bool whatsAppOn)
    {
        var settings = new Dictionary<string, string?> { ["WhatsApp:MessageRetentionDays"] = value };

        if (whatsAppOn)
        {
            settings["WhatsApp:PhoneNumberId"] = ApiFactory.WhatsAppPhoneNumberId;
            settings["WhatsApp:AccessToken"] = "test-access-token";
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWhatsApp(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        using var provider = services.BuildServiceProvider();

        var exception = Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Equal(whatsAppOn, provider.GetRequiredService<IWhatsAppAvailability>().IsEnabled);
        Assert.Contains("WhatsApp:MessageRetentionDays must be between 1 and 3650", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Fuera de los tests corre sola: al arrancar y después una vez por día, ni antes ni después. Con un reloj propio, así
    /// mover un día no mueve el de los demás tests.
    /// </summary>
    [Fact]
    public async Task In_the_background_it_runs_at_startup_and_then_once_a_day()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var contact = await AddContactAsync();
        var old = Inbound(contact, "Viejo", Expired);
        var almost = Inbound(contact, "Casi viejo", Now.AddDays(-RetentionDays).AddHours(1));
        await AddAsync(old, almost);

        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("WhatsApp:ApplyMessageRetentionInBackground", "true")
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            }));
        _ = api.Services;

        await WaitUntilAsync(async () => (await RowsAsync(old))[0].Body is null);
        Assert.Equal("Casi viejo", (await RowsAsync(almost))[0].Body);

        // Un minuto antes del día no corre: "almost" vence una hora después del arranque, así que cualquier corrida de
        // este lado del día (cada hora, cada minuto) ya lo vaciaría.
        clock.Advance(TimeSpan.FromDays(1) - TimeSpan.FromMinutes(1));
        await Task.Delay(100, Ct);
        Assert.Equal("Casi viejo", (await RowsAsync(almost))[0].Body);

        clock.Advance(TimeSpan.FromMinutes(1));

        await WaitUntilAsync(async () => (await RowsAsync(almost))[0].Body is null);
    }

    /// <summary>
    /// Un error (acá, una base que no existe) se registra y la corrida del día siguiente lo intenta de nuevo: la
    /// retención nunca apaga la Api.
    /// </summary>
    [Fact]
    public async Task A_failed_run_is_logged_and_retried_the_next_day_without_stopping_the_api()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting($"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("retention"))
            .UseSetting("WhatsApp:ApplyMessageRetentionInBackground", "true")
            .ConfigureLogging(logging => logging.AddFakeLogging())
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            }));
        var collector = api.Services.GetFakeLogCollector();

        await WaitUntilAsync(() => Task.FromResult(Failures(collector) == 1));
        clock.Advance(TimeSpan.FromDays(1));
        await WaitUntilAsync(() => Task.FromResult(Failures(collector) == 2));

        Assert.False(api.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
    }

    /// <summary>Dice cuántos mensajes vació y con qué plazo; nunca un texto ni un dato de la persona (reglas del plan).</summary>
    [Fact]
    public async Task The_log_says_how_many_texts_were_cleared_and_never_carries_a_text()
    {
        const string PrivateText = "Texto-privado-retencion-5521";
        var contact = await AddContactAsync();
        await AddAsync(Inbound(contact, PrivateText, Expired));
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Logging:LogLevel:Default", "Trace")
            .ConfigureLogging(logging => logging.AddFakeLogging()));

        var cleared = await api.Services.GetRequiredService<WhatsAppMessageRetentionService>().ClearExpiredTextsAsync(Ct);

        var records = api.Services.GetFakeLogCollector().GetSnapshot();
        var summary = Assert.Single(records, record => record.Category == CategoryName);
        Assert.Equal(LogLevel.Information, summary.Level);
        Assert.True(cleared >= 1);
        Assert.Equal(cleared.ToString(CultureInfo.InvariantCulture), summary.GetStructuredStateValue("Count"));
        Assert.Equal(RetentionDays.ToString(CultureInfo.InvariantCulture), summary.GetStructuredStateValue("RetentionDays"));

        string[] secrets = [PrivateText, contact.WaId!, contact.UserIdentifier!, contact.ProfileName!];
        var logged = records.Select(record => string.Join(
            "\n",
            [
                record.Message,
                record.Exception?.ToString() ?? string.Empty,
                .. record.StructuredState?.Select(pair => pair.Value ?? string.Empty) ?? [],
            ]));
        Assert.DoesNotContain(logged, text => secrets.Any(secret => text.Contains(secret, StringComparison.Ordinal)));
    }

    private static int Failures(FakeLogCollector collector) =>
        collector.GetSnapshot().Count(record => record.Category == CategoryName && record.Level == LogLevel.Error);

    private Task<int> ClearExpiredTextsAsync() =>
        factory.Services.GetRequiredService<WhatsAppMessageRetentionService>().ClearExpiredTextsAsync(Ct);

    private static WhatsAppMessage Inbound(WhatsAppContact contact, string body, DateTime occurredAtUtc) =>
        WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Text, body, replyId: null, occurredAtUtc);

    private async Task<WhatsAppContact> AddContactAsync()
    {
        var contact = WhatsAppContact.Create(MetaWebhook.UniqueWaId(), MetaWebhook.UniqueBsuid(), "Perfil Retención", Expired);
        await factory.ExecuteDbContextAsync(db =>
        {
            db.WhatsAppContacts.Add(contact);
            return db.SaveChangesAsync(Ct);
        });

        return contact;
    }

    private Task<int> AddAsync(params WhatsAppMessage[] messages) =>
        factory.ExecuteDbContextAsync(db =>
        {
            db.WhatsAppMessages.AddRange(messages);
            return db.SaveChangesAsync(Ct);
        });

    /// <summary>Las filas, en el orden de <paramref name="messages"/>, tal como están en la base.</summary>
    private async Task<List<MessageRow>> RowsAsync(params WhatsAppMessage[] messages)
    {
        var ids = messages.Select(message => message.Id).ToList();
        var rows = await factory.ExecuteDbContextAsync(db => db.WhatsAppMessages
            .AsNoTracking()
            .Where(message => ids.Contains(message.Id))
            .ToListAsync(Ct));

        return [.. ids.Select(id => MessageRow.Of(rows.Single(row => row.Id == id)))];
    }

    private Task<ContactRow> ContactRowAsync(WhatsAppContact contact) =>
        factory.ExecuteDbContextAsync(async db => ContactRow.Of(
            await db.WhatsAppContacts.AsNoTracking().SingleAsync(row => row.Id == contact.Id, Ct)));

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!await condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private sealed record MessageRow(
        Guid Id,
        Guid? ContactId,
        WhatsAppMessageDirection Direction,
        string WaMessageId,
        WhatsAppMessageKind Kind,
        string? Body,
        string? ReplyId,
        DateTime OccurredAtUtc,
        DateTime? ProcessedAtUtc,
        WhatsAppMessageStatus? Status,
        DateTime? StatusAtUtc,
        int? ErrorCode)
    {
        public static MessageRow Of(WhatsAppMessage message) => new(
            message.Id,
            message.ContactId,
            message.Direction,
            message.WaMessageId,
            message.Kind,
            message.Body,
            message.ReplyId,
            message.OccurredAtUtc,
            message.ProcessedAtUtc,
            message.Status,
            message.StatusAtUtc,
            message.ErrorCode);
    }

    private sealed record ContactRow(string? WaId, string? UserIdentifier, string? ProfileName, Guid? UserId, DateTime LastInboundAtUtc)
    {
        public static ContactRow Of(WhatsAppContact contact) =>
            new(contact.WaId, contact.UserIdentifier, contact.ProfileName, contact.UserId, contact.LastInboundAtUtc);
    }
}
