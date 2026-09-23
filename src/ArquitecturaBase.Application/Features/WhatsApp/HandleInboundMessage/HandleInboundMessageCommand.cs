using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.WhatsApp.HandleInboundMessage;

/// <summary>
/// Le contesta a un contacto lo que tenga pendiente (sección 8 del spec del ingreso con WhatsApp). Lo manda el
/// procesador de los mensajes entrantes, uno por contacto: todos sus pendientes reciben una sola respuesta, porque Meta
/// no deja mandarle a la misma persona más de un mensaje cada 6 segundos, y quedan procesados en la misma transacción
/// que la decisión. Nunca abre una sesión (sección 5): lo máximo que produce es un enlace de un solo uso, mandado al
/// mismo chat.
/// </summary>
public sealed record HandleInboundMessageCommand(Guid ContactId) : ICommand;
