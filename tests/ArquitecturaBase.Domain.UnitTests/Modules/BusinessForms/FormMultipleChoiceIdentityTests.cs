using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;
public sealed class FormMultipleChoiceIdentityTests
{
    [Fact]
    public void Multiple_choice_has_a_new_identity_when_replacing_a_single_choice()
    {
        var single=FormQuestionIdentity.Create(FormQuestionType.Choice).Value;
        var result=single.ChangeType(FormQuestionType.MultiChoice);
        Assert.True(result.IsSuccess);
        Assert.Equal(FormQuestionType.MultiChoice,result.Value.Type);
        Assert.NotEqual(single.FieldId,result.Value.FieldId);
        Assert.Same(result.Value,result.Value.ChangeType(FormQuestionType.MultiChoice).Value);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(19)]
    public void Record_values_keep_their_received_position(int position)
    {
        var option=Guid.NewGuid();
        var value=BusinessFormRecordValue.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"multiChoice",choiceId:option,position:position);
        Assert.Equal(position,value.Position);
        Assert.Equal(option,value.ChoiceId);
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(20)]
    public void A_record_value_cannot_exceed_the_selection_position_bounds(int position) =>
        Assert.Throws<ArgumentOutOfRangeException>(()=>BusinessFormRecordValue.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"multiChoice",choiceId:Guid.NewGuid(),position:position));
}
