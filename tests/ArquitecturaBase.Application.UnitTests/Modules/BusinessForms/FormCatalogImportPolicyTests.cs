using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogImportPolicyTests
{
    private static readonly Guid CatalogId = Guid.Parse("01990000-0000-7000-8000-000000000070");
    private static readonly string Hash = new('a', 64);
    private static readonly string[] Header = ["Código", "Texto", "Texto en inglés", "Detalle", "Detalle en inglés", "Activa"];
    private static FormCatalogStoredItemRow Item(string code, string text, int position = 1, bool active = true, string? value = null) =>
        new(Guid.NewGuid(), CatalogId, code, text, null, null, null, position, active, value);
    private static FormCatalogImportSource Source(params FormCatalogStoredItemRow[] items) => new(CatalogId, 7, BusinessFormCatalogValueType.Text, "es", items);
    private static FormSpreadsheet Sheet(params string?[][] rows) => new("Opciones", Hash,
        rows.Select((cells,index) => new FormSpreadsheetRow(index + 1, cells.Select(text => new FormSpreadsheetCell(text)).ToArray())).ToArray());
    private static FormCatalogImportPlan Plan(FormCatalogImportSource source, FormSpreadsheet sheet, FormCatalogImportMode mode = FormCatalogImportMode.Replace)
    {
        var result = FormCatalogImportPolicy.Build(source, sheet, mode, "es");
        Assert.True(result.IsSuccess);
        return result.Value;
    }
    private static FormCatalogImportRequest Request(FormCatalogImportPlan plan) =>
        new(plan.Preview.Revision, plan.Preview.Mode, Stream.Null, "options.csv", plan.Preview.SheetSha256);

    [Fact]
    public void Appending_new_rows_preserves_the_raw_hash_and_file_order()
    {
        var plan = Plan(Source(), Sheet(Header, ["B", "Segundo"], ["A", "Primero"]), FormCatalogImportMode.Append);
        Assert.Equal("Append", plan.Preview.Mode);
        Assert.Equal(CatalogId, plan.Preview.CatalogId);
        Assert.Equal(7, plan.Preview.Revision);
        Assert.Equal(Hash, plan.Preview.SheetSha256);
        Assert.Equal(2, plan.Preview.RowsRead);
        Assert.Equal(2, plan.Preview.Counts.Added);
        Assert.Equal(new[] { "B", "A" }, plan.Items.Select(item => item.Code));
        Assert.All(plan.Items, item => Assert.Null(item.ItemId));
        Assert.All(plan.Items, item => Assert.True(item.IsActive));
        Assert.Equal(new[] { 2, 3 }, plan.Preview.Added.Select(row => row.Row));
    }

    [Fact]
    public void Replace_inactivates_absent_rows_and_reactivates_present_rows_without_replacing_ids()
    {
        var first = Item("A", "Uno"); var absent = Item("B", "Dos", 2); var returning = Item("C", "Tres", 3, false);
        var plan = Plan(Source(first, absent, returning), Sheet(Header, ["A", "Uno"], ["C", "Tres"], ["D", "Cuatro"]));
        Assert.Equal(1, plan.Preview.Counts.Added);
        Assert.Equal(1, plan.Preview.Counts.Reactivated);
        Assert.Equal(1, plan.Preview.Counts.Deactivated);
        Assert.Equal(1, plan.Preview.Counts.Unchanged);
        Assert.Equal(absent.Id, Assert.Single(plan.Preview.Deactivated).ItemId);
        Assert.DoesNotContain(plan.Items, input => input.ItemId == first.Id);
        Assert.True(Assert.Single(plan.Items, input => input.ItemId == returning.Id).IsActive);
        Assert.False(Assert.Single(plan.Items, input => input.ItemId == absent.Id).IsActive);
        Assert.Equal(3, returning.Position);
        Assert.False(returning.IsActive);
        Assert.True(absent.IsActive);
    }

    [Fact]
    public void Append_updates_matching_codes_but_keeps_every_absent_item()
    {
        var changed = Item("FIRST", "Uno"); var absent = Item("OTHER", "Otro", 2);
        var plan = Plan(Source(changed, absent), Sheet(Header, ["first", "Modificado", null, null, null, "No"]), FormCatalogImportMode.Append);
        var mutation = Assert.Single(plan.Items);
        Assert.Equal(changed.Id, mutation.ItemId);
        Assert.Equal("Modificado", mutation.Text!["es"]);
        Assert.False(mutation.IsActive);
        Assert.DoesNotContain(plan.Preview.Deactivated, row => row.ItemId == absent.Id);
        Assert.True(absent.IsActive);
    }

    [Fact]
    public void A_row_error_preserves_its_existing_item_instead_of_deactivating_it()
    {
        var bad = Item("A", "Original", 8); var absent = Item("B", "Otro", 9);
        var plan = Plan(Source(bad, absent), Sheet(Header, ["a", new string('x',31)]));
        Assert.Equal(1, plan.Preview.Counts.ErrorRows);
        var issue = Assert.Single(plan.Preview.Issues);
        Assert.Equal(2, issue.Row); Assert.Equal("text", issue.Column);
        Assert.Equal(FormCatalogImportErrors.TextTooLong.Code, issue.Code);
        Assert.Equal(31, issue.Length);
        Assert.DoesNotContain(plan.Items, item => item.ItemId == bad.Id);
        Assert.Equal(absent.Id, Assert.Single(plan.Preview.Deactivated).ItemId);
        Assert.Equal("Original", bad.TextEs); Assert.Equal(8, bad.Position); Assert.True(bad.IsActive);
    }

    [Fact]
    public void Every_ambiguous_duplicate_code_row_is_rejected_and_the_existing_item_is_preserved()
    {
        var existing = Item("A", "Original");
        var plan = Plan(Source(existing), Sheet(Header, ["a", "Primero"], ["A", "Segundo"]));
        Assert.Equal(2, plan.Preview.Counts.ErrorRows);
        Assert.Equal(0, plan.Preview.Counts.Added);
        Assert.Empty(plan.Items);
        Assert.Equal(new[] { 2, 3 }, plan.Preview.Issues.Select(issue => issue.Row));
        Assert.All(plan.Preview.Issues, issue => { Assert.Equal("code", issue.Column); Assert.Equal(FormCatalogImportErrors.DuplicateCode.Code, issue.Code); Assert.NotNull(issue.RelatedRow); });
    }

    [Fact]
    public void An_English_original_catalog_reads_Text_as_English_and_the_Spanish_text_column_as_its_translation()
    {
        var south = new FormCatalogStoredItemRow(Guid.NewGuid(), CatalogId, "S", "Sur", "South", null, null, 1, true);
        var plan = Plan(Source(south) with { OriginalCulture = "en" },
            Sheet(["Código", "Texto", "Texto en español"], ["N", "North", "Norte"], ["S", "South", "Sureño"]), FormCatalogImportMode.Append);

        Assert.Empty(plan.Preview.Issues);
        var added = Assert.Single(plan.Items, item => item.ItemId is null);
        Assert.Equal("North", added.Text!["en"]);
        Assert.Equal("Norte", added.Text["es"]);
        Assert.Equal("Norte", Assert.Single(plan.Preview.Added).Text);
        var change = Assert.Single(Assert.Single(plan.Preview.Changed).Changes);
        Assert.Equal("textEs", change.Column);
        Assert.Equal("Sur", change.Before);
        Assert.Equal("Sureño", change.After);
        Assert.Equal("South", Assert.Single(plan.Items, item => item.ItemId == south.Id).Text!["en"]);
    }

    [Theory]
    [InlineData("en", "English text", "English detail")]
    [InlineData("es", "Spanish text", "Spanish detail")]
    public void A_translation_column_in_the_original_language_is_an_unknown_column(string original, string text, string detail)
    {
        var plan = Plan(Source() with { OriginalCulture = original }, Sheet(["Code", "Text", text, detail], ["A", "Original", "Other", "More"]));

        Assert.Equal(new[] { text, detail }, plan.Preview.Issues.Select(issue => issue.Column));
        Assert.All(plan.Preview.Issues, issue => Assert.Equal(FormCatalogImportErrors.UnknownColumn.Code, issue.Code));
        var input = Assert.Single(plan.Items);
        Assert.Equal("Original", input.Text![original]);
        Assert.Null(input.Text[original == "en" ? "es" : "en"]);
        Assert.All(input.Detail!.Values, Assert.Null);
    }

    [Fact]
    public void Repeated_text_is_a_warning_while_distinct_codes_are_applied()
    {
        var plan = Plan(Source(), Sheet(Header, ["A", "Duplicado"], ["B", "duplicado"]));
        Assert.Equal(2, plan.Preview.Counts.Added);
        Assert.Equal(0, plan.Preview.Counts.ErrorRows);
        Assert.Equal(1, plan.Preview.Counts.Warnings);
        Assert.Equal("Warning", Assert.Single(plan.Preview.Issues).Severity);
        Assert.Equal(FormCatalogImportErrors.DuplicateText.Code, plan.Preview.Issues[0].Code);
    }

    [Fact]
    public void Header_aliases_ignore_case_accents_and_surrounding_spaces()
    {
        var plan = Plan(Source(), Sheet([" CÓDIGO ", "Text", "English text", "Detail", "English detail", "Active"],
            ["A", "Original", "English", "Detalle", "Detail", "false"]));
        var input = Assert.Single(plan.Items);
        Assert.Equal("English", input.Text!["en"]); Assert.Equal("Detail", input.Detail!["en"]); Assert.False(input.IsActive);
    }

    [Theory]
    [InlineData("code", "Forms.CatalogImport.MissingColumn")]
    [InlineData("text", "Forms.CatalogImport.MissingColumn")]
    [InlineData("duplicate", "Forms.CatalogImport.DuplicateColumn")]
    public void Missing_and_duplicate_logical_headers_fail_before_planning(string kind, string expected)
    {
        string[] headers = kind switch { "code" => ["Texto"], "text" => ["Code"], _ => ["Código", "Code", "Texto"] };
        var result = FormCatalogImportPolicy.Build(Source(), Sheet(headers), FormCatalogImportMode.Replace, "es");
        Assert.True(result.IsFailure); Assert.Equal(expected, result.Error.Code);
    }

    [Fact]
    public void An_unknown_column_is_reported_and_ignored()
    {
        var plan = Plan(Source(), Sheet(["Code", "Text", "Unknown"], ["A", "Uno", "unused"]));
        Assert.Equal(1, plan.Preview.Counts.Added); Assert.Equal(0, plan.Preview.Counts.ErrorRows);
        Assert.Equal(FormCatalogImportErrors.UnknownColumn.Code, Assert.Single(plan.Preview.Issues).Code);
        Assert.Equal(1, plan.Preview.Issues[0].Row);
    }

    [Theory]
    [InlineData(BusinessFormCatalogValueType.Integer, "1.500", "1500")]
    [InlineData(BusinessFormCatalogValueType.Decimal, "1.500,50", "1500.5")]
    [InlineData(BusinessFormCatalogValueType.Decimal, "1500.50", "1500.5")]
    [InlineData(BusinessFormCatalogValueType.Date, "21/10/2026", "2026-10-21")]
    public void Typed_imports_accept_display_and_canonical_values(BusinessFormCatalogValueType type, string input, string expected)
    {
        var plan = Plan(Source() with { ValueType = type }, Sheet(["Code", "Text", "Value"], ["A", "Uno", input]));
        Assert.Equal(expected, Assert.Single(plan.Items).Value);
    }

    [Fact]
    public void A_formula_without_a_cached_value_is_a_cell_issue_and_does_not_create_an_item()
    {
        var sheet = Sheet(["Code", "Text"], ["A", "unused"]);
        sheet = sheet with { Rows = [sheet.Rows[0], new(2,[new("A"),new(null,FormCatalogImportErrors.FormulaValueMissing)])] };
        var plan = Plan(Source(), sheet);
        Assert.Empty(plan.Items); Assert.Equal(1, plan.Preview.Counts.ErrorRows);
        Assert.Equal(FormCatalogImportErrors.FormulaValueMissing.Code, Assert.Single(plan.Preview.Issues).Code);
        Assert.Equal("text", plan.Preview.Issues[0].Column);
    }

    [Theory]
    [InlineData(10000, true)]
    [InlineData(10001, false)]
    public void Row_capacity_is_enforced_after_header_processing(int count, bool accepted)
    {
        var rows = new[] { new string?[] { "Code", "Text" } }.Concat(Enumerable.Range(1,count).Select(index => new string?[] { "C"+index, "Option"+index })).ToArray();
        var result = FormCatalogImportPolicy.Build(Source(),Sheet(rows),FormCatalogImportMode.Append,"es");
        Assert.Equal(accepted,result.IsSuccess);
        if (accepted) { Assert.Equal(count,result.Value.Items.Count); Assert.Equal(count,result.Value.Preview.RowsRead); Assert.Equal(100,result.Value.Preview.Added.Count); Assert.True(result.Value.Preview.ListsTruncated); }
        else Assert.Equal(FormCatalogImportErrors.TooManyRows.Code,result.Error.Code);
    }

    [Fact]
    public void Inactive_items_still_count_toward_the_resulting_catalog_capacity()
    {
        var source = Source(Enumerable.Range(1,10000).Select(index=>Item("OLD"+index,"Old",index,false)).ToArray());
        var result = FormCatalogImportPolicy.Build(source,Sheet(Header,["NEW","New"]),FormCatalogImportMode.Replace,"es");
        Assert.Equal(FormCatalogErrors.TooManyItems,result.Error);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 2)]
    public void Only_more_than_a_quarter_of_active_items_requires_a_deactivation_confirmation(int removed, int confirmation)
    {
        var source = Source(Enumerable.Range(1,4).Select(index=>Item("C"+index,"Item"+index,index)).ToArray());
        var rows = new[] { Header }.Concat(Enumerable.Range(removed+1,4-removed).Select(index=>new[]{"C"+index,"Item"+index})).ToArray();
        Assert.Equal(confirmation,Plan(source,Sheet(rows)).Preview.Confirmations.Deactivations);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void The_extra_error_checkbox_starts_above_ten_percent(int errors, bool expected)
    {
        var rows = new[] { Header }.Concat(Enumerable.Range(1,10).Select(index=>new[]{"C"+index,index<=errors?new string('x',31):"Item"+index})).ToArray();
        var plan = Plan(Source(),Sheet(rows));
        Assert.Equal(errors,plan.Preview.Counts.ErrorRows); Assert.Equal(expected,plan.Preview.Confirmations.ManyErrors);
    }

    [Fact]
    public void Preview_limits_lists_but_keeps_the_complete_error_counts()
    {
        var rows = new[] { Header }.Concat(Enumerable.Range(1,150).Select(index=>new[]{"C"+index,new string('x',31)})).ToArray();
        var plan = Plan(Source(),Sheet(rows));
        Assert.Equal(150,plan.Preview.Counts.ErrorRows); Assert.Equal(100,plan.Preview.Issues.Count); Assert.True(plan.Preview.ListsTruncated);
    }

    [Fact]
    public void The_last_import_warning_uses_only_the_matching_original_hash()
    {
        var at = new DateTime(2026,10,22,0,0,0,DateTimeKind.Utc);
        var source = Source() with { LastImportSha256 = Hash, LastImportAtUtc = at };
        Assert.Equal(at,Plan(source,Sheet(Header)).Preview.AlreadyAppliedAtUtc);
        Assert.Null(Plan(source,Sheet(Header) with { Sha256 = new('b',64) }).Preview.AlreadyAppliedAtUtc);
    }

    [Fact]
    public void Apply_rejects_another_file_and_requires_empty_confirmation()
    {
        var plan = Plan(Source(),Sheet(Header));
        var request = Request(plan);
        Assert.Equal(FormCatalogImportErrors.SheetChanged,FormCatalogImportPolicy.ValidateConfirmations(plan,request with { SheetSha256 = new('b',64) }).Error);
        Assert.Equal(FormCatalogImportErrors.ConfirmationRequired,FormCatalogImportPolicy.ValidateConfirmations(plan,request).Error);
        Assert.True(FormCatalogImportPolicy.ValidateConfirmations(plan,request with { ConfirmEmpty = true }).IsSuccess);
    }

    [Fact]
    public void Apply_requires_the_exact_deactivation_count_and_explicit_error_acceptance()
    {
        var source = Source(Item("A","Uno"),Item("B","Dos",2),Item("C","Tres",3),Item("D","Cuatro",4));
        var plan = Plan(source,Sheet(Header,["A",new string('x',31)]));
        var request = Request(plan) with { ConfirmEmpty=true, ConfirmErrors=true };
        Assert.Equal(3,plan.Preview.Confirmations.Deactivations);
        Assert.True(FormCatalogImportPolicy.ValidateConfirmations(plan,request with { ConfirmDeactivations=3 }).IsSuccess);
        Assert.Equal(FormCatalogImportErrors.ConfirmationRequired,FormCatalogImportPolicy.ValidateConfirmations(plan,request with { ConfirmDeactivations=2 }).Error);
        Assert.Equal(FormCatalogImportErrors.ConfirmationRequired,FormCatalogImportPolicy.ValidateConfirmations(plan,request with { ConfirmDeactivations=3,ConfirmErrors=false }).Error);
    }
}
