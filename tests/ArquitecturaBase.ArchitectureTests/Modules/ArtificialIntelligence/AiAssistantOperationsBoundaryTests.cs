using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantOperationsBoundaryTests
{
    [Fact]
    public void The_neutral_operations_port_is_an_entry_point_but_its_helper_is_not()
    {
        var assembly = typeof(IAssistantOperations).Assembly;
        var operations = assembly.GetType(
            "ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services.AiAssistantOperations", throwOnError: true)!;
        var submissions = assembly.GetType(
            "ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services.AiAssistantSubmissionPolicy", throwOnError: true)!;

        Assert.Contains(typeof(IAssistantOperations), operations.GetInterfaces());
        Assert.True(UseCaseEntryPoints.Contains(operations));
        Assert.False(UseCaseEntryPoints.Contains(submissions));
        Assert.False(UseCaseEntryPoints.Contains(typeof(IAssistantOperations)));
    }
}
