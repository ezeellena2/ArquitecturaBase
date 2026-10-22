using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Text.Json;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>Pure codec for the compiler profile. No capture, persistence or identity inference.</summary>
internal static partial class WhatsAppFlowValueCodec
{
    public const string DecimalPattern = "^-?(0|[1-9][0-9]*)([.,][0-9]+)?$";
    public const int DecimalMaxChars = 60;
    private static readonly BigInteger MaxCoefficient = BigInteger.Parse("79228162514264337593543950335", CultureInfo.InvariantCulture);

    public static Result<WhatsAppFlowValueResponse> Decode(WhatsAppFlowFieldMapping mapping, string? value)
    {
        if (mapping.Type == "multiChoice") return DecodeMultiple(mapping, value);
        if (string.IsNullOrEmpty(value)) return mapping.Required ? FlowErrors.InvalidValue : new WhatsAppFlowValueResponse(false);
        return mapping.Type switch
        {
            "boolean" => value switch
            {
                "true" => new WhatsAppFlowValueResponse(true, Boolean: true),
                "false" => new WhatsAppFlowValueResponse(true, Boolean: false),
                _ => FlowErrors.InvalidValue,
            },
            "decimal" => DecodeDecimal(value),
            "date" => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? new WhatsAppFlowValueResponse(true, Date: date) : FlowErrors.InvalidValue,
            "choice" => Guid.TryParseExact(value, "D", out var choice) && mapping.Choices.Contains(choice)
                ? new WhatsAppFlowValueResponse(true, ChoiceId: choice) : FlowErrors.InvalidValue,
            "shortText" or "longText" => mapping.MaxLength is { } max && value.Length <= max && StorableText.IsStorable(value.AsSpan())
                ? new WhatsAppFlowValueResponse(true, Text: value) : FlowErrors.InvalidValue,
            _ => FlowErrors.InvalidValue,
        };
    }

    private static Result<WhatsAppFlowValueResponse> DecodeMultiple(WhatsAppFlowFieldMapping mapping, string? value)
    {
        if (mapping.Selection is not { MinSelected: >= 0 and <= 20, MaxSelected: >= 1 and <= 20 } range
            || range.MinSelected > range.MaxSelected || mapping.Required != (range.MinSelected > 0)) return FlowErrors.InvalidValue;
        if (value is null) return mapping.Required ? FlowErrors.InvalidValue : new WhatsAppFlowValueResponse(false);
        try
        {
            using var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 2 });
            var array = document.RootElement;
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() < range.MinSelected
                || array.GetArrayLength() > range.MaxSelected) return FlowErrors.InvalidValue;
            var selected = new List<Guid>();
            var seen = new HashSet<Guid>();
            foreach (var option in array.EnumerateArray())
            {
                if (option.ValueKind != JsonValueKind.String || !Guid.TryParseExact(option.GetString(), "D", out var id)
                    || id == Guid.Empty || !mapping.Choices.Contains(id) || !seen.Add(id)) return FlowErrors.InvalidValue;
                selected.Add(id);
            }
            return new WhatsAppFlowValueResponse(true, ChoiceIds: selected);
        }
        catch (JsonException) { return FlowErrors.InvalidValue; }
    }

    private static Result<WhatsAppFlowValueResponse> DecodeDecimal(string value)
    {
        if (value.Length > DecimalMaxChars || DecimalGrammar().Match(value) is not { Success: true } match || match.Length != value.Length)
            return FlowErrors.InvalidValue;
        var negative = value[0] == '-';
        var unsigned = negative ? value[1..] : value;
        var parts = unsigned.Replace(',', '.').Split('.');
        var scale = parts.Length == 2 ? parts[1].Length : 0;
        var coefficient = BigInteger.Parse(string.Concat(parts), CultureInfo.InvariantCulture);
        while (scale > 0 && coefficient % 10 == 0) { coefficient /= 10; scale--; }
        // decimal.TryParse can round excessive precision. Construct the exact 96-bit coefficient instead.
        if (scale > 28 || coefficient > MaxCoefficient) return FlowErrors.InvalidValue;
        var number = new decimal(unchecked((int)(uint)(coefficient & uint.MaxValue)), unchecked((int)(uint)((coefficient >> 32) & uint.MaxValue)),
            unchecked((int)(uint)((coefficient >> 64) & uint.MaxValue)), negative, (byte)scale);
        return new WhatsAppFlowValueResponse(true, Decimal: number == 0 ? "0" : number.ToString("0.############################", CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(DecimalPattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex DecimalGrammar();
}
