namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>
/// Stored as its number: a new kind goes at the end. <see cref="LocationRequest"/> goes before the Flow of its send (W1-A);
/// <see cref="List"/> is a text with a button that opens up to ten options (task 9 of the documents).
/// </summary>
public enum WhatsAppOutgoingMessageKind { Text, Flow, Document, QuickReplies, LocationRequest, List }
