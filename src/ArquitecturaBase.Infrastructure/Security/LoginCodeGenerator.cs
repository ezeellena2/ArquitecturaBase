using System.Security.Cryptography;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Application.Features.Auth;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Security;

internal sealed class LoginCodeGenerator(IOptions<LoginCodeOptions> options) : ILoginCodeGenerator
{
    private const string Digits = "0123456789";

    public string Generate() => RandomNumberGenerator.GetString(Digits, options.Value.Length);
}
