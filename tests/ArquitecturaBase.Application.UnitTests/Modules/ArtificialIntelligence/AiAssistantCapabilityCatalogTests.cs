using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantCapabilityCatalogTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static AssistantChannelContext Context(Guid actor, params AssistantCapabilityReference[] allowed) =>
        new("home", actor, null, "en", "UTC", new([ArtificialIntelligencePermissions.AssistantUse], []), allowed, [new("assistant_answer", 1)]);

    [Fact]
    public async Task A_channel_offers_only_exact_registered_versions_with_current_permissions()
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [ArtificialIntelligencePermissions.AssistantUse, "users.read"];
        var source = new PreparedAssistantCapabilitySource([
            PreparedAssistantCapabilitySource.Definition("users.summary", 1, AssistantCapabilityKind.Read, "users.read"),
            PreparedAssistantCapabilitySource.Definition("roles.list", 1, AssistantCapabilityKind.Read, "roles.read"),
            PreparedAssistantCapabilitySource.Definition("forms.offer", 1, AssistantCapabilityKind.Proposal, "forms.create")]);
        var catalog = new AiAssistantCapabilityCatalogPolicy([source], permissions);
        var context = Context(actor, new("users.summary", 1), new("roles.list", 1));
        var result = await catalog.BuildAsync(context, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("users.summary", Assert.Single(result.Value.Capabilities).Id);
        Assert.Equal("assistant_answer", Assert.Single(result.Value.Terminals).Id);
        Assert.Equal(0, source.Calls);
        permissions.Permissions[actor] = [ArtificialIntelligencePermissions.AssistantUse];
        var revoked = await catalog.BuildAsync(context, Ct);
        Assert.True(revoked.IsSuccess);
        Assert.Empty(revoked.Value.Capabilities);
        Assert.Equal(0, source.Calls);
    }

    [Theory]
    [InlineData(11,false,true)]
    [InlineData(12,false,false)]
    [InlineData(12,true,true)]
    public async Task The_twelve_tool_limit_includes_the_terminal_after_permission_filtering(int count,bool filterLast,bool accepted)
    {
        var actor=Guid.NewGuid();var permissions=new FakePermissionService();permissions.Permissions[actor]=[ArtificialIntelligencePermissions.AssistantUse];
        var definitions=Enumerable.Range(0,count).Select(index=>PreparedAssistantCapabilitySource.Definition($"fictional.read-{index}",1,AssistantCapabilityKind.Read,filterLast && index==count-1?["missing.permission"]:[])).ToArray();
        var context=Context(actor,definitions.Select(definition=>new AssistantCapabilityReference(definition.Id,definition.Version)).ToArray());
        var result=await new AiAssistantCapabilityCatalogPolicy([new PreparedAssistantCapabilitySource(definitions)],permissions).BuildAsync(context,Ct);
        Assert.Equal(accepted,result.IsSuccess);if(!accepted)Assert.Equal(AiAssistantErrors.ToolRejected,result.Error);
    }
    [Fact]
    public async Task An_unknown_version_is_not_silently_replaced_with_the_installed_version()
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [ArtificialIntelligencePermissions.AssistantUse];
        var source = new PreparedAssistantCapabilitySource([PreparedAssistantCapabilitySource.Definition("system.help", 2)]);
        var catalog = new AiAssistantCapabilityCatalogPolicy([source], permissions);
        var result = await catalog.BuildAsync(Context(actor, new AssistantCapabilityReference("system.help", 1)), Ct);
        Assert.Equal(AiAssistantErrors.ToolRejected, result.Error);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void Duplicate_capability_versions_fail_composition_before_an_operation_can_start()
    {
        var definition = PreparedAssistantCapabilitySource.Definition();
        Assert.Throws<InvalidOperationException>(() => new AiAssistantCapabilityCatalogPolicy(
            [new PreparedAssistantCapabilitySource([definition]), new PreparedAssistantCapabilitySource([definition])], new FakePermissionService()));
    }

    [Fact]
    public async Task A_nonhome_channel_does_not_inherit_the_home_permission()
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = ["fictional.channel.use", "fictional.read"];
        var source = new PreparedAssistantCapabilitySource([PreparedAssistantCapabilitySource.Definition("fictional.read", 1, AssistantCapabilityKind.Read, "fictional.read")]);
        var context = new AssistantChannelContext("owner-supplied-channel", actor, null, "es", "America/Argentina/Buenos_Aires",
            new(["fictional.channel.use"], []), [new("fictional.read", 1)], [new("assistant_answer", 1)]);
        var result = await new AiAssistantCapabilityCatalogPolicy([source], permissions).BuildAsync(context, Ct);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Capabilities);
        Assert.DoesNotContain(ArtificialIntelligencePermissions.AssistantUse, context.RequiredPermissions.Actor);
    }

    [Fact]
    public async Task A_manual_operator_and_recipient_satisfy_their_own_requirements_without_home_permission()
    {
        var actor = Guid.NewGuid(); var subject = Guid.NewGuid(); var permissions = new FakePermissionService();
        permissions.Permissions[actor] = ["fictional.channel.read", "fictional.channel.send"];
        permissions.Permissions[subject] = ["fictional.records.create"];
        var definition = PreparedAssistantCapabilitySource.Definition("fictional.propose", 1, AssistantCapabilityKind.Proposal)
            with { RequiredPermissions = new([], ["fictional.records.create"]) };
        var source = new PreparedAssistantCapabilitySource([definition]);
        var context = new AssistantChannelContext("owner-channel", actor, subject, "es", "UTC",
            new(["fictional.channel.read", "fictional.channel.send"], []), [new("fictional.propose", 1)], [new("assistant_proposal", 1)]);
        var result = await new AiAssistantCapabilityCatalogPolicy([source], permissions).BuildAsync(context, Ct);
        Assert.True(result.IsSuccess); Assert.Single(result.Value.Capabilities); Assert.Equal(0, source.Calls);
        Assert.Equal(new[]{"fictional.channel.read", "fictional.channel.send"}, result.Value.RequiredPermissions.Actor.Order(StringComparer.Ordinal));
        Assert.Equal(new[]{"fictional.records.create"}, result.Value.RequiredPermissions.Subject);
        Assert.DoesNotContain(ArtificialIntelligencePermissions.AssistantUse, result.Value.RequiredPermissions.Actor);
    }

    [Fact]
    public async Task Actor_permissions_never_satisfy_a_subject_requirement()
    {
        var actor = Guid.NewGuid(); var subject = Guid.NewGuid(); var permissions = new FakePermissionService();
        permissions.Permissions[actor] = ["fictional.channel.read", "fictional.records.create"];
        permissions.Permissions[subject] = [];
        var definition = PreparedAssistantCapabilitySource.Definition("fictional.propose", 1, AssistantCapabilityKind.Proposal)
            with { RequiredPermissions = new([], ["fictional.records.create"]) };
        var context = new AssistantChannelContext("owner-channel", actor, subject, "es", "UTC",
            new(["fictional.channel.read"], []), [new("fictional.propose", 1)], [new("assistant_proposal", 1)]);
        var result = await new AiAssistantCapabilityCatalogPolicy([new PreparedAssistantCapabilitySource([definition])], permissions).BuildAsync(context, Ct);
        Assert.True(result.IsSuccess); Assert.Empty(result.Value.Capabilities);
    }

    [Fact]
    public async Task Subject_permissions_never_satisfy_an_actor_channel_requirement()
    {
        var actor = Guid.NewGuid(); var subject = Guid.NewGuid(); var permissions = new FakePermissionService();
        permissions.Permissions[actor] = []; permissions.Permissions[subject] = ["fictional.channel.read"];
        var context = new AssistantChannelContext("owner-channel", actor, subject, "es", "UTC",
            new(["fictional.channel.read"], []), [], [new("assistant_proposal", 1)]);
        var result = await new AiAssistantCapabilityCatalogPolicy([], permissions).BuildAsync(context, Ct);
        Assert.Equal(AiAssistantErrors.Forbidden, result.Error);
    }

    [Fact]
    public async Task Subject_permissions_are_rechecked_and_never_fall_back_to_the_actor()
    {
        var actor = Guid.NewGuid(); var subject = Guid.NewGuid(); var permissions = new FakePermissionService();
        permissions.Permissions[actor] = ["fictional.channel.read", "fictional.records.create"];
        permissions.Permissions[subject] = ["fictional.records.create"];
        var definition = PreparedAssistantCapabilitySource.Definition("fictional.propose", 1, AssistantCapabilityKind.Proposal)
            with { RequiredPermissions = new([], ["fictional.records.create"]) };
        var context = new AssistantChannelContext("owner-channel", actor, subject, "es", "UTC",
            new(["fictional.channel.read"], []), [new("fictional.propose", 1)], [new("assistant_proposal", 1)]);
        var catalog = new AiAssistantCapabilityCatalogPolicy([new PreparedAssistantCapabilitySource([definition])], permissions);
        Assert.Single((await catalog.BuildAsync(context, Ct)).Value.Capabilities);
        permissions.Permissions[subject] = [];
        Assert.Empty((await catalog.BuildAsync(context, Ct)).Value.Capabilities);
        Assert.Empty((await catalog.BuildAsync(context with { SubjectUserId = null }, Ct)).Value.Capabilities);
    }

    [Fact]
    public void Distinct_versions_share_the_registry_without_replacing_each_other()
    {
        var first = PreparedAssistantCapabilitySource.Definition("fictional.read", 1);
        var second = PreparedAssistantCapabilitySource.Definition("fictional.read", 2);
        var catalog = new AiAssistantCapabilityCatalogPolicy([new PreparedAssistantCapabilitySource([first, second])], new FakePermissionService());
        Assert.Equal(2, catalog.Definitions.Count);
        Assert.Same(first, catalog.Find(new("fictional.read", 1)));
        Assert.Same(second, catalog.Find(new("fictional.read", 2)));
        Assert.Null(catalog.Find(new("fictional.read", 3)));
        Assert.Equal(AiAssistantErrors.ToolRejected, catalog.ParseOutput(new("fictional.read", 3), "{}").Error);
    }

    [Theory]
    [InlineData("inputSchema")]
    [InlineData("outputSchema")]
    [InlineData("inputBudget")]
    [InlineData("outputBudget")]
    [InlineData("itemsBudget")]
    [InlineData("timeoutBudget")]
    [InlineData("kind")]
    public void Invalid_capability_contracts_fail_composition(string invalid)
    {
        var definition = PreparedAssistantCapabilitySource.Definition();
        definition = invalid switch
        {
            "inputSchema" => definition with { InputSchemaJson = "[]" },
            "outputSchema" => definition with { OutputSchemaJson = "{\"type\":\"object\",\"additionalProperties\":true}" },
            "inputBudget" => definition with { Budget = definition.Budget with { InputBytes = 0 } },
            "outputBudget" => definition with { Budget = definition.Budget with { OutputBytes = 0 } },
            "itemsBudget" => definition with { Budget = definition.Budget with { MaximumItems = -1 } },
            "timeoutBudget" => definition with { Budget = definition.Budget with { TimeoutSeconds = 0 } },
            _ => definition with { Kind = (AssistantCapabilityKind)99 },
        };
        Assert.Throws<InvalidOperationException>(() => new AiAssistantCapabilityCatalogPolicy([new PreparedAssistantCapabilitySource([definition])], new FakePermissionService()));
    }

    [Theory]
    [InlineData("emptyChannel")]
    [InlineData("emptyActor")]
    [InlineData("unknownTerminal")]
    [InlineData("unknownTerminalVersion")]
    [InlineData("duplicateTerminal")]
    [InlineData("duplicateReference")]
    [InlineData("ambiguousVersions")]
    [InlineData("invalidCulture")]
    [InlineData("invalidTimeZone")]
    public async Task Invalid_channels_are_rejected_before_offering_tools(string invalid)
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [ArtificialIntelligencePermissions.AssistantUse];
        var first = PreparedAssistantCapabilitySource.Definition("fictional.read", 1);
        var second = PreparedAssistantCapabilitySource.Definition("fictional.read", 2);
        var catalog = new AiAssistantCapabilityCatalogPolicy([new PreparedAssistantCapabilitySource([first, second])], permissions);
        var context = Context(actor, new AssistantCapabilityReference("fictional.read", 1));
        context = invalid switch
        {
            "emptyChannel" => context with { ChannelId = " " },
            "emptyActor" => context with { ActorUserId = Guid.Empty },
            "unknownTerminal" => context with { AllowedTerminals = [new("fictional_terminal", 1)] },
            "unknownTerminalVersion" => context with { AllowedTerminals = [new("assistant_answer", 2)] },
            "duplicateTerminal" => context with { AllowedTerminals = [new("assistant_answer", 1), new("assistant_answer", 1)] },
            "duplicateReference" => context with { AllowedCapabilities = [new("fictional.read", 1), new("fictional.read", 1)] },
            "ambiguousVersions" => context with { AllowedCapabilities = [new("fictional.read", 1), new("fictional.read", 2)] },
            "invalidCulture" => context with { Culture = "not a culture" },
            _ => context with { TimeZoneId = "fictional/time-zone" },
        };
        Assert.Equal(AiAssistantErrors.ToolRejected, (await catalog.BuildAsync(context, Ct)).Error);
    }

    [Fact]
    public async Task Global_subject_requirements_need_that_explicit_identity_and_its_current_permissions()
    {
        var actor = Guid.NewGuid();
        var subject = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [ArtificialIntelligencePermissions.AssistantUse, "fictional.subject.use"];
        permissions.Permissions[subject] = ["fictional.subject.use"];
        var catalog = new AiAssistantCapabilityCatalogPolicy([], permissions);
        var context = Context(actor) with { SubjectUserId = subject, RequiredPermissions = new([ArtificialIntelligencePermissions.AssistantUse], ["fictional.subject.use"]) };
        Assert.True((await catalog.BuildAsync(context, Ct)).IsSuccess);
        Assert.Equal(AiAssistantErrors.Forbidden, (await catalog.BuildAsync(context with { SubjectUserId = null }, Ct)).Error);
        permissions.Permissions[subject] = [];
        Assert.Equal(AiAssistantErrors.Forbidden, (await catalog.BuildAsync(context, Ct)).Error);
    }
}
