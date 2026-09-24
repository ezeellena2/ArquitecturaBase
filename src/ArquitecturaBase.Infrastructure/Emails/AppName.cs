using ArquitecturaBase.Application.Interfaces.Integrations;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>El nombre del sistema es el de los correos (<c>Email:AppName</c>): uno solo para todo lo que ve la persona.</summary>
internal sealed class AppName(IOptions<EmailOptions> options) : IAppName
{
    public string Value => options.Value.AppName;
}
