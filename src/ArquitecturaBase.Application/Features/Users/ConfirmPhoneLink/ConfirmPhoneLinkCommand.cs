using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.ConfirmPhoneLink;

/// <summary>
/// El código que llegó por WhatsApp para vincular <see cref="Phone"/> a la propia cuenta. <see cref="Phone"/> va en
/// formato internacional: es el que devolvió el pedido del código, no lo que tipeó la persona. Los cambios se guardan
/// aunque falle, porque los intentos fallidos se descuentan del código.
/// </summary>
public sealed record ConfirmPhoneLinkCommand(string? Phone, string? Code) : ICommand, IPersistChangesOnFailure;
