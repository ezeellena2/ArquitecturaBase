using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Caching;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AssistantQualification;

internal static class QualificationHost
{
    // Public metadata from Api.csproj; this is NOT a key. Never list the secret store.
    private const string ApiUserSecretsId = "4f8d2c1a-9b3e-4d7f-a6c5-2e1b8f9d0a47";

    public static IHost Build(QualificationSettings settings)
    {
        // This method is called only after an explicit --live and isolation checks.
        // No ApiFactory, Api Program, AppHost, web listener or InitializeDatabaseAsync.
        var shared = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(settings.RepositoryRoot, "src/ArquitecturaBase.Api/appsettings.json"), optional: false)
            .AddUserSecrets(ApiUserSecretsId)
            .AddEnvironmentVariables()
            .Build();
        var sourceServices = new ServiceCollection();
        sourceServices.AddLogging(logging => logging.ClearProviders());
        sourceServices.AddSingleton(TimeProvider.System);
        sourceServices.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(settings.SourceConnectionString)
            .UseOpenIddict<Guid>().UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        // Registers the actual internal reader through its public owning registration.
        // This provider is never started; only its IAiConnectionReader is resolved by the bridge.
        sourceServices.AddArtificialIntelligenceInfrastructure(shared);
        var source = new SourceConnectionServices(sourceServices.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true, ValidateOnBuild = false,
        }));
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = "Testing", ApplicationName = "AssistantQualification",
            ContentRootPath = settings.RepositoryRoot, Args = [],
        });
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddConfiguration(shared).AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:appdb"] = settings.EvaluationConnectionString,
            ["ConnectionStrings:cache"] = settings.RedisConnectionString,
            ["Caching:KeyPrefix"] = "AssistantQualification:" + settings.RunId,
            ["Authentication:Issuer"] = "https://qualification.example.invalid",
            ["Seed:AdminEmail"] = "unused-admin@example.invalid",
            ["Email:Delivery"] = "PickupDirectory",
            ["Email:PickupDirectory"] = Path.Combine(settings.OutputDirectory, "mail-unused"),
            ["FileStorage:Enabled"] = "false",
            ["WhatsApp:PhoneNumberId"] = "",
            ["BusinessForms:PublicationWorker:ProcessInBackground"] = "false",
            ["BusinessForms:ActionWorker:ProcessInBackground"] = "false",
        });
        builder.AddRedisCaching();
        ArquitecturaBase.Application.DependencyInjection.AddApplication(builder.Services);
        ArquitecturaBase.Infrastructure.DependencyInjection.AddInfrastructure(builder.Services, builder.Configuration, builder.Environment);
        builder.Services.AddArtificialIntelligenceApplication().AddArtificialIntelligenceInfrastructure(builder.Configuration);
        builder.Services.AddBusinessFormsApplication().AddBusinessFormsInfrastructure();
        builder.Services.AddWhatsAppApplication().AddWhatsAppInfrastructure(builder.Configuration);
        QualificationDataProtection.Configure(builder.Services, settings);
        // The hypothesis is local to this isolated host. It is never a production qualification declaration.
        builder.Services.PostConfigure<AiAssistantOptions>(options =>
        {
            options.ProcessInBackground = true;
            options.ModelProfiles = [new(AiProvider.OpenAI, "gpt-6.1-sol", 131072, true, true) { Id = "gpt-6.1-sol-candidate-1", Version = 1 }];
            options.AudioModelProfiles = [new(AiProvider.OpenAI, "gpt-transcribe", "openai-stt-candidate", 1, ["audio/ogg", "audio/wav"], 25000000)];
            options.Limits = new AiAssistantLimits();
        });
        builder.Services.AddSingleton<SourceConnectionServices>(_ => source);
        builder.Services.RemoveAll<IAiConnectionReader>();
        builder.Services.AddScoped<IAiConnectionReader, ReadOnlyAiConnectionReader>();
        builder.Services.AddScoped<QualificationCurrentUser>();
        builder.Services.Replace(ServiceDescriptor.Scoped<ICurrentUser>(provider => provider.GetRequiredService<QualificationCurrentUser>()));
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<QualificationMeasurements>();
        builder.Services.AddScoped<SafeRunLedger>();
        builder.Services.AddScoped<QualificationSeed>();
        builder.Services.AddScoped<HomeRunner>();
        builder.Services.AddScoped<ChannelRunner>();
        builder.Services.AddScoped<AudioRunner>();
        builder.Services.AddScoped<QualificationFormSeed>();
        builder.Services.RemoveAll<IPrivateFileStore>();
        builder.Services.AddSingleton<IPrivateFileStore>(_ => new QualificationPrivateFiles(Path.Combine(settings.OutputDirectory, "private-audio")));
        builder.Services.AddScoped<StatusRunner>();
        builder.Services.AddScoped<SafeTurnMetadata>();
        // Leave the real AI hosted worker + retention worker registered. Do not resolve the internal coordinator.
        return builder.Build();
    }
}
