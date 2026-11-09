using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// CA1-A: a calculation is a read-only decimal question with one of three fixed operations over questions of the draft.
public sealed class FormCalculationDraftTests
{
    private static readonly IReadOnlyDictionary<string,string> Text = new Dictionary<string,string> { ["es"]="Factura" };
    private static readonly string Quantity = "0196f2a0-0000-7000-8000-000000000001";
    private static readonly string Price = "0196f2a0-0000-7000-8000-000000000002";
    private static readonly string Amount = "0196f2a0-0000-7000-8000-000000000003";

    private static FormFieldInput Number(string localId, FormCalculation? calculation = null, bool required = false, string type = "decimal") =>
        new(localId,null,null,type,new Dictionary<string,string>{ ["es"]=localId },null,required,null,null,Calculation: calculation);

    private static SaveFormDraftRequest Request(params FormFieldInput[] fields) => new(Guid.NewGuid(),Text,"FAC",new(1,"es",Text,fields));

    private static string[] Errors(SaveFormDraftRequest request) =>
        new SaveFormDraftRequestValidator().Validate(request).Errors.Select(error => error.PropertyName).ToArray();

    private static FormCalculation Multiply(params string[] operands) => new("multiply",operands.Select(Guid.Parse).ToArray());

    [Fact]
    public void A_calculation_is_saved_and_its_new_operands_are_named_by_their_field_ids()
    {
        var request = Request(Number(Quantity),Number(Price),Number(Amount,Multiply(Quantity,Price)));
        Assert.Empty(Errors(request));

        var result = FormDraftPolicy.Resolve(FormDraftPolicy.Normalize(request),null,FormRetiredIdentities.Empty);

        Assert.True(result.IsSuccess);
        var fields = result.Value.Draft.Fields;
        Assert.Equal("multiply",fields[2].Calculation!.Operation);
        Assert.Equal(new[] { fields[0].FieldId,fields[1].FieldId },fields[2].Calculation!.Operands);
        Assert.Null(fields[0].Calculation);
    }

    [Fact]
    public void A_question_without_calculation_keeps_its_bytes()
    {
        var json = FormDraftPolicy.Serialize(new FormFieldInput("q",null,null,"decimal",Text,null,false,null,null));
        Assert.DoesNotContain("calculation",json);
    }

    [Theory]
    [InlineData("multiply",1)]
    [InlineData("multiply",3)]
    [InlineData("percent",1)]
    [InlineData("sum",0)]
    [InlineData("divide",2)]
    public void The_operation_and_its_operand_count_are_fixed(string operation, int count)
    {
        var operands = new[] { Quantity,Price,"0196f2a0-0000-7000-8000-000000000009" }.Take(count).Select(Guid.Parse).ToArray();
        var request = Request(Number(Quantity),Number(Price),Number(Amount,new(operation,operands)));
        Assert.Contains("draft.fields[2].calculation",Errors(request));
    }

    [Fact]
    public void A_sum_takes_up_to_twenty_operands()
    {
        var request = Request(Number(Amount,new("sum",Enumerable.Range(1,21).Select(index => Guid.Parse($"0196f2a0-0000-7000-8000-{index:D12}")).ToArray())));
        Assert.Contains("draft.fields[0].calculation",Errors(request));
    }

    [Fact]
    public void Only_an_optional_decimal_is_calculated()
    {
        Assert.Contains("draft.fields[2].calculation",Errors(Request(Number(Quantity),Number(Price),Number(Amount,Multiply(Quantity,Price),type:"shortText"))));
        Assert.Contains("draft.fields[2].calculation",Errors(Request(Number(Quantity),Number(Price),Number(Amount,Multiply(Quantity,Price),required:true))));
    }

    [Fact]
    public void An_operand_is_another_question_named_once()
    {
        Assert.Contains("draft.fields[1].calculation",Errors(Request(Number(Quantity),Number(Amount,Multiply(Quantity,Amount)))));
        Assert.Contains("draft.fields[1].calculation",Errors(Request(Number(Quantity),Number(Amount,Multiply(Quantity,Quantity)))));
        Assert.Contains("draft.fields[1].calculation",Errors(Request(Number(Quantity),Number(Amount,new("multiply",[Guid.Parse(Quantity),Guid.Empty])))));
    }
}
