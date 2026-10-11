using System.Reflection;
using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests.Modules.WhatsApp;

public sealed class FlowOperationLoggingTests
{
    private const string OperationLogOwner = "ArquitecturaBase.Application.Common.Logging.OperationLog";

    [Fact]
    public void Every_flow_service_entry_uses_operation_log()
    {
        var calls = CallSites.Calls(Assembly.Load("ArquitecturaBase.Application"));
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["ArquitecturaBase.Application.Modules.WhatsApp.Services.WhatsAppFlowDispatchService"] = 4,
            ["ArquitecturaBase.Application.Modules.WhatsApp.Services.WhatsAppFlowPublicationService"] = 1,
            ["ArquitecturaBase.Application.Modules.WhatsApp.Services.WhatsAppFlowStatusService"] = 2,
        };

        foreach (var (service, expectedEntries) in expected)
        {
            var actual = calls.Count(call => call.Owner == service && call.DeclaringType == OperationLogOwner && call.Method == "RunAsync");
            Assert.Equal(expectedEntries, actual);
        }
    }
}
