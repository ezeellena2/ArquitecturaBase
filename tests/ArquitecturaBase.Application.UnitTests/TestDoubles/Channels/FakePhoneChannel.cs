using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Channels;

/// <summary>
/// Un canal telefónico con lo que el test le diga: prendido o apagado, sus países y su número. Acepta cualquier número,
/// salvo que el test le dé un <see cref="Rejection"/>; guarda los que miró (<see cref="Checked"/>).
/// </summary>
internal sealed class FakePhoneChannel(bool isEnabled, IReadOnlyList<string>? countries = null, string? displayNumber = null)
    : IPhoneChannel
{
    public bool IsEnabled { get; } = isEnabled;

    public IReadOnlyList<string> Countries { get; } = countries ?? [];

    public string? DisplayNumber { get; } = displayNumber;

    /// <summary>El error con que rechaza todo número, como un canal que no le manda a ese país; null acepta todo.</summary>
    public Error? Rejection { get; init; }

    public List<PhoneNumber> Checked { get; } = [];

    public Result EnsureCanSendTo(PhoneNumber phone)
    {
        Checked.Add(phone);

        return Rejection is { } error ? error : Result.Success();
    }
}
