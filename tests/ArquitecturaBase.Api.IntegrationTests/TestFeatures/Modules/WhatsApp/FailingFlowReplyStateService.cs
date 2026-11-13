using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

/// <summary>
/// Review of task 9c of the documents: the unit of work that marks a rejected reply fails (a transient PostgreSQL error, a deadlock, the
/// deadline, a deploy) after BusinessForms kept its receipt, as many times as <see cref="Failures.Remaining"/> says; nothing of it is saved,
/// and the reply worker retries the reply. Everything else goes to the real state service.
/// </summary>
internal sealed class FailingFlowReplyStateService(IWhatsAppFlowReplyStateService inner, FailingFlowReplyStateService.Failures failures)
    : IWhatsAppFlowReplyStateService
{
    internal sealed class Failures
    {
        public int Remaining { get; set; }
        public int Failed { get; set; }
    }

    internal static void Configure(IServiceCollection services, Failures failures)
    {
        var real = services.Single(descriptor => descriptor.ServiceType == typeof(IWhatsAppFlowReplyStateService)).ImplementationType!;
        services.Replace(ServiceDescriptor.Scoped<IWhatsAppFlowReplyStateService>(provider =>
            new FailingFlowReplyStateService((IWhatsAppFlowReplyStateService)ActivatorUtilities.CreateInstance(provider, real), failures)));
    }

    public Task<Result> MarkDownloadingAsync(string waMessageId, Guid sessionId, int fileCount, CancellationToken cancellationToken) =>
        inner.MarkDownloadingAsync(waMessageId, sessionId, fileCount, cancellationToken);

    public Task<Result> RegisterFailureAsync(string waMessageId, Guid? sessionId, CancellationToken cancellationToken, string? failureCode = null,
        string? rejectedQuestion = null) => inner.RegisterFailureAsync(waMessageId, sessionId, cancellationToken, failureCode, rejectedQuestion);

    public Task<Result> MarkProcessedAsync(string waMessageId, Guid? sessionId, WhatsAppFlowCaptureState state, Guid? recordId, string? number,
        string? errorCode, CancellationToken cancellationToken, string? rejectedQuestion = null, WhatsAppFlowRejection? rejection = null)
    {
        if (rejection is not null && failures.Remaining > 0)
        {
            failures.Remaining--;
            failures.Failed++;
            throw new InvalidOperationException("The unit of work that marks the rejected reply failed.");
        }
        return inner.MarkProcessedAsync(waMessageId, sessionId, state, recordId, number, errorCode, cancellationToken, rejectedQuestion, rejection);
    }
}
