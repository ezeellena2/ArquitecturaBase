using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormRecordReferenceRowTests
{
    private static readonly Guid RecordId = Guid.Parse("11111111-1111-7111-8111-111111111111");
    private static readonly Guid FieldId = Guid.Parse("22222222-2222-7222-8222-222222222222");
    private static readonly Guid CatalogId = Guid.Parse("33333333-3333-7333-8333-333333333333");
    private static readonly Guid ItemId = Guid.Parse("44444444-4444-7444-8444-444444444444");

    [Fact]
    public void A_reference_outside_a_block_keeps_the_bytes_already_sealed_in_snapshots()
    {
        var reference = new FormRecordReferenceRow(RecordId, FieldId, CatalogId, 3, ItemId, "Norte", null, "Sucursales");

        var json = FormDraftPolicy.Serialize(reference);

        Assert.Equal("""{"recordId":"11111111-1111-7111-8111-111111111111","fieldId":"22222222-2222-7222-8222-222222222222","catalogId":"33333333-3333-7333-8333-333333333333","catalogRevision":3,"itemId":"44444444-4444-7444-8444-444444444444","text":"Norte","detail":null,"catalogName":"Sucursales","translationsJson":null}""", json);
        Assert.Equal(0, FormDraftPolicy.Read<FormRecordReferenceRow>(json).Row);
    }

    [Fact]
    public void A_reference_chosen_in_an_item_keeps_its_row_in_the_snapshot()
    {
        var reference = new FormRecordReferenceRow(RecordId, FieldId, CatalogId, 3, ItemId, "Norte", null, "Sucursales", Row: 2);

        var json = FormDraftPolicy.Serialize(reference);

        Assert.EndsWith(""","translationsJson":null,"row":2}""", json);
        Assert.Equal(reference, FormDraftPolicy.Read<FormRecordReferenceRow>(json));
    }
}
