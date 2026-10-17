using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantQualification;

// Technical fixture writer for the isolated, fictitious evaluation database only.
// Business authoring, catalog creation, suggestion configuration and capture use their real public services.
// Historical channel rows establish read relationships; they never claim a Meta publication or delivery.
internal sealed class QualificationFormSeed(ApplicationDbContext db,IUnitOfWork unitOfWork,IFormDraftService drafts,
    IFormCatalogService catalogs,IFormSuggestionService suggestions,IStructuredRecordSink records,IStructuredFormSource forms,
    IStructuredMissingFormRequestSink missing,IEnumerable<IAssistantCapabilitySource> capabilities,
    IEnumerable<IStructuredFormChannel> channels,IFormPublicationProtector publications,IWhatsAppFlowSessionProtector sessions,IWhatsAppFlowProtector flowProtection,
    QualificationCurrentUser currentUser,TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;
    public async Task<QualificationChannelFixture> CreateAsync(QualificationFixture actors,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actors);
        var manager = actors.Operators.Single(actor => actor.Culture == "es");
        currentUser.Set(manager.UserId);
        var colors = Require(await catalogs.CreateManualAsync(new(CatalogTexts("Color","Color"),CatalogTexts("Colores de prueba","Fixture colors"),
            [new("red",null,"RED",CatalogTexts("Rojo","Red"),null,true),new("blue",null,"BLUE",CatalogTexts("Azul","Blue"),null,true)]),ct),"colors");
        var branches = Require(await catalogs.CreateManualAsync(new(CatalogTexts("Sede","Branch"),CatalogTexts("Sedes de prueba","Fixture branches"),
            [new("north",null,"NORTH",new Dictionary<string,string?> { ["es"] = "Sucursal Norte" },null,true)]),ct),"branches");
        var prepared = new List<PreparedForm>
        {
            await CreateFormAsync("visit","VISIT","Registrar visita","Register visit",VisitFields(),
                "Cuando quiere registrar una visita a una sucursal. No es mantenimiento ni un control de importes.",ct),
            await CreateFormAsync("local-visit","LOCALVISIT","Visita local","Local visit",[..VisitFields(),
                Field("branch","choice","Sede","Branch",catalog:branches.Catalog.Id)],
                "Cuando pide explícitamente Visita local o Local visit, para visitar una sede local.",ct),
            await CreateFormAsync("maintenance","MAINT","Registrar mantenimiento","Register maintenance",
                [Field("place","shortText","Lugar","Place",80,true),Field("notes","longText","Notas","Notes",600)],
                "Cuando quiere registrar tareas de mantenimiento, reparaciones o arreglos de equipos.",ct),
            await CreateFormAsync("control","CONTROL","Registrar control","Register control",
                [Field("text","shortText","Texto","Text",80,true),Field("notes","longText","Notas","Notes",600),
                    Field("amount","decimal","Importe","Amount"),Field("day","date","Día","Day"),
                    Field("confirmed","boolean","Confirmado","Confirmed"),Field("color","choice","Color","Color",catalog:colors.Catalog.Id)],
                "Cuando pide registrar un control con texto, notas, importe, fecha, confirmación y color.",ct),
        };
        var published = new List<QualificationForm>();
        foreach (var form in prepared) published.Add(await PublishFixtureAsync(form,manager.UserId,ct));
        var visit = published.Single(form => form.Alias == "visit");
        var maintenance = published.Single(form => form.Alias == "maintenance");
        var controlIndex = published.FindIndex(form => form.Alias == "control");
        published[controlIndex] = published[controlIndex] with { ChoiceIds = colors.ItemIdentityMap };
        var suggestionId = Guid.CreateVersion7();
        Require(await suggestions.SaveAsync(visit.DefinitionId,new(0,[new(suggestionId,maintenance.DefinitionId,"Assistant",
            "Después de la visita, cuando necesita registrar el mantenimiento o los arreglos mencionados.",true,[],
            [new(new("Field",visit.Fields["place"]),maintenance.Fields["place"])])]),ct),"suggestion");
        var captured = new List<QualificationSuggestion>();
        var offers = new List<QualificationOffer>();
        foreach (var subject in actors.Subjects)
        {
            var history = await CreateChannelHistoryAsync(subject,manager.UserId,visit,ct);
            var response = await records.CaptureAsync(new(history.SessionId,visit.DefinitionId,visit.VersionId,subject.UserId,
                Now().AddMinutes(-10),new Dictionary<string,string> { [Key(visit.Fields["place"])] = subject.Culture == "es" ? "Sucursal Norte" : "North branch",
                    [Key(visit.Fields["notes"])] = subject.Culture == "es" ? "Hace falta mantenimiento del equipo." : "The equipment needs maintenance." }),ct);
            if (response is not { State: "Completed",RecordId: { } recordId }) throw new InvalidOperationException("Qualification source capture failed.");
            var available = Require(await forms.ListSuggestionsAsync(subject.UserId,subject.Culture,recordId,ct),"source suggestions");
            if (available.Candidates.Single().SuggestionId != suggestionId) throw new InvalidOperationException("Qualification suggestion identity changed.");
            captured.Add(new(subject.UserId,recordId,available.SourceNumber,suggestionId,maintenance.DefinitionId));
            Require(await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var session = await db.Set<WhatsAppFlowSession>().SingleAsync(session => session.Id == history.SessionId,token);
                return session.CompleteCapture("qualification.capture." + session.Id.ToString("N"),WhatsAppFlowCaptureState.Completed,recordId,available.SourceNumber,null)
                    ? Result.Success() : Result.Failure(FormRecordErrors.InvalidResponse);
            },CommitPolicy.OnSuccess,ct),"historical capture receipt");
            offers.Add(history.Offer);
        }
        await CreateMissingTopicAsync(actors.Subjects.Single(actor => actor.Culture == "es"),ct);
        return new(published,captured,offers);
    }

    private async Task<PreparedForm> CreateFormAsync(string alias,string prefix,string spanish,string english,FormFieldInput[] fields,string when,CancellationToken ct)
    {
        var saved = Require(await drafts.CreateAsync(new(Guid.CreateVersion7(),Texts(spanish,english),prefix,
            new(1,"es",Texts("Enviar","Submit"),fields),OfferWhen:when),ct),"draft");
        var ids = saved.IdentityMap.Fields.ToDictionary(field => field.LocalId,field => field.FieldId,StringComparer.Ordinal);
        return new(alias,saved.Definition,ids);
    }

    private async Task<QualificationForm> PublishFixtureAsync(PreparedForm form,Guid manager,CancellationToken ct)
    {
        var questions = form.Definition.Draft.Fields.Select(question => new StructuredFormQuestion(question.FieldId,question.Key,question.Type,
            question.Label,question.Help,question.Required,question.MaxLength,
            question.Options?.Select(option => new StructuredFormChoice(option.ChoiceId,option.Label)).ToArray(),question.Source?.Catalog,
            question.Files is {} files ? new(files.MinFiles,files.MaxFiles,files.PhotoSource) : null)).ToArray();
        var channel = channels.Single(channel => channel.ChannelId == "phone");
        var check = Require(await channel.CheckAsync(new(1,"es",form.Definition.Name,form.Definition.Draft.SubmitLabel,questions,["es","en"],25),ct),"compilation");
        if (check.Issues.Count != 0) throw new InvalidOperationException("Qualification fixture is not representable by the real compiler.");
        var versionId = Guid.Empty;
        Require(await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(definition => definition.Id == form.Definition.Id,token);
            var schema = JsonSerializer.Serialize(new FormVersionSchema(1,form.Definition.Draft.Fields),Json);
            var hash = Hash(schema);
            var version = BusinessFormVersion.Create(definition.Id,1,hash); version.SealSchema(schema);
            version.SealPresentation(JsonSerializer.Serialize(form.Definition.Name,Json),JsonSerializer.Serialize(form.Definition.Draft.SubmitLabel,Json),"es",questions.Length);
            var now = Now();
            var operation = BusinessFormPublicationOperation.Accept(definition.Id,version.Id,manager,Guid.CreateVersion7(),definition.Revision,1,null,[],now);
            var manifest = new FormPublicationManifest(1,Hash("qualification-attempt:" + definition.Id),Hash("qualification-publication:" + definition.Id),
                definition.Id,version.Id,definition.Revision,hash,form.Definition.Name,definition.Prefix,"es",form.Definition.Draft.SubmitLabel,
                [new("phone",check.ProfileRevision,check.Cultures.Select(culture => new FormPublicationCultureProfile(culture.Culture,culture.CompilationFingerprint)).ToArray())],25);
            operation.SealManifest(publications.ProtectManifest(new(operation.Id,manager,operation.RequestId,definition.Id,version.Id),manifest));
            var reserved = operation.Reserve(now,TimeSpan.FromMinutes(1)); if (reserved.IsFailure) return Result.Failure(reserved.Error);
            var observed = operation.RecordChannelResult(reserved.Value,now,"Published","QualificationFixture"); if (observed.IsFailure) return observed;
            var completed = operation.Complete(reserved.Value,now); if (completed.IsFailure) return completed;
            var activated = definition.ActivateVersion(null,version.Id); if (activated.IsFailure) return activated;
            definition.LockPrefix();
            db.Set<BusinessFormVersion>().Add(version); db.Set<BusinessFormPublicationOperation>().Add(operation);
            db.Set<BusinessFormField>().AddRange(form.Definition.Draft.Fields.Select(field => BusinessFormField.Register(field.FieldId,definition.Id,field.Key,field.Type)));
            versionId = version.Id;
            return Result.Success();
        },CommitPolicy.OnSuccess,ct),"published fixture");
        return new(form.Definition.Id,versionId,form.Alias,form.Fields);
    }

    private async Task<PendingHistory> CreateChannelHistoryAsync(QualificationActor subject,Guid manager,QualificationForm form,CancellationToken ct)
    {
        var requestId = Guid.CreateVersion7();
        var sessionId = Guid.Empty;
        Require(await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var user = await db.Users.SingleAsync(user => user.Id == subject.UserId,token);
            if (user.PhoneNumber is null) throw new InvalidOperationException("Qualification subject needs its confirmed fictitious phone.");
            var now = Now();
            var contact = WhatsAppContact.Create(user.PhoneNumber.TrimStart('+'),"qualification." + subject.UserId.ToString("N"),null,now);
            contact.LinkUser(subject.UserId);
            var account = new WhatsAppFlowAccountContext("qualification-no-waba","qualification-no-phone",WhatsAppFlowProfile.GraphApiVersion,Hash("qualification-no-meta"));
            var operation = await db.Set<WhatsAppFlowOperation>().SingleOrDefaultAsync(operation => operation.VersionId == form.VersionId,token);
            if (operation is null)
            {
                var neutral = await db.Set<BusinessFormPublicationOperation>().Where(operation => operation.VersionId == form.VersionId).Select(operation => operation.Id).SingleAsync(token);
                operation = WhatsAppFlowOperation.Accept(neutral,form.DefinitionId,form.VersionId,manager);
                operation.SealManifest(flowProtection.ProtectManifest(operation.Id,neutral,form.VersionId,
                    new(1,Hash("qualification-history-request"),Hash("qualification-history-publication"),account,[])));
                var reserve = operation.Reserve(now,TimeSpan.FromMinutes(1)); if (reserve.IsFailure) return Result.Failure(reserve.Error);
                // The history has no publication asset or JSON and no pending transport work.
                operation.Release(now,true,false); db.Set<WhatsAppFlowOperation>().Add(operation);
            }
            var binding = WhatsAppFlowBinding.Create(operation.Id,form.VersionId,subject.Culture,"qualification_history_" + subject.Culture,[]);
            var names = await db.Set<BusinessFormVersion>().Where(version => version.Id == form.VersionId).Select(version => version.NameJson!).SingleAsync(token);
            var tokenText = "qualification-" + Guid.CreateVersion7().ToString("N");
            var session = WhatsAppFlowSession.Create(contact.Id,contact.LinkRevision,subject.UserId,manager,form.DefinitionId,form.VersionId,1,binding.Id,
                subject.Culture,names,"VISIT",Hash(tokenText),Hash("qualification-history:" + requestId),now.AddDays(30));
            var payload = new WhatsAppFlowSessionPayload(1,contact.Id,subject.UserId,manager,form.DefinitionId,form.VersionId,binding.Id,subject.Culture,
                "qualification-fixture",contact.ModeRevision,"0",binding.TechnicalName,Hash("qualification-no-asset"),tokenText,"Abrir","SCREEN_0",
                account);
            session.SealPayload(sessions.Protect(session.Id,contact.Id,subject.UserId,form.VersionId,payload));
            sessionId = session.Id;
            var outgoing = WhatsAppOutgoingMessage.CreateFlow(manager,contact.Id,requestId,"Qualification historical offer",session.Id,now.AddMinutes(-15));
            outgoing.Start(now.AddMinutes(-15)); outgoing.MarkUncertain(); outgoing.Review();
            db.Set<WhatsAppContact>().Add(contact); db.Set<WhatsAppFlowBinding>().Add(binding);
            db.Set<WhatsAppFlowSession>().Add(session); db.Set<WhatsAppOutgoingMessage>().Add(outgoing);
            return Result.Success();
        },CommitPolicy.OnSuccess,ct),"channel history");
        return new(sessionId,new(subject.UserId,requestId,form.DefinitionId,form.VersionId));
    }

    private async Task CreateMissingTopicAsync(QualificationActor subject,CancellationToken ct)
    {
        var context = new AssistantChannelContext("whatsapp-assistant",subject.UserId,subject.UserId,"es","America/Argentina/Buenos_Aires",new([],[]),
            [new("forms.assistant-candidates",1),new("forms.missing-request.propose",1)],[new("assistant_proposal",1)]);
        var readSource = capabilities.Single(source => source.Definitions.Any(definition => definition.Id == "forms.assistant-candidates"));
        var proposalSource = capabilities.Single(source => source.Definitions.Any(definition => definition.Id == "forms.missing-request.propose"));
        var turn = Guid.CreateVersion7();
        var read = Require(await readSource.ExecuteAsync(new(turn,context,new("forms.assistant-candidates",1),"{}",[]),ct),"topic read");
        var observed = new AssistantObservedRead(turn,1,new("forms.assistant-candidates",1),read.Payload);
        var arguments = JsonSerializer.Serialize(new { existingTopicId = (Guid?)null,name = "Préstamo de herramientas",description = "Piden registrar préstamos de herramientas entre sucursales." },Json);
        var proposal = Require(await proposalSource.ExecuteAsync(new(turn,context,new("forms.missing-request.propose",1),arguments,[observed]),ct),"topic proposal");
        var now = Now();
        Require(await missing.CaptureAsync(new(Guid.CreateVersion7(),now.AddMinutes(-5),context,
            new(turn,Guid.CreateVersion7(),new("forms.missing-request.propose",1),proposal.Payload) { ObservedReads = [observed] },
            new("qualification-fixture",1,"qualification-fixture",1,now,context.ChannelId)),ct),"topic capture");
    }

    private static FormFieldInput[] VisitFields() => [Field("place","shortText","Lugar","Place",80,true),
        Field("notes","longText","Notas","Notes",600),new("photo",null,null,"photo",Texts("Foto","Photo"),null,false,null,null,Files:new(0,1,"cameraOrGallery"))];
    private static FormFieldInput Field(string key,string type,string es,string en,int? max = null,bool required = false,Guid? catalog = null) =>
        new(key,null,null,type,Texts(es,en),null,required,max,null,catalog.HasValue ? new(catalog) : null);
    private static Dictionary<string,string?> CatalogTexts(string es,string en) => new() { ["es"] = es,["en"] = en };
    private static Dictionary<string,string> Texts(string es,string en) => new() { ["es"] = es,["en"] = en };
    private static string Key(Guid field) => "field_" + field.ToString("N");
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private DateTime Now() { var now = clock.GetUtcNow().UtcDateTime; return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond)); }
    private static T Require<T>(Result<T> result,string stage) => result.IsSuccess ? result.Value : throw new InvalidOperationException("Qualification " + stage + " failed.");
    private static void Require(Result result,string stage) { if (result.IsFailure) throw new InvalidOperationException("Qualification " + stage + " failed."); }
    private sealed record PendingHistory(Guid SessionId,QualificationOffer Offer);
    private sealed record PreparedForm(string Alias,FormDefinitionResponse Definition,IReadOnlyDictionary<string,Guid> Fields);
}
