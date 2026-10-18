using System.Reflection;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantCapabilityBoundaryTests
{
    private const string Executor = "ArquitecturaBase.Infrastructure.Assistant.ScopedAssistantCapabilityExecutor";
    private static readonly Assembly Application = typeof(IAssistantCapabilitySource).Assembly;
    private static readonly Assembly Infrastructure = Assembly.Load("ArquitecturaBase.Infrastructure");
    private static readonly Assembly Api = Assembly.Load("ArquitecturaBase.Api");

    [Fact]
    public void Capabilities_have_only_read_and_proposal_kinds() =>
        Assert.Equal(["Read", "Proposal"], Enum.GetNames<AssistantCapabilityKind>());

    [Fact]
    public void Every_model_generation_call_stays_inside_the_existing_engine()
    {
        var calls = new[] { Application, Infrastructure, Api }.SelectMany(CallSites.Calls).ToArray();
        const string providers = "ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.";
        const string router = providers + "LlmConversationClient";
        const string openAi = providers + "OpenAI.OpenAiConversationClient";
        const string anthropic = providers + "Anthropic.AnthropicConversationClient";
        AssertOnlyCallers(calls, typeof(ILlmConversationClient).FullName!,
            "ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services.AiAssistantExecutionService");
        AssertOnlyCallers(calls, openAi, router);
        AssertOnlyCallers(calls, anthropic, router);
        AssertOnlyCallers(calls, providers + "LlmConversationHttpTransport", openAi, anthropic);
        Assert.DoesNotContain(calls, call => call.DeclaringType == router && call.Method == "SendAsync");
        Assert.Equal([anthropic, router, openAi], Infrastructure.GetTypes()
            .Where(type => !type.IsAbstract && !type.IsInterface && typeof(ILlmConversationClient).IsAssignableFrom(type))
            .Select(type => type.FullName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Capability_sources_and_their_local_helpers_cannot_receive_writers_or_external_transports()
    {
        var sources = Application.GetTypes().Where(type => !type.IsAbstract && !type.IsInterface
            && typeof(IAssistantCapabilitySource).IsAssignableFrom(type)).ToArray();
        Assert.NotEmpty(sources);
        foreach (var source in sources)
        {
            var forbidden = LocalDependencies(source, Application).Where(IsForbiddenDependency)
                .Select(type => type.FullName).Distinct(StringComparer.Ordinal).ToArray();
            Assert.True(forbidden.Length == 0, source.FullName + ": " + string.Join(", ", forbidden));
        }
    }

    [Fact]
    public void The_dependency_detector_rejects_writers_and_transports_and_accepts_local_read_ports()
    {
        Type[] forbidden = [typeof(IUnitOfWork), typeof(IServiceProvider), typeof(IRoleService), typeof(IUserRepository),
            typeof(ILlmConversationClient), typeof(HttpClient), typeof(IEnumerable<IUnitOfWork>),typeof(IOrdinaryDependency),typeof(IInnocentReader),typeof(IEnumerable<IInnocentReader>)];
        Assert.All(forbidden, dependency => Assert.True(IsForbiddenDependency(dependency), dependency.FullName));
        Type[] allowed = [typeof(IUserReader), typeof(IPermissionService), typeof(TimeProvider), typeof(IEnumerable<IUserReader>)];
        Assert.All(allowed, dependency => Assert.False(IsForbiddenDependency(dependency), dependency.FullName));
    }

    [Fact]
    public void The_read_port_allowlist_is_enforced_through_local_helper_dependencies()
    {
        var local=typeof(AiAssistantCapabilityBoundaryTests).Assembly;
        Assert.Contains(LocalDependencies(typeof(EnvelopeRoot),local),dependency=>IsForbiddenDependency(dependency,local));
        Assert.DoesNotContain(LocalDependencies(typeof(ReadEnvelopeRoot),local),dependency=>IsForbiddenDependency(dependency,local));
    }

    [Fact]
    public void The_generalized_executor_replaces_the_old_query_registration()
    {
        var uses = CallSites.TypeUses(Infrastructure)
            .Where(use => use.Owner == "ArquitecturaBase.Infrastructure.DependencyInjection").ToArray();
        Assert.Contains(uses, use => use.Type == "ArquitecturaBase.Application.Interfaces.Integrations.Assistant.IAssistantCapabilityExecutor");
        Assert.Contains(uses, use => use.Type == Executor);
        Assert.DoesNotContain(uses, use => use.Type.Contains("SystemQueryExecutor", StringComparison.Ordinal));
        Assert.Null(Infrastructure.GetType("ArquitecturaBase.Infrastructure.SystemQueries.ScopedSystemQueryExecutor"));
        Assert.Null(Application.GetType("ArquitecturaBase.Application.Interfaces.Channels.ISystemQuerySource"));
        Assert.Null(Application.GetType("ArquitecturaBase.Application.Interfaces.Integrations.SystemQueries.ISystemQueryExecutor"));
    }

    [Fact]
    public void Local_capability_execution_has_one_technical_scope_and_no_other_execution_path()
    {
        var executorCalls = CallSites.Calls(Infrastructure).Where(call => call.Owner == Executor).ToArray();
        Assert.NotEmpty(executorCalls);
        Assert.Single(executorCalls, call => call.Method == "CreateAsyncScope");
        Assert.DoesNotContain(executorCalls, call => call.Method == "ExecuteInTransactionAsync"
            || call.DeclaringType == typeof(ILlmConversationClient).FullName);
        var sourceCalls = new[] { Application, Infrastructure, Api }.SelectMany(CallSites.Calls)
            .Where(call => call.DeclaringType == typeof(IAssistantCapabilitySource).FullName && call.Method == "ExecuteAsync").ToArray();
        Assert.NotEmpty(sourceCalls);
        Assert.All(sourceCalls, call => Assert.Equal(Executor, call.Owner));
    }

    private static void AssertOnlyCallers(IEnumerable<CallSites.Call> calls, string transport, params string[] allowedOwners)
    {
        var matches = calls.Where(call => call.DeclaringType == transport && call.Method == "SendAsync").ToArray();
        Assert.NotEmpty(matches);
        Assert.All(matches, call => Assert.Contains(call.Owner, allowedOwners));
    }

    private sealed class EnvelopeRoot(Envelope helper) { public Envelope Helper {get;}=helper; }
    private sealed class Envelope(IInnocentReader dependency) { public IInnocentReader Dependency {get;}=dependency; }
    private sealed class ReadEnvelopeRoot(ReadEnvelope helper) { public ReadEnvelope Helper {get;}=helper; }
    private sealed class ReadEnvelope(IUserReader dependency) { public IUserReader Dependency {get;}=dependency; }
    private interface IOrdinaryDependency { Task SendAsync(); }
    private interface IInnocentReader { void Save(); }

    private static readonly HashSet<Type> ReadPorts = new Type[]
    {
        typeof(IUserReader),typeof(IRoleReader),typeof(IPermissionService),typeof(IPermissionCatalog),typeof(IPermissionCatalogSource),typeof(TimeProvider),
    }.Concat(new[]
    {
        "ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence.IAiBusinessInformationReader",
        "ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence.IWhatsAppAssistantReader",
        "ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence.IFormSelectionReader",
        "ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence.IFormMissingRequestReader",
        "ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence.IFormResponseReader",
        "ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence.IFormSuggestionReader",
        "ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence.IFormCatalogReader",
        "ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence.IFormCatalogFilterReader",
    }.Select(name=>Application.GetType(name)).OfType<Type>()).ToHashSet();

    private static bool IsForbiddenDependency(Type dependency)=>IsForbiddenDependency(dependency,Application);
    private static bool IsForbiddenDependency(Type dependency,Assembly localAssembly)
    {
        if(dependency.IsGenericType && dependency.GetGenericTypeDefinition()==typeof(IEnumerable<>))
            return IsForbiddenDependency(dependency.GenericTypeArguments[0],localAssembly);
        if(ReadPorts.Contains(dependency))return false;
        return dependency.Assembly!=localAssembly || dependency.IsInterface || dependency.IsAbstract;
    }

    private static IEnumerable<Type> LocalDependencies(Type source, Assembly application)
    {
        var visited = new HashSet<Type>();
        var pending = new Queue<Type>();
        pending.Enqueue(source);
        while (pending.TryDequeue(out var current))
        {
            if (!visited.Add(current)) continue;
            foreach (var dependency in current.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType))
            {
                yield return dependency;
                var localType = dependency.IsGenericType && dependency.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                    ? dependency.GenericTypeArguments[0] : dependency;
                if (localType.Assembly != application) continue;
                if (!localType.IsInterface && !localType.IsAbstract) pending.Enqueue(localType);
                else foreach (var implementation in application.GetTypes().Where(type => !type.IsInterface && !type.IsAbstract
                    && localType.IsAssignableFrom(type))) pending.Enqueue(implementation);
            }
        }
    }
}
