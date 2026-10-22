using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class FormRecordReferenceValueTests
{
    [Theory]
    [InlineData("Integer", "1500")]
    [InlineData("Decimal", "1500.5")]
    [InlineData("Date", "2026-10-22")]
    public void A_reference_copies_the_canonical_value_and_type_as_immutable_capture_facts(string type, string value)
    {
        var reference = BusinessFormRecordReference.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3,
            Guid.NewGuid(), "Opción", "Detalle", "Catálogo", value: value, valueType: type);

        Assert.Equal(value, reference.Value);
        Assert.Equal(type, reference.ValueType);
        Assert.Equal(3, reference.CatalogRevision);
        Assert.Equal("Opción", reference.Text);
        Assert.Equal("Detalle", reference.Detail);
    }

    [Fact]
    public void A_legacy_reference_has_no_inferred_typed_value()
    {
        var reference = BusinessFormRecordReference.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Guid.NewGuid(), "1500", null, "Catálogo");

        Assert.Null(reference.Value);
        Assert.Null(reference.ValueType);
    }
}
