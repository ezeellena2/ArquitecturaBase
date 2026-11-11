namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>Stored as its number: a new kind goes at the end. <see cref="LocationRequest"/> goes before the Flow of its send (W1-A).</summary>
public enum WhatsAppOutgoingMessageKind { Text, Flow, Document, QuickReplies, LocationRequest }
