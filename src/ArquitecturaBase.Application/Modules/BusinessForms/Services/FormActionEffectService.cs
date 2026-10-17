using ArquitecturaBase.Application.Common.Logging;
using Microsoft.Extensions.Logging;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed partial class FormActionEffectService:IFormActionEffectService
{
    private readonly ILogger<FormActionEffectService> _logger;
    private readonly IFormPdfActionService _pdf;private readonly IStructuredRecordActionChannel[] _channels;
    public FormActionEffectService(IFormPdfActionService pdf,IEnumerable<IStructuredRecordActionChannel> channels,ILogger<FormActionEffectService> logger)
    {
        _pdf=pdf;_channels=channels.ToArray();_logger=logger;
        if(_channels.Any(channel=>string.IsNullOrWhiteSpace(channel.ChannelId))||_channels.DistinctBy(channel=>channel.ChannelId).Count()!=_channels.Length)
            throw new InvalidOperationException("Action channel identifiers must be unique and nonempty.");
    }
    public async Task<FormActionOutcomeResponse> ExecuteAsync(FormActionWorkResponse work,CancellationToken cancellationToken)
    {
        OperationLog.Handling(_logger,"ExecuteFormAction");FormActionOutcomeResponse outcome;
        try{outcome=work.Kind=="GeneratePdf"?await PdfAsync(work,cancellationToken):await NotifyAsync(work,cancellationToken);}
        catch(Exception exception)when(!cancellationToken.IsCancellationRequested)
        {
            LogUnconfirmed(_logger,work.RunId,exception.GetType().Name);
            outcome=new(FormActionRunState.Uncertain,FormActionErrors.EffectUnconfirmed.Code);
        }
        if(outcome.State is FormActionRunState.Failed or FormActionRunState.Uncertain)OperationLog.Failed(_logger,"ExecuteFormAction",outcome.FailureCode??FormActionErrors.EffectUnconfirmed.Code);
        else OperationLog.Handled(_logger,"ExecuteFormAction");
        return outcome;
    }
    [LoggerMessage(Level=LogLevel.Warning,Message="Form action {RunId} could not be confirmed after {ExceptionType}.")]
    private static partial void LogUnconfirmed(ILogger logger,Guid runId,string exceptionType);
    private async Task<FormActionOutcomeResponse> PdfAsync(FormActionWorkResponse work,CancellationToken ct)
    {
        var snapshot=work.Snapshot.Pdf??throw new InvalidOperationException("The PDF action snapshot is missing.");
        var result=await _pdf.ExecuteAsync(work.RecordId,snapshot,work.ReconciliationOnly,ct);
        if(result.IsFailure)return new(FormActionRunState.Uncertain,result.Error.Code);
        if(result.Value.State==FormPdfActionState.NotFound&&(!work.ReconciliationOnly||work.RetryRequested))result=await _pdf.ExecuteAsync(work.RecordId,snapshot,false,ct);
        if(result.IsFailure)return new(FormActionRunState.Uncertain,result.Error.Code);
        return result.Value.State switch
        {
            FormPdfActionState.Completed=>new(FormActionRunState.Completed,Pdf:result.Value),
            FormPdfActionState.Uncertain=>new(FormActionRunState.Uncertain,result.Value.FailureCode??FormActionErrors.EffectUnconfirmed.Code,Pdf:result.Value),
            _=>new(FormActionRunState.Failed,result.Value.FailureCode??FormActionErrors.EffectUnconfirmed.Code,true,result.Value),
        };
    }
    private async Task<FormActionOutcomeResponse> NotifyAsync(FormActionWorkResponse work,CancellationToken ct)
    {
        var role=work.Kind=="NotifyRole";
        if(role&&work.Snapshot.Recipients.Count==0)return new(FormActionRunState.Completed,Recipients:[]);
        if(_channels.Length==0)return role
            ?new(FormActionRunState.Completed,Recipients:work.Snapshot.Recipients.Select(recipient=>new FormActionRecipientOutcome(recipient.UserId,recipient.DisplayName,null,new(StructuredRecordActionState.Failed,FormActionErrors.ChannelUnavailable.Code))).ToArray())
            :new(FormActionRunState.Failed,FormActionErrors.ChannelUnavailable.Code,true);
        var recipients=new List<FormActionRecipientOutcome>();
        foreach(var recipient in work.Snapshot.Recipients)
        foreach(var channel in _channels)
        {
            if(recipient.PreparationFailureCode is {} preparationFailure)
            {
                recipients.Add(new(recipient.UserId,recipient.DisplayName,channel.ChannelId,new(StructuredRecordActionState.Failed,preparationFailure)));continue;
            }
            var previous=work.PriorRecipients?.SingleOrDefault(item=>item.UserId==recipient.UserId&&item.ChannelId==channel.ChannelId);
            if(previous is not null&&(previous.Status.State==StructuredRecordActionState.Accepted
                ||role&&previous.Status.State==StructuredRecordActionState.Failed&&previous.Status.Review?.WasSent!=false)){recipients.Add(previous);continue;}
            StructuredRecordActionResponse status;
            try
            {
                status=await channel.GetAsync(work.RunId,recipient.UserId,ct);
                if(status.State==StructuredRecordActionState.Uncertain&&work.DeliveryReview is {} review&&work.DeliveryReviewRecipientUserId==recipient.UserId)
                    status=await channel.ReviewAsync(work.RunId,recipient.UserId,review,ct);
                else if(status.State==StructuredRecordActionState.NotFound&&(!work.ReconciliationOnly||work.RetryRequested))
                {
                    StructuredRecordActionDocument? document=null;
                    if(work.Snapshot.PdfRunId is not null)
                    {
                        var artifact=work.PdfArtifacts.Single(artifact=>artifact.Culture==recipient.Culture);
                        document=new(artifact.StorageKey,artifact.Name,artifact.ContentType,artifact.Size,artifact.Sha256);
                    }
                    status=await channel.EnqueueAsync(new(work.RunId,recipient.UserId,recipient.Body,document),ct);
                }
                else if(!role&&status.State==StructuredRecordActionState.Failed&&status.CanRetry&&work.RetryRequested)
                    status=await channel.RetryAsync(work.RunId,recipient.UserId,work.Fence,ct);
                if(status.State==StructuredRecordActionState.NotFound)
                    status=status with{State=StructuredRecordActionState.Failed,FailureCode=FormActionErrors.RecipientUnavailable.Code,CanRetry=!role};
            }
            catch(Exception exception)when(!ct.IsCancellationRequested)
            {
                LogUnconfirmed(_logger,work.RunId,exception.GetType().Name);
                status=new(StructuredRecordActionState.Uncertain,FormActionErrors.EffectUnconfirmed.Code);
            }
            recipients.Add(new(recipient.UserId,recipient.DisplayName,channel.ChannelId,status));
        }
        if(recipients.Count==0)return new(FormActionRunState.Failed,FormActionErrors.RecipientUnavailable.Code,Recipients:recipients);
        if(recipients.Any(item=>item.Status.State==StructuredRecordActionState.Uncertain))
            return new(FormActionRunState.Uncertain,recipients.First(item=>item.Status.State==StructuredRecordActionState.Uncertain).Status.FailureCode??FormActionErrors.EffectUnconfirmed.Code,Recipients:recipients);
        if(recipients.Any(item=>item.Status.State is StructuredRecordActionState.Pending or StructuredRecordActionState.Sending))
            return new(FormActionRunState.Running,Recipients:recipients);
        if(role||recipients.All(item=>item.Status.State==StructuredRecordActionState.Accepted))return new(FormActionRunState.Completed,Recipients:recipients);
        return new(FormActionRunState.Failed,recipients.First(item=>item.Status.State==StructuredRecordActionState.Failed).Status.FailureCode??FormActionErrors.EffectUnconfirmed.Code,
            recipients.Any(item=>item.Status.CanRetry),Recipients:recipients);
    }
}
