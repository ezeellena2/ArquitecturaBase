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
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "RecordId", "FieldId", "Position" }));
        Assert.DoesNotContain(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "RecordId", "FieldId" }));
    }

    [Fact]
    public void Catalog_references_are_unique_per_selected_item_instead_of_per_question()
    {
        var entity = Entity<BusinessFormRecordReference>();
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "RecordId", "FieldId", "ItemId" }));
        Assert.DoesNotContain(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "RecordId", "FieldId" }));
        Assert.Contains(entity.GetCheckConstraints(), value => value.Name == "CK_BusinessFormRecordReferences_Value");
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
