using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

/// <summary>Maps a multipart send by hand. Unreadable answers or files without their question go as null, for the validator to reject.</summary>
internal static class FormEntryRequestMapping
{
    private static readonly JsonSerializerOptions AnswerOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static async Task<SubmitFormEntryRequest> MapAsync(SubmitFormEntryHttpRequest request, CancellationToken cancellationToken) =>
        new(request.RequestId, request.VersionId, Answers(request.Answers),
            await FilesAsync(request.Files, request.FileFieldIds, cancellationToken));

    public static async Task<CorrectFormEntryRequest> MapAsync(CorrectFormEntryHttpRequest request, CancellationToken cancellationToken) =>
        new(request.RequestId, request.VersionId, Answers(request.Answers), await FilesAsync(request.Files, request.FileFieldIds, cancellationToken),
            request.Reason, request.KeptFileIds);

    public static PreviewFormIssueRequest Map(PreviewFormIssueHttpRequest request) =>
        new(request.VersionId, request.Answers?.Select(Answer).ToArray());

    private static FormEntryAnswer[]? Answers(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<FormEntryAnswerHttpItem[]>(json, AnswerOptions)?.Select(Answer).ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // An answer and the answers of each item of the block; a null one stays null, for the validator to reject.
    private static FormEntryAnswer Answer(FormEntryAnswerHttpItem? answer) => answer is null ? null! : new(answer.FieldId, answer.Value, answer.ChoiceIds,
        answer.Rows?.Select(item => item is null ? null! : (IReadOnlyList<FormEntryAnswer>)item.Select(Answer).ToArray()).ToArray());

    private static async Task<List<FormEntryFileInput>?> FilesAsync(List<IFormFile> uploaded, List<Guid> fieldIds,
        CancellationToken cancellationToken)
    {
        if (uploaded.Count != fieldIds.Count) return null;
        var files = new List<FormEntryFileInput>(uploaded.Count);
        for (var index = 0; index < uploaded.Count; index++)
        {
            var file = uploaded[index];
            await using var content = new MemoryStream();
            await file.CopyToAsync(content, cancellationToken);
            files.Add(new(fieldIds[index], Path.GetFileName(file.FileName), file.ContentType, content.ToArray()));
        }
        return files;
    }
}
