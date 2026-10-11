using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
internal sealed partial class FormPublicationBackgroundService(IServiceScopeFactory scopes, TimeProvider timeProvider,
    IOptions<FormPublicationWorkerOptions> options, ILogger<FormPublicationBackgroundService> logger) : BackgroundService
{
    private static readonly string[] RequiredPermissions = [BusinessFormsPermissions.Read, BusinessFormsPermissions.Publish];
    public async Task ProcessNextAsync(Guid? operationId, CancellationToken cancellationToken)
    {
        FormPublicationWorkResponse? work;
        await using (var reservation = scopes.CreateAsyncScope())
        {
            var result = await reservation.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>().ReserveAsync(operationId, cancellationToken);
            work = result.IsSuccess ? result.Value : null;
        }
        if (work is null) return;
        var progress = new List<FormPublicationChannelProgress>();
        var snapshot = new StructuredFormCheckRequest(work.Schema.Format, work.Manifest.OriginalCulture, work.Manifest.Name, work.Manifest.SubmitLabel,
            work.Schema.Questions.Select(field => new StructuredFormQuestion(field.FieldId, field.Key, field.Type, field.Label, field.Help, field.Required,
                field.MaxLength, field.Options?.Select(option => new StructuredFormChoice(option.ChoiceId, option.Label)).ToArray())).ToArray(), ["es", "en"]);
        foreach (var profile in work.Manifest.Profiles)
        {
            await using var dispatch = scopes.CreateAsyncScope();
            var channel = dispatch.ServiceProvider.GetServices<IStructuredFormChannel>().SingleOrDefault(value => value.ChannelId == profile.ChannelId);
            if (channel is null)
            {
                progress.Add(new(profile.ChannelId, Response(work, "Uncertain", "ChannelUnavailable"))); continue;
            }
            var accepted = await channel.AcceptAsync(new(work.OperationId, work.DefinitionId, work.VersionId, work.ActorUserId, RequiredPermissions,
                work.Manifest.PublicationFingerprint, profile.ProfileRevision, profile.Cultures.Select(culture => new StructuredFormPublicationCulture(culture.Culture, culture.CompilationFingerprint)).ToArray(), snapshot), cancellationToken);
            if (accepted.IsFailure)
            {
                progress.Add(new(profile.ChannelId, Response(work, "Failed", "ChannelRejected"))); continue;
            }
            var observed = await channel.GetPublicationAsync(new(work.OperationId, work.DefinitionId, work.VersionId, work.Manifest.PublicationFingerprint), cancellationToken);
            progress.Add(new(profile.ChannelId, observed.IsSuccess ? observed.Value : Response(work, "Uncertain", "ChannelUnavailable")));
        }
        await using var completion = scopes.CreateAsyncScope();
        await completion.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>().CompleteAsync(work, progress, cancellationToken);
    }
    private static StructuredFormPublicationResponse Response(FormPublicationWorkResponse work, string state, string stage) =>
        new(work.OperationId, work.DefinitionId, work.VersionId, work.Manifest.PublicationFingerprint, state, stage, [], null, state == "Uncertain");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ProcessInBackground) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessNextAsync(null, stoppingToken); }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested) { LogUnknown(logger, exception.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, stoppingToken);
        }
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "The form publication outcome could not be confirmed after {ExceptionType}; durable recovery will inspect it.")]
    private static partial void LogUnknown(ILogger logger, string exceptionType);
}
