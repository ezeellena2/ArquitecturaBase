using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Channels;

/// <summary>
/// Un participante que anota cada aviso en <paramref name="events"/>, con su nombre adelante ("participant-lock:" y la
/// cuenta, "participant-confirmed:", "participant-released:"): con el repositorio de enlaces anotando en la misma lista,
/// un test ve el orden de los locks. Guarda además el número nuevo que recibió cada lock.
/// </summary>
internal sealed class RecordingPhoneLinkParticipant(List<string> events, string name = "participant")
    : IPhoneLinkParticipant
{
    public List<PhoneNumber?> LockedPhones { get; } = [];

    /// <summary>
    /// Corre al avisar la confirmación, antes de anotarla: así un test ve qué había guardado en ese momento.
    /// </summary>
    public Func<Task>? WhenConfirmed { get; set; }

    public Task LockAsync(Guid userId, PhoneNumber? newPhone, CancellationToken cancellationToken)
    {
        events.Add($"{name}-lock:{userId}");
        LockedPhones.Add(newPhone);

        return Task.CompletedTask;
    }

    public async Task PhoneConfirmedAsync(Guid userId, PhoneNumber phone, CancellationToken cancellationToken)
    {
        if (WhenConfirmed is { } whenConfirmed)
        {
            await whenConfirmed();
        }

        events.Add($"{name}-confirmed:{userId}:{phone.Value}");
    }

    public Task PhoneReleasedAsync(Guid userId, CancellationToken cancellationToken)
    {
        events.Add($"{name}-released:{userId}");

        return Task.CompletedTask;
    }
}
