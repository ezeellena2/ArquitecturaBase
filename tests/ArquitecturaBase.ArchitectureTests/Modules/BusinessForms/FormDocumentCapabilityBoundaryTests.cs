using System.Reflection;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

namespace ArquitecturaBase.ArchitectureTests.Modules.BusinessForms;

/// <summary>
/// Task 7: the documents capabilities read the forms with reading through their reader and the turn's document from the request; they
/// never open the private copies, write, register a document or take the inbox's port, which belong to the services that own a unit of
/// work. The general rule of capability sources covers them too; this names them so a move or a rename does not leave them unseen.
/// </summary>
public sealed class FormDocumentCapabilityBoundaryTests
{
    private static readonly Assembly Application = typeof(IAssistantCapabilitySource).Assembly;
    private const string Source = "ArquitecturaBase.Application.Modules.BusinessForms.Channels.BusinessFormDocumentAssistantCapabilitySource";

    [Fact]
    public void The_documents_capabilities_never_receive_the_private_files_a_unit_of_work_a_repository_or_the_inbox_port()
    {
        var source = Application.GetType(Source, throwOnError: true)!;
        Assert.True(typeof(IAssistantCapabilitySource).IsAssignableFrom(source));
        var dependencies = Dependencies(source).ToArray();
        Assert.Contains(typeof(IFormReceivedDocumentReader), dependencies);
        Type[] forbidden = [typeof(IPrivateFileStore), typeof(IUnitOfWork), typeof(IFormReceivedDocumentRepository), typeof(IStructuredDocumentChannel),
            typeof(IStructuredAttachmentReader)];
        Assert.Empty(dependencies.Intersect(forbidden));
    }

    // The constructor dependencies of the source and of the concrete helpers of Application it receives, recursively.
    private static IEnumerable<Type> Dependencies(Type type)
    {
        var visited = new HashSet<Type>();
        var pending = new Queue<Type>([type]);
        while (pending.TryDequeue(out var current))
        {
            if (!visited.Add(current)) continue;
            foreach (var dependency in current.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType))
            {
                yield return dependency;
                if (dependency.Assembly == Application && dependency is { IsInterface: false, IsAbstract: false }) pending.Enqueue(dependency);
            }
        }
    }
}
