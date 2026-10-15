using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Files;

internal static class FileStorageRegistration
{
    public static IServiceCollection AddFileStorage(this IServiceCollection services)
    {
        services.AddOptions<FileStorageOptions>().BindConfiguration(FileStorageOptions.SectionName)
            .Validate(options => options.IsValid(), "FileStorage requires a private container and an explicit Azure credential mode.")
            .ValidateOnStart();
        services.AddSingleton<IPrivateFileStore, AzureBlobFileStore>();
        services.AddSingleton<IFileContentProcessor, FileContentProcessor>();
        return services;
    }
}
