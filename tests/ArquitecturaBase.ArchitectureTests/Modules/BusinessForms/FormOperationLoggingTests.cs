using System.Reflection;
using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests.Modules.BusinessForms;

public sealed class FormOperationLoggingTests
{
    private const string OperationLogOwner = "ArquitecturaBase.Application.Common.Logging.OperationLog";

    [Fact]
    public void Every_form_service_entry_uses_operation_log()
    {
        var calls = CallSites.Calls(Assembly.Load("ArquitecturaBase.Application"));
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["ArquitecturaBase.Application.Modules.BusinessForms.Services.BusinessFormPublicationService"] = 1,
            ["ArquitecturaBase.Application.Modules.BusinessForms.Services.FormDraftService"] = 6,
            ["ArquitecturaBase.Application.Modules.BusinessForms.Services.FormDraftRetentionService"] = 1,
            ["ArquitecturaBase.Application.Modules.BusinessForms.Services.FormPreviewService"] = 1,
            ["ArquitecturaBase.Application.Modules.BusinessForms.Services.FormPublicationAcceptanceService"] = 1,
            ["ArquitecturaBase.Application.Modules.BusinessForms.Services.FormPublicationDispatchService"] = 2,
            ["ArquitecturaBase.Application.Modules.BusinessForms.Services.FormPublicationStatusService"] = 1,
        };

        foreach (var (service, expectedEntries) in expected)
        {
            var actual = calls.Count(call => call.Owner == service && call.DeclaringType == OperationLogOwner && call.Method == "RunAsync");
            Assert.Equal(expectedEntries, actual);
        }
    }
}
