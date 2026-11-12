using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// DOC1: formularios brings «Comprobante de transferencia» to the library as data: fifteen questions, every one bound, the account
// and the CUIT of each side with their fixed validation, the banks and wallets as an own catalog built from the BCRA table of
// entities, how a copy reads a receipt and the standards of the next stages as «Próximamente».
public sealed class FormTransferReceiptStandardTests
{
    private static readonly IStructuredFormStandardSource Source = new BusinessFormStandardSource();
    private static StructuredFormStandard Standard => Assert.Single(Source.Standards);
    private static FormDraftInput Draft => FormDraftPolicy.Read<FormDraftInput>(Standard.DraftJson);

    [Fact]
    public void The_standard_has_its_fifteen_questions_bound_with_the_validations_of_each_side()
    {
        var standard = Standard;
        Assert.Equal(("forms.transfer-receipt", 1, "REC"), (standard.Key, standard.Version, standard.Prefix));
        Assert.Equal(("Comprobante de transferencia", "Transfer receipt"), (standard.Name["es"], standard.Name["en"]));
        Assert.NotNull(standard.Folder);
        var draft = Draft;
        Assert.Equal("es", draft.Culture);
        Assert.Equal(["choice", "shortText", "shortText", "shortText", "choice", "shortText", "shortText", "shortText", "decimal", "choice", "date",
            "shortText", "shortText", "longText", "document"], draft.Fields.Select(field => field.Type));
        Assert.Equal(draft.Fields.Select(field => field.LocalId).Order(StringComparer.Ordinal), standard.Bindings.Keys.Order(StringComparer.Ordinal));
        Assert.Equal([FormQuestionValidation.BankAccount, FormQuestionValidation.TaxId, FormQuestionValidation.BankAccount, FormQuestionValidation.TaxId],
            draft.Fields.Where(field => field.Validation is not null).Select(field => field.Validation!));
        Assert.Equal([1, 3, 5, 7], draft.Fields.Select((field, index) => (field, index)).Where(pair => pair.field.Validation is not null).Select(pair => pair.index));
        // The bank or wallet of each side is a question of the catalog the standard brings, and the currency has its own two options.
        Assert.Equal(new[] { draft.Fields[0].LocalId, draft.Fields[4].LocalId }.Order(StringComparer.Ordinal), standard.Catalogs!.Keys.Order(StringComparer.Ordinal));
        Assert.All(standard.Catalogs!.Values, key => Assert.Equal("forms.bank-entities", key));
        Assert.Equal(["Pesos", "Dólares"], draft.Fields[9].Options!.Select(option => option.Label["es"]));
        // The receipt is the document itself: one file, required.
        Assert.Equal((1, 1, true, (string?)null), (draft.Fields[14].Files!.MinFiles, draft.Fields[14].Files!.MaxFiles, draft.Fields[14].Required,
            draft.Fields[14].Files!.PhotoSource));
        Assert.Equal(["amount", "currency", "date", "receipt"], draft.Fields.Where(field => field.Required).Select(field => standard.Bindings[field.LocalId]));
        // The time as the receipt writes it («14:35», «2:35:07 p. m.»).
        Assert.Equal(("time", 16), (standard.Bindings[draft.Fields[11].LocalId], draft.Fields[11].MaxLength));
        // A copy is also completed by WhatsApp, whose Flow takes a label of up to 20 characters in a text, a number or a list, and a
        // list without help.
        Assert.All(draft.Fields.Where(field => field.Type is "shortText" or "longText" or "decimal" or "choice"),
            field => Assert.All(field.Label.Values, label => Assert.InRange(label.Length, 1, 20)));
        Assert.All(draft.Fields.Where(field => field.Type == "choice"), field => Assert.Null(field.Help));
    }

    [Fact]
    public void The_copy_reads_receipts_with_its_recognition_summary_duplicates_and_currency()
    {
        var reading = Standard.Reading!;
        var data = Standard.Bindings.Values.ToHashSet(StringComparer.Ordinal);
        Assert.All(new[] { "es", "en" }, culture =>
        {
            Assert.InRange(reading.Recognition[culture].Length, 1, FormDocumentReading.MaxRecognitionLength);
            Assert.Contains("{amount}", reading.Summary[culture], StringComparison.Ordinal);
            Assert.Contains("{sourceBank}", reading.Summary[culture], StringComparison.Ordinal);
        });
        Assert.Equal(["operationNumber", "amount", "date"], reading.DuplicateKeys);
        Assert.Equal(new Dictionary<string, string> { ["amount"] = "currency" }, reading.Currencies);
        Assert.Subset(data, reading.DuplicateKeys.Concat(reading.Currencies.Keys).Concat(reading.Currencies.Values).ToHashSet(StringComparer.Ordinal));

        // «Agregar» copies it into the draft on, in the copy's language and without lookups, and the draft that results is valid.
        var copied = FormStandardPolicy.Reading(Standard, Draft.Culture);
        Assert.Equal(reading.Recognition["es"], copied!.Recognition);
        Assert.True(copied.Enabled);
        Assert.Empty(copied.Lookups);
        // The catalog of a bound question is set by «Agregar», by its Key, before saving.
        var draft = Draft with { DocumentReading = copied };
        draft = draft with { Fields = draft.Fields.Select(field => Standard.Catalogs!.ContainsKey(field.LocalId)
            ? field with { Source = new FormQuestionSource(Guid.CreateVersion7()) } : field).ToArray() };
        Assert.Empty(new SaveFormDraftRequestValidator().Validate(new SaveFormDraftRequest(Guid.NewGuid(), Standard.Name, Standard.Prefix, draft)).Errors);
    }

