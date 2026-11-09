using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Publishing a calculation (CA1-A): its operands are earlier number questions within its reach. Multiply and percent use the same
// item when the calculation is in the block, and the questions outside it otherwise; a sum goes outside the block.
public sealed class FormCalculationPublicationTests
{
    private static readonly IReadOnlyDictionary<string,string> Text = new Dictionary<string,string> { ["es"]="Factura" };

    private static CanonicalFormField Field(string type, bool? repeated = null, FormCalculation? calculation = null) =>
        new(Guid.CreateVersion7(),"field_x",type,new Dictionary<string,string>{ ["es"]="Dato" },null,false,null,
            type == "choice" ? [] : null,Rows: type == "group" ? new(1,20) : null,Repeated: repeated,Calculation: calculation);

    private static FormDefinitionResponse Definition(params CanonicalFormField[] fields) =>
        new(Guid.NewGuid(),Text,"FAC",false,1,new FormDraftResponse(1,"es",Text,fields));

    private static CanonicalFormField Calc(string operation, bool? repeated, params CanonicalFormField[] operands) =>
        Field("decimal",repeated,new(operation,operands.Select(operand => operand.FieldId).ToArray()));

    [Fact]
    public void An_invoice_with_the_amount_of_each_item_and_its_total_has_no_issue()
    {
        var quantity = Field("decimal",true); var price = Field("decimal",true);
        var amount = Calc(FormCalculation.Multiply,true,quantity,price);
        var tax = Field("decimal");
        var total = Calc(FormCalculation.Sum,null,amount,tax);
        var vat = Calc(FormCalculation.Percent,null,total,tax);

        Assert.Empty(FormPublicationPolicy.ValidateCalculations(Definition(tax,Field("group"),quantity,price,amount,total,vat)));
    }

    [Fact]
    public void An_operand_that_goes_after_or_does_not_exist_blocks_at_the_calculation()
    {
        var price = Field("decimal");
        var later = Field("decimal");
        var total = Field("decimal",null,new(FormCalculation.Multiply,[price.FieldId,later.FieldId]));

        var issue = Assert.Single(FormPublicationPolicy.ValidateCalculations(Definition(price,total,later)));

        Assert.Equal(FormPublicationErrors.CalculationInvalid.Code,issue.Code);
        Assert.Equal(total.FieldId,issue.FieldId);
        Assert.Equal("draft.fields[1].calculation",issue.Path);
        Assert.False(string.IsNullOrWhiteSpace(issue.Message));
        Assert.Single(FormPublicationPolicy.ValidateCalculations(Definition(price,Field("decimal",null,new(FormCalculation.Sum,[Guid.CreateVersion7()])))));
    }

    [Fact]
    public void Only_number_questions_are_operands()
    {
        var text = Field("shortText");
        Assert.Single(FormPublicationPolicy.ValidateCalculations(Definition(text,Calc(FormCalculation.Sum,null,text))));
    }

    [Fact]
    public void Multiply_and_percent_keep_to_one_item_or_to_the_outside()
    {
        var outside = Field("decimal"); var quantity = Field("decimal",true);
        Assert.Single(FormPublicationPolicy.ValidateCalculations(Definition(outside,Field("group"),quantity,Calc(FormCalculation.Multiply,true,quantity,outside))));
        Assert.Single(FormPublicationPolicy.ValidateCalculations(Definition(outside,Field("group"),quantity,Calc(FormCalculation.Percent,null,outside,quantity))));
    }

    [Fact]
    public void The_rate_of_a_percentage_may_be_a_choice_from_a_catalog()
    {
        var amount = Field("decimal",true);
        var rate = new CanonicalFormField(Guid.CreateVersion7(),"field_rate","choice",new Dictionary<string,string>{ ["es"]="Alícuota" },null,true,null,null,
            new FormQuestionSource(Guid.CreateVersion7()),Repeated: true);
        Assert.Empty(FormPublicationPolicy.ValidateCalculations(Definition(Field("group"),amount,rate,Calc(FormCalculation.Percent,true,amount,rate))));
        Assert.Single(FormPublicationPolicy.ValidateCalculations(Definition(Field("group"),amount,rate,Calc(FormCalculation.Multiply,true,amount,rate))));
    }

    [Fact]
    public void A_sum_goes_outside_the_block()
    {
        var quantity = Field("decimal",true);
        Assert.Single(FormPublicationPolicy.ValidateCalculations(Definition(Field("group"),quantity,Calc(FormCalculation.Sum,true,quantity))));
    }
}
