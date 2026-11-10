namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

/// <summary>
/// A Home turn with a spreadsheet (Stage 2): the turn's fields, without a spreadsheet of their own, and the browser's file with its
/// name. Its owner reads the bytes through the neutral port; the server keeps the copy and seals the descriptor in the turn.
/// </summary>
public sealed record SubmitAiAssistantFileTurnRequest(SubmitAiAssistantTurnRequest Turn, byte[] Content, string FileName)
{
    public override string ToString() => "SubmitAiAssistantFileTurnRequest { Content = [REDACTED] }";
}
