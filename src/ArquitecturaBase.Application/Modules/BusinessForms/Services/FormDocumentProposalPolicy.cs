using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The validation of a document's proposal without AI (decision 8), on the current active version of each candidate: every read question
/// with its value and mark (read with doubts or not read), the fixed validations of the bound questions (what fails stays, marked), the
/// form's lookups in catalogs, the bank or wallet from the account by its entity code when the account passes (the standard's
/// <c>banks</c>), and the repetition of the same file or of the standard's duplicate keys in that form. A candidate whose form changed
/// version or stopped being active is left out; «sure» without its candidate is «unknown», and «doubt» with one left is «sure».
/// </summary>
internal sealed class FormDocumentProposalPolicy(IFormReceivedDocumentReader reader, IFormCatalogItemCodeReader codes,
    IEnumerable<IStructuredFormStandardSource> sources)
{
    public async Task<FormDocumentPreparedProposal> PrepareAsync(FormDocumentEntryProposal proposal, string sha256, Guid? exceptDocumentId,
        CancellationToken ct)
    {
        var candidates = new List<FormDocumentPreparedCandidate>();
        FormDocumentRepetition? repetition = null;
        foreach (var candidate in proposal.Candidates)
        {
            var row = await reader.FindActiveFormAsync(candidate.DefinitionId, ct);
            if (row is null || row.VersionId != candidate.VersionId) continue;
            var schema = FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson);
            var standard = Standard(row);
            var prepared = await CandidateAsync(row, schema, standard, candidate, ct);
            candidates.Add(prepared);
            repetition ??= await RepetitionAsync(row, standard, prepared, sha256, exceptDocumentId, ct);
        }
        var outcome = candidates.Count switch
        {
            0 => FormDocumentEntryProposal.Unknown,
            1 => FormDocumentEntryProposal.Sure,
            _ => FormDocumentEntryProposal.Doubt,
        };
        return new FormDocumentPreparedProposal(outcome, proposal.DocumentType, candidates) { Repetition = repetition };
    }

    /// <summary>A row of a spreadsheet as a candidate of its form's active version (VC1-A), validated and completed like a document's.</summary>
    public Task<FormDocumentPreparedCandidate> PrepareCandidateAsync(FormDocumentFormRow row, FormVersionSchema schema, FormDocumentEntryCandidate candidate,
        CancellationToken ct) => CandidateAsync(row, schema, Standard(row), candidate, ct);

    /// <summary>
    /// A response with the same data as a row (VC1-A): only by every duplicate key of the standard, never by the file, which a row shares
    /// with the others of its spreadsheet and a capture of a row never attaches.
    /// </summary>
    public Task<FormDocumentRepetition?> FindRepeatedDataAsync(FormDocumentFormRow row, FormDocumentPreparedCandidate candidate, CancellationToken ct) =>
        RepetitionAsync(row, Standard(row), candidate, null, null, ct);

    private async Task<FormDocumentPreparedCandidate> CandidateAsync(FormDocumentFormRow row, FormVersionSchema schema, StructuredFormStandard? standard,
        FormDocumentEntryCandidate candidate, CancellationToken ct)
    {
        var questions = FormDocumentFormPolicy.ReadQuestions(schema);
        var given = candidate.Values.ToDictionary(value => value.FieldId);
        var values = new Dictionary<Guid, FormDocumentPreparedValue>();
        // The standard's text duplicate keys (the operation number of a transfer): the one free text the inbox is searched by.
        var operations = standard?.Reading?.DuplicateKeys is { } duplicateKeys && Bindings(row) is { } bound
            ? duplicateKeys.Select(key => bound.TryGetValue(key, out var fieldId) ? fieldId : Guid.Empty).Where(fieldId => fieldId != Guid.Empty).ToHashSet()
            : [];
        foreach (var question in questions)
        {
            given.TryGetValue(question.FieldId, out var read);
            var value = read?.Value is { } text && FormScalarPrefillPolicy.Normalize(Project(question), text, []) is { IsSuccess: true } canonical
                ? canonical.Value : null;
            var mark = value is null ? FormDocumentMarks.NotRead : read!.Doubtful ? FormDocumentMarks.Doubtful : null;
            string? error = null;
            if (value is not null && question.Validation is { } validation && FormRecordAnswerPolicy.ValidationFailure(validation, value) is { } failure)
            {
                mark = FormDocumentMarks.Invalid;
                error = failure.Code;
            }
            values[question.FieldId] = new(question.FieldId, question.Type, value, mark)
            {
                ErrorCode = error,
                Text = value is not null && question.Type == "choice" && question.Source?.Catalog is null
                    ? question.Options?.SingleOrDefault(option => option.ChoiceId.ToString("D") == value)?.Label : null,
                Searchable = value is not null && error is null && (question.Type == "decimal" || question.Validation is not null
                    || question.Type is "shortText" or "longText" && operations.Contains(question.FieldId)),
            };
        }
        await FillBanksAsync(row, schema, standard, values, ct);
        await FillLookupsAsync(row, schema, values, ct);
        return new(row.DefinitionId, row.VersionId, row.DefinitionRevision, questions.Select(question => values[question.FieldId]).ToArray());
    }

    // The bank or wallet a standard pairs with an account: its catalog option by the account's entity code, only when the account passes.
    private async Task FillBanksAsync(FormDocumentFormRow row, FormVersionSchema schema, StructuredFormStandard? standard,
        Dictionary<Guid, FormDocumentPreparedValue> values, CancellationToken ct)
    {
        if (standard?.Reading?.Banks is not { Count: > 0 } banks || Bindings(row) is not { } bound) return;
        foreach (var (accountKey, bankKey) in banks)
        {
            if (!bound.TryGetValue(accountKey, out var accountId) || !bound.TryGetValue(bankKey, out var bankId)
                || !values.TryGetValue(accountId, out var account) || !values.TryGetValue(bankId, out var bank) || bank.Value is not null
                || account is not { Value: { } text, Mark: not FormDocumentMarks.Invalid } || BankAccountKey.Parse(text)?.EntityCode is not { } code
                || schema.Questions.SingleOrDefault(question => question.FieldId == bankId)?.Source?.Catalog is not { } catalogId) continue;
            var found = await codes.ListActiveAsync(catalogId, [code], ct);
            if (found.TryGetValue(code, out var itemId)) values[bankId] = await CompletedAsync(bank, itemId, ct);
        }
    }

    // DOC1: each lookup fills an empty catalog question with the option whose code, text or field matches what another question read.
    private async Task FillLookupsAsync(FormDocumentFormRow row, FormVersionSchema schema, Dictionary<Guid, FormDocumentPreparedValue> values, CancellationToken ct)
    {
        if (FormDocumentFormPolicy.Reading(row) is not { Enabled: true, Lookups.Count: > 0 } reading) return;
        foreach (var lookup in reading.Lookups)
        {
            if (!values.TryGetValue(lookup.QuestionId, out var target) || target.Value is not null
                || !values.TryGetValue(lookup.SourceQuestionId, out var source) || source is not { Value: { } read, Mark: not FormDocumentMarks.Invalid }
                || schema.Questions.SingleOrDefault(question => question.FieldId == lookup.QuestionId)?.Source?.Catalog is not { } catalogId) continue;
            var validation = schema.Questions.SingleOrDefault(question => question.FieldId == lookup.SourceQuestionId)?.Validation;
            var value = validation switch
            {
                FormQuestionValidation.TaxId => CuitNumber.Normalize(read) ?? read.Trim(),
                FormQuestionValidation.BankAccount => BankAccountKey.Parse(read)?.Value ?? read.Trim(),
                _ => read.Trim(),
            };
            Guid? itemId = lookup.Field == FormDocumentLookup.Code
                ? (await codes.ListActiveAsync(catalogId, [value], ct)).TryGetValue(value, out var coded) ? coded : null
                : (await reader.FindCatalogItemAsync(catalogId, lookup.Field, value, ct))?.ItemId;
            if (itemId is { } found) values[lookup.QuestionId] = await CompletedAsync(target, found, ct);
        }
    }

    private async Task<FormDocumentPreparedValue> CompletedAsync(FormDocumentPreparedValue value, Guid itemId, CancellationToken ct)
    {
        var item = (await reader.ListCatalogItemsAsync([itemId], ct)).SingleOrDefault();
        var texts = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(item?.TextEs)) texts["es"] = item.TextEs;
        if (!string.IsNullOrWhiteSpace(item?.TextEn)) texts["en"] = item.TextEn;
        return value with { Value = itemId.ToString("D"), Mark = FormDocumentMarks.Completed, Text = texts.Count == 0 ? null : texts };
    }

    // The same file, or the same values of every duplicate key of the standard (all of them read and passing), in the form; without a file
    // (a row of a spreadsheet), only the values, and nothing to ask without them.
    private async Task<FormDocumentRepetition?> RepetitionAsync(FormDocumentFormRow row, StructuredFormStandard? standard, FormDocumentPreparedCandidate candidate,
        string? sha256, Guid? exceptDocumentId, CancellationToken ct)
    {
        var keys = new List<FormDocumentRepetitionKey>();
        if (standard?.Reading is { DuplicateKeys.Count: > 0 } reading && Bindings(row) is { } bound)
        {
            foreach (var datum in reading.DuplicateKeys)
            {
                var value = bound.TryGetValue(datum, out var fieldId) ? candidate.Values.SingleOrDefault(item => item.FieldId == fieldId) : null;
                if (value is not { Value: not null, Mark: not FormDocumentMarks.Invalid } || value.Type is not ("shortText" or "longText" or "decimal" or "date"))
                {
                    keys.Clear();
                    break;
                }
                keys.Add(new(value.FieldId, value.Type, value.Value!));
            }
        }
        if (sha256 is null && keys.Count == 0) return null;
        var found = (await reader.ListRepetitionsAsync(row.DefinitionId, sha256, keys, exceptDocumentId, ct)).MinBy(item => item.AtUtc);
        return found is null ? null : new(found.AtUtc, found.RecordId,
            found.Number is { } number ? row.Prefix + "-" + number.ToString("D6", CultureInfo.InvariantCulture) : null, found.DocumentId);
    }

    public StructuredFormStandard? Standard(FormDocumentFormRow row) => row.StandardKey is not { } key ? null
        : sources.SelectMany(source => source.Standards).FirstOrDefault(standard => standard.Key == key && standard.Version == row.StandardVersion);

    public static IReadOnlyDictionary<string, Guid>? Bindings(FormDocumentFormRow row) =>
        row.StandardBindingsJson is { } json ? FormDraftPolicy.Read<Dictionary<string, Guid>>(json) : null;

    private static StructuredFormQuestion Project(CanonicalFormField field) => new(field.FieldId, field.Key, field.Type, field.Label, field.Help,
        field.Required, field.MaxLength, field.Options?.Select(option => new StructuredFormChoice(option.ChoiceId, option.Label)).ToArray(), null);
}
