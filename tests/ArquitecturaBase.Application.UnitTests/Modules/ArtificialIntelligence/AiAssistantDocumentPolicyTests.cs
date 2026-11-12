using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// ReadDocument: the descriptor of the private copy and the reading are validated whole, never repaired or cut by the engine.
public sealed class AiAssistantDocumentPolicyTests
{
    private static readonly DateTime Expiry = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
    private static AssistantDocumentInput Document() => new("whatsapp/documents/fictional.pdf", "Comprobante Galicia.pdf", "application/pdf",
        2048, new string('a', 64), 2, Expiry);
    private static StructuredDocumentReading Reading() => new(StructuredDocumentReadability.Readable, null, "es", "Transferencia | $ 1.000", false);

    [Theory]
    [InlineData("application/pdf", 10)]
    [InlineData("image/jpeg", 1)]
    [InlineData("image/png", 1)]
    public void A_pdf_or_a_photo_within_the_limits_is_a_valid_descriptor(string type, int pages)
    {
        Assert.Null(AiAssistantDocumentPolicy.Validate(Document() with { ContentType = type, Pages = pages }));
        Assert.Null(AiAssistantDocumentPolicy.Validate(Document() with { Size = StructuredAttachmentLimits.MaximumBytes }));
    }

    [Theory]
    [InlineData("key")]
    [InlineData("keyControl")]
    [InlineData("name")]
    [InlineData("folder")]
    [InlineData("word")]
    [InlineData("typeParameters")]
    [InlineData("empty")]
    [InlineData("size")]
    [InlineData("pages")]
    [InlineData("noPages")]
    [InlineData("hash")]
    [InlineData("upperHash")]
    [InlineData("expiry")]
    public void An_invalid_descriptor_is_rejected(string change)
    {
        var document = Document();
        document = change switch
        {
            "key" => document with { StorageKey = " " },
            "keyControl" => document with { StorageKey = "whatsapp/documents/\nfictional.pdf" },
            "name" => document with { Name = "" },
            "folder" => document with { Name = "../comprobante.pdf" },
            "word" => document with { ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
            "typeParameters" => document with { ContentType = "application/pdf; charset=binary" },
            "empty" => document with { Size = 0 },
            "size" => document with { Size = StructuredAttachmentLimits.MaximumBytes + 1 },
            "pages" => document with { Pages = StructuredAttachmentLimits.MaximumPages + 1 },
            "noPages" => document with { Pages = 0 },
            "hash" => document with { Sha256 = "bad" },
            "upperHash" => document with { Sha256 = new string('A', 64) },
            _ => document with { ExpiresAtUtc = DateTime.SpecifyKind(Expiry, DateTimeKind.Unspecified) },
        };
        Assert.Equal(AiAssistantErrors.Invalid, AiAssistantDocumentPolicy.Validate(document));
    }

    [Fact]
    public void The_sealed_descriptor_reads_back_only_when_it_is_still_valid()
    {
        var document = Document();
        Assert.Equal(document, AiAssistantDocumentPolicy.ReadDescriptor(AiAssistantDocumentPolicy.Serialize(document)));
        Assert.Null(AiAssistantDocumentPolicy.ReadDescriptor(AiAssistantDocumentPolicy.Serialize(document with { Pages = 11 })));
        Assert.Null(AiAssistantDocumentPolicy.ReadDescriptor("not-json"));
        Assert.Null(AiAssistantDocumentPolicy.ReadDescriptor(null));
    }

    [Theory]
    [InlineData("application/pdf", 2048, 2, true)]
    [InlineData("image/png", 2048, 2, false)]
    [InlineData("application/pdf", 4097, 2, false)]
    [InlineData("application/pdf", 2048, 6, false)]
    public void A_profile_reads_a_document_only_with_its_type_bytes_and_pages(string type, long size, int pages, bool fits)
    {
        var profile = new AiAssistantDocumentModelProfile(AiProvider.OpenAI, "vision-model", "fixture-document", 1, ["application/pdf", "image/jpeg"], 4096, 5);
        Assert.Equal(fits, AiAssistantDocumentPolicy.Fits(Document() with { ContentType = type, Size = size, Pages = pages }, profile));
    }

    [Theory]
    [InlineData(StructuredDocumentReadability.Readable, null, "es", "Monto: $ 1.000")]
    [InlineData(StructuredDocumentReadability.Partial, StructuredDocumentProblem.Cut, "spa", "Monto: [ilegible]")]
    [InlineData(StructuredDocumentReadability.Unreadable, StructuredDocumentProblem.Blurry, null, "")]
    [InlineData(StructuredDocumentReadability.Unreadable, StructuredDocumentProblem.Dark, null, "[illegible]")]
    public void A_complete_reading_is_valid(StructuredDocumentReadability readability, StructuredDocumentProblem? problem, string? language, string text)
    {
        Assert.True(AiAssistantDocumentPolicy.Valid(new(readability, problem, language, text, false), AiAssistantLimits.ReadingCharactersCeiling));
    }

    [Theory]
    [InlineData("readability")]
    [InlineData("problem")]
    [InlineData("languageUpper")]
    [InlineData("languageLong")]
    [InlineData("languageDigits")]
    [InlineData("tooLong")]
    [InlineData("nul")]
    [InlineData("halfEmoji")]
    [InlineData("emptyReadable")]
    [InlineData("emptyPartial")]
    public void An_incomplete_or_unstorable_reading_is_invalid_and_never_repaired(string change)
    {
        var reading = Reading();
        reading = change switch
        {
            "readability" => reading with { Readability = (StructuredDocumentReadability)7 },
            "problem" => reading with { Problem = (StructuredDocumentProblem)9 },
            "languageUpper" => reading with { Language = "ES" },
            "languageLong" => reading with { Language = "spanish" },
            "languageDigits" => reading with { Language = "e1" },
            "tooLong" => reading with { Text = new string('a', 101) },
            "nul" => reading with { Text = "Monto\0" },
            "halfEmoji" => reading with { Text = "Monto \ud83d" },
            "emptyReadable" => reading with { Text = "  " },
            _ => reading with { Readability = StructuredDocumentReadability.Partial, Text = "" },
        };
        Assert.False(AiAssistantDocumentPolicy.Valid(reading, 100));
    }

    [Fact]
    public void A_completed_reading_is_stored_closed_and_read_back_whole()
    {
        var reading = new StructuredDocumentReading(StructuredDocumentReadability.Partial, StructuredDocumentProblem.Covered, "es", "| Monto | $ 1.000 |", true);
        var json = AiAssistantDocumentPolicy.SerializeReading(reading);
        using (var document = JsonDocument.Parse(json))
        {
            Assert.Equal(["kind", "language", "problem", "readability", "text", "truncated"],
                document.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            Assert.Equal("readDocument", document.RootElement.GetProperty("kind").GetString());
            Assert.Equal("partial", document.RootElement.GetProperty("readability").GetString());
            Assert.Equal("covered", document.RootElement.GetProperty("problem").GetString());
        }
        Assert.Equal(reading, AiAssistantDocumentPolicy.ReadStored(json));
        var readable = AiAssistantDocumentPolicy.SerializeReading(Reading());
        Assert.Contains("\"problem\":null", readable, StringComparison.Ordinal);
        Assert.Equal(Reading(), AiAssistantDocumentPolicy.ReadStored(readable));
    }

    [Theory]
    [InlineData("{\"kind\":\"normalizedAudio\",\"readability\":\"readable\",\"problem\":null,\"language\":\"es\",\"text\":\"Monto\",\"truncated\":false}")]
    [InlineData("{\"kind\":\"readDocument\",\"readability\":\"readable\",\"problem\":null,\"language\":\"es\",\"text\":\"Monto\"}")]
    [InlineData("{\"kind\":\"readDocument\",\"readability\":\"readable\",\"problem\":null,\"language\":\"es\",\"text\":\"Monto\",\"truncated\":false,\"extra\":1}")]
    [InlineData("{\"kind\":\"readDocument\",\"readability\":0,\"problem\":null,\"language\":\"es\",\"text\":\"Monto\",\"truncated\":false}")]
    [InlineData("{\"kind\":\"readDocument\",\"readability\":\"legible\",\"problem\":null,\"language\":\"es\",\"text\":\"Monto\",\"truncated\":false}")]
    [InlineData("{\"kind\":\"readDocument\",\"readability\":\"readable\",\"problem\":null,\"language\":\"es\",\"text\":\"\",\"truncated\":false}")]
    [InlineData("[]")]
    public void Anything_else_stored_is_not_a_reading(string json)
    {
        Assert.Null(AiAssistantDocumentPolicy.ReadStored(json));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("es-AR", "es")]
    [InlineData("EN", "en")]
    [InlineData("pt_BR", "pt")]
    [InlineData("spa", "spa")]
    public void A_blank_or_region_coded_language_becomes_its_lowercase_primary_subtag_before_validating(string? language, string? expected)
    {
        var normalized = AiAssistantDocumentPolicy.Normalize(Reading() with { Language = language });

        Assert.Equal(expected, normalized.Language);
        Assert.Equal(Reading() with { Language = expected }, normalized);
        Assert.True(AiAssistantDocumentPolicy.Valid(normalized, AiAssistantLimits.ReadingCharactersCeiling));
    }

    [Theory]
    [InlineData("spanish")]
    [InlineData("e1-AR")]
    public void A_language_that_is_not_a_code_stays_invalid_after_the_mapping(string language) =>
        Assert.False(AiAssistantDocumentPolicy.Valid(AiAssistantDocumentPolicy.Normalize(Reading() with { Language = language }),
            AiAssistantLimits.ReadingCharactersCeiling));

    [Fact]
    public void A_stored_reading_is_bound_by_the_one_reading_ceiling()
    {
        var longest = Reading() with { Text = new string('a', AiAssistantLimits.ReadingCharactersCeiling) };

        Assert.Equal(longest, AiAssistantDocumentPolicy.ReadStored(AiAssistantDocumentPolicy.SerializeReading(longest)));
        Assert.Null(AiAssistantDocumentPolicy.ReadStored(AiAssistantDocumentPolicy.SerializeReading(longest with { Text = longest.Text + "a" })));
    }

    [Fact]
    public void Diagnostics_redact_the_descriptor_and_the_reading()
    {
        Assert.DoesNotContain("Comprobante", Document().ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("whatsapp/documents", Document().ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Transferencia", Reading().ToString(), StringComparison.Ordinal);
    }
}
