using ArquitecturaBase.Application.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests;

/// <summary>
/// La parte del módulo WhatsApp de ApplicationHelpersTests: registra el módulo como Program.cs, así la convención de
/// helpers también mira los suyos. Lleva el namespace de la clase del núcleo, no el de su carpeta, para ser otra parte de
/// la misma clase.
/// </summary>
public sealed partial class ApplicationHelpersTests
{
    static partial void AddModules(IServiceCollection services) => services.AddWhatsAppApplication();
}
