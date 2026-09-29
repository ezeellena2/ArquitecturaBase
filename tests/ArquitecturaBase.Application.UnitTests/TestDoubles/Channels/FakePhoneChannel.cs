using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Channels;

/// <summary>
/// Un canal telefónico con lo que el test le diga: prendido o apagado, sus países y su número. Acepta cualquier número.
/// </summary>
internal sealed class FakePhoneChannel(bool isEnabled, IReadOnlyList<string>? countries = null, string? displayNumber = null)
    : IPhoneChannel
{
    public bool IsEnabled { get; } = isEnabled;

    public IReadOnlyList<string> Countries { get; } = countries ?? [];

    public string? DisplayNumber { get; } = displayNumber;

    public Result EnsureCanSendTo(PhoneNumber phone) => Result.Success();
}
