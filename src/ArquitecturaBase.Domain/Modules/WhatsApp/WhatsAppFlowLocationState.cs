namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>
/// W1-A: whether a send asks for the location before its Flow and how that ended: the location arrived, or the person went on
/// without it when the question is optional. Stored as text.
/// </summary>
public enum WhatsAppFlowLocationState { None, Requested, Received, Skipped }
