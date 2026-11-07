using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ArquitecturaBase.ArchitectureTests.Modules.BusinessForms;

public sealed class FormMultipleChoiceValueModelTests
{
    [Fact]
    public void A_record_value_has_a_zero_default_and_a_bounded_position_without_changing_its_typed_value_check()
    {
        var entity = Entity<BusinessFormRecordValue>();
        var position = entity.FindProperty("Position");
        Assert.NotNull(position);
        Assert.Equal(typeof(int), position.ClrType);
        Assert.False(position.IsNullable);
        Assert.Equal(0, position.GetDefaultValue());
        Assert.NotNull(position.FindAnnotation(RelationalAnnotationNames.DefaultValue));
        Assert.Equal("\"Position\" >= 0 AND \"Position\" < 20", Assert.Single(entity.GetCheckConstraints(),
            value => value.Name == "CK_BusinessFormRecordValues_Position").Sql);
        Assert.Contains(entity.GetCheckConstraints(), value => value.Name == "CK_BusinessFormRecordValues_ExactlyOneValue");
        // Each item of a block has its own positions (AddFormRecordRows).
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "RecordId", "FieldId", "Row", "Position" }));
        Assert.DoesNotContain(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "RecordId", "FieldId" }));
        Assert.DoesNotContain(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "RecordId", "FieldId", "Position" }));
    }

    [Fact]
    public void Catalog_references_are_unique_per_selected_item_instead_of_per_question()
    {
        var entity = Entity<BusinessFormRecordReference>();
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "RecordId", "FieldId", "Row", "ItemId" }));
        Assert.DoesNotContain(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "RecordId", "FieldId" }));
        Assert.DoesNotContain(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "RecordId", "FieldId", "ItemId" }));
        Assert.Contains(entity.GetCheckConstraints(), value => value.Name == "CK_BusinessFormRecordReferences_Value");
    }

    [Fact]
    public void Values_and_references_keep_their_item_with_a_zero_default_bounded_by_the_items_of_a_block()
    {
        foreach (var (entity, check) in new[] { (Entity<BusinessFormRecordValue>(), "CK_BusinessFormRecordValues_Row"),
            (Entity<BusinessFormRecordReference>(), "CK_BusinessFormRecordReferences_Row") })
        {
            var row = entity.FindProperty("Row");
            Assert.NotNull(row);
            Assert.Equal(typeof(int), row.ClrType);
            Assert.False(row.IsNullable);
            Assert.Equal(0, row.GetDefaultValue());
            Assert.Equal("\"Row\" >= 0 AND \"Row\" < 100", Assert.Single(entity.GetCheckConstraints(), value => value.Name == check).Sql);
        }
    }

    [Fact]
    public void A_rejected_item_is_optional_and_requires_its_rejected_question()
    {
        var entity = Entity<BusinessFormCaptureReceipt>();
        var row = entity.FindProperty("RejectedRow");
        Assert.NotNull(row);
        Assert.Equal(typeof(int?), row.ClrType);
        Assert.True(row.IsNullable);
        Assert.Equal("\"RejectedRow\" IS NULL OR \"RejectedFieldId\" IS NOT NULL AND \"RejectedRow\" >= 0 AND \"RejectedRow\" < 100",
            Assert.Single(entity.GetCheckConstraints(), value => value.Name == "CK_BusinessFormCaptureReceipts_RejectedRow").Sql);
    }

    private static IEntityType Entity<T>()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        options.UseNpgsql();
        options.UseOpenIddict<Guid>();
        using var context = new ApplicationDbContext(options.Options);
        return context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(T))!;
    }
}
