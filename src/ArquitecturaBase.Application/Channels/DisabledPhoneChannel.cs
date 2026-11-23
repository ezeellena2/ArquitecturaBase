using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Channels;

/// <summary>
/// El canal telefónico cuando ningún módulo lo da: apagado, sin países ni número. Acepta un número de cualquier país:
/// sin canal no hay tarifa por país, y una cuenta puede tener solo teléfono (decisión 4 de la Etapa 6). AddApplication lo
/// registra con TryAdd; un módulo que da el canal lo reemplaza.
/// </summary>
internal sealed class DisabledPhoneChannel : IPhoneChannel
{
    public bool IsEnabled => false;

    public Task<IReadOnlyList<string>> ListCountriesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

    public string? DisplayNumber => null;

    public Task<Result> EnsureCanSendToAsync(PhoneNumber phone, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
}