    [Fact]
    public void The_banks_and_wallets_catalog_comes_from_the_official_table_with_the_entity_code_as_code()
    {
        var catalog = Assert.Single(Standard.OwnCatalogs!);
        Assert.Equal(("forms.bank-entities", "es", "Bancos y billeteras"), (catalog.Key, catalog.OriginalCulture, catalog.Name["es"]));
        var table = FormStandardResources.BankEntities;
        Assert.Equal(table.Banks.Count + table.PaymentProviders.Count, catalog.Items!.Count);
        Assert.All(catalog.Items!, item =>
        {
            Assert.Matches("^([0-9]{3}|[0-9]{4})$", item.Code);
            Assert.InRange(item.Text["es"].Length, 1, 30);
            Assert.False(string.IsNullOrWhiteSpace(item.Detail!["es"]));
        });
        Assert.Equal(catalog.Items!.Count, catalog.Items!.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(catalog.Items!, item => item.Code == "285" && item.Detail!["es"] == "BANCO MACRO S.A.");
        Assert.Contains(catalog.Items!, item => item.Code == "011" && item.Detail!["es"] == "BANCO DE LA NACION ARGENTINA");
        Assert.Equal(new DateOnly(2026, 11, 11), table.Date);
        Assert.Contains("https://api.bcra.gob.ar/cheques/v1.0/entidades", table.Sources);
    }

    [Fact]
    public void The_library_says_what_it_reads_and_its_validations_in_the_menu_language()
    {
        // Each datum by the key of the copy's bound map, in the standard's order, so the editor pairs it with its question.
        var reads = FormStandardPolicy.Reads(Standard, "es")!;
        Assert.Equal(14, reads.Count);
        Assert.Equal([new FormLibraryReadResponse("sourceBank", "Banco de origen"), new("sourceAccount", "Cuenta de origen")], reads.Take(2));
        Assert.Equal(Draft.Fields.Where(field => field.Type != "document").Select(field => Standard.Bindings[field.LocalId]), reads.Select(read => read.Key));
        Assert.DoesNotContain(reads, read => read.Key == "receipt");
        Assert.Equal(new FormLibraryReadResponse("sourceBank", "Sender bank"), FormStandardPolicy.Reads(Standard, "en")![0]);
        Assert.Equal([new FormLibraryValidationResponse("Cuenta de origen", "bankAccount"), new("CUIT/CUIL de origen", "taxId"),
            new("Cuenta de destino", "bankAccount"), new("CUIT/CUIL de destino", "taxId")], FormStandardPolicy.Validations(Standard, "es"));

        // A standard that does not read documents and has no fixed validations keeps its bytes.
        var plain = Standard with { Reading = null, DraftJson = """{"format":1,"culture":"es","submitLabel":{"es":"Enviar"},"fields":[{"localId":"a","type":"shortText","label":{"es":"Nombre"},"required":true,"maxLength":80}]}""",
            Bindings = new Dictionary<string, string> { ["a"] = "name" } };
        Assert.Null(FormStandardPolicy.Reads(plain, "es"));
        Assert.Null(FormStandardPolicy.Validations(plain, "es"));
        Assert.Null(FormStandardPolicy.Reading(plain, "es"));
    }

    [Fact]
    public void The_standards_of_the_next_stages_are_listed_without_a_draft()
    {
        var upcoming = Source.Upcoming;
        Assert.Equal(11, upcoming.Count);
        Assert.Equal(upcoming.Count, upcoming.Select(standard => standard.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(upcoming, standard =>
        {
            Assert.StartsWith("forms.", standard.Key, StringComparison.Ordinal);
            Assert.InRange(standard.Key.Length, 1, 64);
            Assert.All(new[] { "es", "en" }, culture =>
            {
                Assert.False(string.IsNullOrWhiteSpace(standard.Name[culture]));
                Assert.False(string.IsNullOrWhiteSpace(standard.Description[culture]));
            });
        });
        Assert.Equal("Nota de crédito o débito", upcoming[0].Name["es"]);
        Assert.DoesNotContain(upcoming, standard => standard.Key == Standard.Key);
    }

    // The receipt is a document question: without private storage its copy could never be published, so the library does not offer it.
    [Fact]
    public void Without_private_storage_the_library_offers_no_standard_with_a_photo_or_document_question()
    {
        var names = new Dictionary<string, string> { ["es"] = "Simple", ["en"] = "Plain" };
        var plain = new StructuredFormStandard("test.plain", 1, names, names, "PLN",
            """{"format":1,"culture":"es","submitLabel":{"es":"Enviar"},"fields":[{"localId":"a","type":"shortText","label":{"es":"Nombre"},"required":true,"maxLength":80}]}""",
            new Dictionary<string, string> { ["a"] = "name" });
        IStructuredFormStandardSource[] sources = [Source, new PlainSource(plain)];

        var withoutStorage = new FormLibraryOfferPolicy(sources, new MemoryPrivateFileStore(false));
        var withStorage = new FormLibraryOfferPolicy(sources, new MemoryPrivateFileStore(true));

        Assert.Equal(["test.plain"], withoutStorage.Offered().Select(standard => standard.Key));
        Assert.Equal(["forms.transfer-receipt", "test.plain"], withStorage.Offered().Select(standard => standard.Key));
        Assert.Equal(11, withoutStorage.Upcoming.Count);
        Assert.Throws<InvalidOperationException>(() => new FormLibraryOfferPolicy([Source, Source], new MemoryPrivateFileStore(true)));
    }

    private sealed class PlainSource(StructuredFormStandard standard) : IStructuredFormStandardSource
    {
        public IReadOnlyList<StructuredFormStandard> Standards { get; } = [standard];
    }
}
