using System.Reflection;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.ArchitectureTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.ArchitectureTests.Modules.BusinessForms;

// One service per kind creates from an assistant proposal: anything else that read a proposal to create would skip its checks.
// Importing the spreadsheet a proposal mapped (Stage 2) reads it too, and creates nothing.
public sealed class FormAssistantProposalCreationTests
{
    private const string Services = "ArquitecturaBase.Application.Modules.BusinessForms.Services.";
    private static readonly Assembly[] Scanned =
        [Assembly.Load("ArquitecturaBase.Application"), Assembly.Load("ArquitecturaBase.Infrastructure"), Assembly.Load("ArquitecturaBase.Api")];

    [Fact]
    public void Only_the_owner_services_create_from_an_assistant_proposal()
    {
        var calls = Scanned.SelectMany(CallSites.Calls).ToArray();
        Assert.Equal([Services + "FormCatalogAssistantImportPolicy", Services + "FormCatalogAssistantProposalService", Services + "FormDraftProposalService"],
            Callers(calls, typeof(IAssistantProposalLookup), nameof(IAssistantProposalLookup.FindAsync)));
        Assert.Equal([Services + "FormDraftProposalService"], Callers(calls, typeof(SaveFormDraftRequest), "set_Proposal"));
        Assert.Equal([Services + "FormDraftService"], Callers(calls, typeof(BusinessFormDefinition), nameof(BusinessFormDefinition.RecordProposal)));
        Assert.Equal([Services + "FormCatalogAssistantProposalService"],
            Callers(calls, typeof(BusinessFormCatalog), nameof(BusinessFormCatalog.CreateProposedManual)));
        Assert.Equal([Services + "FormCatalogProposalService"], Callers(calls, typeof(BusinessFormCatalog), nameof(BusinessFormCatalog.CreateProposedFilters)));
    }

    [Fact]
    public void The_rule_sees_a_draft_that_records_a_proposal_somewhere_else()
    {
        var calls = CallSites.Calls(typeof(FormAssistantProposalCreationTests).Assembly);
        Assert.Contains(typeof(StrayProposalRecorder).FullName!, Callers(calls, typeof(BusinessFormDefinition), nameof(BusinessFormDefinition.RecordProposal)));
        Assert.Contains(typeof(StrayProposalRecorder).FullName!, Callers(calls, typeof(SaveFormDraftRequest), "set_Proposal"));
    }

    private static string[] Callers(IEnumerable<CallSites.Call> calls, Type type, string method) =>
        [.. calls.Where(call => call.DeclaringType == type.FullName && call.Method == method)
            .Select(call => call.Owner).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}

// Control only: its IL must be detected; it is never executed.
file static class StrayProposalRecorder
{
    public static SaveFormDraftRequest Record(BusinessFormDefinition definition, SaveFormDraftRequest request)
    {
        definition.RecordProposal(Guid.NewGuid(), "{}");
        return request with { Proposal = new(Guid.NewGuid(), "{}") };
    }
}
