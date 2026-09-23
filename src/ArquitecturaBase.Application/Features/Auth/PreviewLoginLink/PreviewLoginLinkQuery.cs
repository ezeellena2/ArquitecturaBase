using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Auth.PreviewLoginLink;

/// <summary>
/// A qué cuenta lleva el enlace del chat, para que la persona lo confirme antes de entrar (sección 11 del spec del
/// ingreso con WhatsApp). No lo consume: la vista previa de WhatsApp o un antivirus que lo abra no lo gastan.
/// </summary>
public sealed record PreviewLoginLinkQuery(string? Token) : IQuery<LoginLinkPreviewResponse>
{
    /// <summary>Un record imprime sus propiedades, y el token sirve para entrar: no tiene que terminar en un log.</summary>
    public override string ToString() => nameof(PreviewLoginLinkQuery);
}
