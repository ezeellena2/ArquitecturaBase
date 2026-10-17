using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogProposalPolicyTests
{
    private const string RoleId = "3c8767d3-78cb-4782-9d42-462d73b898bc";
    private static FormCatalogMetadataPayload Metadata => new("users", [new("name", "text", true, true, true, ["contains", "equals"], null), new("role", "text", false, true, true, ["has"], [new(RoleId, "Supervisores")]), new("active", "boolean", true, true, true, ["equals"], [new("true", "Activo"), new("false", "Inactivo")])]);
    private static FormCatalogProposalPayload Valid => new(1, "Supervisores", "Supervisores activos", new("name", "role", [new("role", "has", RoleId), new("active", "equals", "true")], false, "name", false), []);

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void A_valid_proposal_needs_only_the_original_language_and_an_observed_role(string culture) =>
        Assert.True(FormCatalogProposalPolicy.Validate(Valid, Metadata, culture).IsSuccess);

    [Fact]
    public void An_unavailable_column_is_preserved_as_a_visible_limitation()
    {
        var proposal = Valid with { Limitations = [new("UnsupportedColumn", "zona norte")] };
        Assert.True(FormCatalogProposalPolicy.Validate(proposal, Metadata, "es").IsSuccess);
    }

    [Fact]
    public void Filters_without_a_role_still_support_name_and_active_conditions()
    {
        var proposal = Valid with { Filters = Valid.Filters with { Conditions = [new("name", "contains", "Equipo"), new("active", "equals", "false")], DetailColumn = null, SortColumn = "active" } };
        Assert.True(FormCatalogProposalPolicy.Validate(proposal, Metadata, "es").IsSuccess);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("role")]
    [InlineData("active")]
    public void Only_existing_columns_are_used_for_the_optional_detail(string column) =>
        Assert.True(FormCatalogProposalPolicy.Validate(Valid with { Filters = Valid.Filters with { DetailColumn = column } }, Metadata, "es").IsSuccess);

    [Theory]
    [InlineData(" ", "Valid")]
    [InlineData("Valid", " ")]
    [InlineData("\0", "Valid")]
    [InlineData("Valid", "\0")]
    public void Non_storable_or_empty_original_text_is_rejected(string name, string description) =>
        Assert.True(FormCatalogProposalPolicy.Validate(Valid with { Name = name, Description = description }, Metadata, "es").IsFailure);

    [Theory]
    [InlineData(121, 10)]
    [InlineData(10, 1001)]
    public void Text_budgets_are_checked_before_acceptance(int name, int description) =>
        Assert.True(FormCatalogProposalPolicy.Validate(Valid with { Name = new string('a', name), Description = new string('b', description) }, Metadata, "es").IsFailure);

    [Theory]
    [InlineData("role", "has", "77290e76-d270-42a0-a231-46cdad07e0ab")]
    [InlineData("role", "equals", RoleId)]
    [InlineData("role", "has", "Supervisores")]
    [InlineData("active", "equals", "active")]
    [InlineData("zone", "equals", "north")]
    [InlineData("name", "sql", "SELECT * FROM users")]
    public void Unknown_roles_and_unsupported_filter_grammar_are_rejected(string column, string operation, string value) =>
        Assert.True(FormCatalogProposalPolicy.Validate(Valid with { Filters = Valid.Filters with { Conditions = [new(column, operation, value)] } }, Metadata, "es").IsFailure);

    [Fact]
    public void The_metadata_source_cannot_be_changed() =>
        Assert.True(FormCatalogProposalPolicy.Validate(Valid, Metadata with { Source = "arbitrary_table" }, "es").IsFailure);

    [Fact]
    public void The_metadata_read_must_have_a_real_positive_ordinal() =>
        Assert.True(FormCatalogProposalPolicy.Validate(Valid with { MetadataReadOrdinal = 0 }, Metadata, "es").IsFailure);

    [Fact]
    public void Conditions_have_a_small_fixed_limit() =>
        Assert.True(FormCatalogProposalPolicy.Validate(Valid with { Filters = Valid.Filters with { Conditions = Enumerable.Repeat<FormCatalogFilterCondition?>(new("active", "equals", "true"), 11).ToArray() } }, Metadata, "es").IsFailure);

    [Theory]
    [InlineData("arbitrary", "zone")]
    [InlineData("UnsupportedColumn", " ")]
    public void Limitations_have_known_codes_and_nonempty_criteria(string code, string criterion) =>
        Assert.True(FormCatalogProposalPolicy.Validate(Valid with { Limitations = [new(code, criterion)] }, Metadata, "es").IsFailure);

    [Fact]
    public void Unbounded_limitations_are_rejected() =>
        Assert.True(FormCatalogProposalPolicy.Validate(Valid with { Limitations = Enumerable.Repeat(new FormCatalogProposalLimitation("UnsupportedColumn", "zone"), 6).ToArray() }, Metadata, "es").IsFailure);

    [Fact]
    public void Unsupported_original_culture_is_rejected() =>
        Assert.True(FormCatalogProposalPolicy.Validate(Valid, Metadata, "fr").IsFailure);
}
