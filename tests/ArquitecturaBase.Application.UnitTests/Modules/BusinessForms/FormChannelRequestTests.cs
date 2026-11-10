using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// What a channel needs of a calculation (CA1-A) and a condition (CO1-A): the question is computed by the owner, or shows only
// with some answers of an earlier one. Without them the request keeps its bytes.
public sealed class FormChannelRequestTests
{
    private static readonly IReadOnlyDictionary<string,string> Text = new Dictionary<string,string> { ["es"]="Factura" };

    private static CanonicalFormField Field(string type, FormCalculation? calculation = null, FormShowWhen? showWhen = null) =>
        new(Guid.CreateVersion7(),"field_x",type,new Dictionary<string,string>{ ["es"]="Dato" },null,false,null,null,
            Calculation: calculation,ShowWhen: showWhen);

    private static FormDefinitionResponse Definition(params CanonicalFormField[] fields) =>
        new(Guid.NewGuid(),Text,"FAC",false,1,new FormDraftResponse(1,"es",Text,fields));

    [Fact]
    public void A_calculation_and_a_condition_reach_the_channel_and_nothing_changes_without_them()
    {
        var service = Field("boolean");
        var price = Field("decimal",showWhen: new(service.FieldId,["true"]));
        var total = Field("decimal",new(FormCalculation.Sum,[price.FieldId]));

        var questions = FormPublicationSnapshotPolicy.ChannelRequest(Definition(service,price,total)).Questions;

        Assert.True(questions[2].Calculated);
        Assert.Equal(service.FieldId,questions[1].ShowWhen!.FieldId);
        Assert.Equal(["true"],questions[1].ShowWhen!.Values);
        Assert.Null(questions[0].Calculated);
        Assert.Null(questions[0].ShowWhen);
        var json = FormDraftPolicy.Serialize(questions[0]);
        Assert.DoesNotContain("calculated",json,StringComparison.Ordinal);
        Assert.DoesNotContain("showWhen",json,StringComparison.Ordinal);
    }
}
