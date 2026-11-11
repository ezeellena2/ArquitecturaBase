using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

// UB1-A: a location is a question of its own; changing any type to it, or from it, is another question.
public sealed class FormLocationIdentityTests
{
    [Fact]
    public void A_location_question_has_its_own_identity()
    {
        var result = FormQuestionIdentity.Create(FormQuestionType.Location);

        Assert.True(result.IsSuccess);
        Assert.Equal(FormQuestionType.Location, result.Value.Type);
        Assert.NotEqual(Guid.Empty, result.Value.FieldId);
    }

    [Theory]
    [InlineData(FormQuestionType.ShortText)]
    [InlineData(FormQuestionType.Photo)]
    [InlineData(FormQuestionType.Number)]
    public void Changing_a_question_to_a_location_creates_another_identity(FormQuestionType type)
    {
        var previous = FormQuestionIdentity.Create(type).Value;

        var location = previous.ChangeType(FormQuestionType.Location);

        Assert.True(location.IsSuccess);
        Assert.NotEqual(previous.FieldId, location.Value.FieldId);
        Assert.Same(location.Value, location.Value.ChangeType(FormQuestionType.Location).Value);
        Assert.NotEqual(location.Value.FieldId, location.Value.ChangeType(FormQuestionType.ShortText).Value.FieldId);
    }

    // Latitude, longitude and accuracy are three decimal values of one question, at positions 0, 1 and 2.
    [Theory]
    [InlineData(0, "-34.603722")]
    [InlineData(1, "-58.381592")]
    [InlineData(2, "15")]
    public void A_location_keeps_its_three_decimals_by_position(int position, string number)
    {
        var value = BusinessFormRecordValue.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "location",
            decimalValue: decimal.Parse(number, System.Globalization.CultureInfo.InvariantCulture), position: position);

        Assert.Equal(position, value.Position);
        Assert.Equal(decimal.Parse(number, System.Globalization.CultureInfo.InvariantCulture), value.DecimalValue);
        Assert.Equal(0, value.Row);
    }
}
