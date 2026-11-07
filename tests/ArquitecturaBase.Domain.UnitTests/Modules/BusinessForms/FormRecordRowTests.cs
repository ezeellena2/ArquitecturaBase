using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class FormRecordRowTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Fingerprint = new('a', 64);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(99)]
    public void A_record_value_keeps_the_item_it_was_answered_in(int row)
    {
        var value = BusinessFormRecordValue.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "decimal", decimalValue: 200m, row: row);

        Assert.Equal(row, value.Row);
        Assert.Equal(0, value.Position);
        Assert.Equal(200m, value.DecimalValue);
    }

    [Fact]
    public void A_record_value_outside_a_block_is_in_row_zero() =>
        Assert.Equal(0, BusinessFormRecordValue.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "shortText", textValue: "Norte").Row);

    [Fact]
    public void A_multiple_choice_in_an_item_keeps_both_its_row_and_its_position()
    {
        var value = BusinessFormRecordValue.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "multiChoice",
            choiceId: Guid.NewGuid(), position: 3, row: 4);

        Assert.Equal(4, value.Row);
        Assert.Equal(3, value.Position);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void A_record_value_cannot_leave_the_items_of_a_block(int row) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => BusinessFormRecordValue.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "decimal", decimalValue: 1m, row: row));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void A_reference_keeps_the_item_its_option_was_chosen_in(int row)
    {
        var reference = BusinessFormRecordReference.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Guid.NewGuid(), "Opción", null, "Catálogo", row: row);

        Assert.Equal(row, reference.Row);
    }

    [Fact]
    public void A_reference_outside_a_block_is_in_row_zero() =>
        Assert.Equal(0, BusinessFormRecordReference.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Guid.NewGuid(), "Opción", null, "Catálogo").Row);

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void A_reference_cannot_leave_the_items_of_a_block(int row) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => BusinessFormRecordReference.Create(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), "Opción", null, "Catálogo", row: row));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void A_rejected_receipt_keeps_the_item_of_the_rejected_answer(int row)
    {
        var fieldId = Guid.NewGuid();

        var receipt = BusinessFormCaptureReceipt.Reject(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Fingerprint,
            "Forms.Record.CatalogItemInactive", Now, fieldId, row);

        Assert.Equal(fieldId, receipt.RejectedFieldId);
        Assert.Equal(row, receipt.RejectedRow);
    }

    [Fact]
    public void A_rejection_outside_a_block_has_no_item()
    {
        var receipt = BusinessFormCaptureReceipt.Reject(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Fingerprint,
            "Forms.Record.CatalogItemInactive", Now, Guid.NewGuid());

        Assert.Null(receipt.RejectedRow);
    }

    [Fact]
    public void A_rejected_item_needs_the_rejected_question() =>
        Assert.Throws<ArgumentException>(() => BusinessFormCaptureReceipt.Reject(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Fingerprint, "Forms.Record.CatalogItemInactive", Now, rejectedFieldId: null, rejectedRow: 1));

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void A_rejected_item_cannot_leave_the_items_of_a_block(int row) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => BusinessFormCaptureReceipt.Reject(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Fingerprint, "Forms.Record.CatalogItemInactive", Now, Guid.NewGuid(), row));
}
