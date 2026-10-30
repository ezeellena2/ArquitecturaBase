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

    public static async Task<SubmitFormEntryRequest> MapAsync(SubmitFormEntryHttpRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<FormEntryAnswer>? answers;
        try
        {
            answers = JsonSerializer.Deserialize<FormEntryAnswerHttpItem[]>(request.Answers, AnswerOptions)?
                .Select(answer => new FormEntryAnswer(answer.FieldId, answer.Value, answer.ChoiceIds)).ToArray();
        }
        catch (JsonException)
        {
            answers = null;
        }
        if (request.Files.Count != request.FileFieldIds.Count) return new(request.RequestId, request.VersionId, answers, null);
        var files = new List<FormEntryFileInput>(request.Files.Count);
        for (var index = 0; index < request.Files.Count; index++)
        {
            var file = request.Files[index];
            await using var content = new MemoryStream();
            await file.CopyToAsync(content, cancellationToken);
            files.Add(new(request.FileFieldIds[index], Path.GetFileName(file.FileName), file.ContentType, content.ToArray()));
        }
        return new(request.RequestId, request.VersionId, answers, files);
    }
}
