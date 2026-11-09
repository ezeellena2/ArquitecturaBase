using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// The capture computes the calculations (CA1-A) as decimal answers, rounded to 2 decimals half-even each on its own: the total adds
// the amounts already rounded. An answer sent for a calculation is invalid, because the server is the one that computes it.
public sealed class FormCalculationCaptureTests
{
    private static CanonicalFormField Field(string key, string type = "decimal", bool? repeated = null, FormCalculation? calculation = null) =>
        new(Guid.CreateVersion7(),key,type,new Dictionary<string,string>{ ["es"]=key },null,false,null,null,
            Rows: type == "group" ? new(0,20) : null,Repeated: repeated,Calculation: calculation);

    private static CanonicalFormField Calc(string key, string operation, bool? repeated, params CanonicalFormField[] operands) =>
        Field(key,"decimal",repeated,new(operation,operands.Select(operand => operand.FieldId).ToArray()));

    private sealed record Invoice(FormVersionSchema Schema, CanonicalFormField Amount, CanonicalFormField Total, CanonicalFormField Vat);

    private static Invoice Build()
    {
        var rate = Field("rate"); var group = Field("items","group");
        var quantity = Field("quantity",repeated: true); var price = Field("price",repeated: true);
        var amount = Calc("amount",FormCalculation.Multiply,true,quantity,price);
        var total = Calc("total",FormCalculation.Sum,null,amount);
        var vat = Calc("vat",FormCalculation.Percent,null,total,rate);
        return new(new FormVersionSchema(1,[rate,group,quantity,price,amount,total,vat]),amount,total,vat);
    }

    private static IReadOnlyList<FormRecordAnswer>? Capture(FormVersionSchema schema, Dictionary<string,string> answers) =>
        FormRecordAnswerPolicy.Normalize(schema,answers,[]);

    [Fact]
    public void The_amount_of_each_item_the_total_and_the_tax_are_computed()
    {
        var invoice = Build();

        var answers = Capture(invoice.Schema,new() { ["rate"]="21",
            ["items"]="""[{"quantity":"12","price":"850"},{"quantity":"1","price":"5100.005"}]""" })!;

        var rows = answers.Single(answer => answer.Type == "group").Rows!;
        Assert.Equal(new decimal?[] { 10200.00m,5100.00m },rows.Select(row => row.Single(answer => answer.FieldId == invoice.Amount.FieldId).Decimal));
        Assert.Equal(15300.00m,answers.Single(answer => answer.FieldId == invoice.Total.FieldId).Decimal);
        Assert.Equal(3213.00m,answers.Single(answer => answer.FieldId == invoice.Vat.FieldId).Decimal);
    }

    [Theory]
    [InlineData("0.125","0.12")]
    [InlineData("0.135","0.14")]
    [InlineData("-0.125","-0.12")]
    public void Each_calculation_rounds_to_two_decimals_half_even(string price, string amount)
    {
        var invoice = Build();
        var answers = Capture(invoice.Schema,new() { ["items"]=$$"""[{"quantity":"1","price":"{{price}}"}]""" })!;
        Assert.Equal(decimal.Parse(amount,System.Globalization.CultureInfo.InvariantCulture),
            answers.Single(answer => answer.Type == "group").Rows![0].Single(answer => answer.FieldId == invoice.Amount.FieldId).Decimal);
    }

    [Fact]
    public void An_empty_operand_leaves_a_product_empty_and_a_sum_adds_what_there_is()
    {
        var invoice = Build();

        var answers = Capture(invoice.Schema,new() { ["items"]="""[{"quantity":"2"},{"quantity":"1","price":"10"}]""" })!;

        var rows = answers.Single(answer => answer.Type == "group").Rows!;
        Assert.DoesNotContain(rows[0],answer => answer.FieldId == invoice.Amount.FieldId);
        Assert.Equal(10m,answers.Single(answer => answer.FieldId == invoice.Total.FieldId).Decimal);
        Assert.DoesNotContain(answers,answer => answer.FieldId == invoice.Vat.FieldId);
    }

    [Fact]
    public void A_sum_without_values_is_left_empty()
    {
        var invoice = Build();
        var answers = Capture(invoice.Schema,new() { ["items"]="[]" })!;
        Assert.DoesNotContain(answers,answer => answer.FieldId == invoice.Total.FieldId);
    }

    [Fact]
    public void The_rate_of_a_catalog_option_is_its_value()
    {
        var group = Field("items","group");
        var amount = Field("amount",repeated: true);
        var rate = new CanonicalFormField(Guid.CreateVersion7(),"rate","choice",new Dictionary<string,string>{ ["es"]="Alícuota" },null,false,null,null,
            new FormQuestionSource(Guid.CreateVersion7()),Repeated: true);
        var vat = Calc("vat",FormCalculation.Percent,true,amount,rate);
        var general = Guid.CreateVersion7();
        var offer = new ArquitecturaBase.Application.Models.StructuredData.StructuredFormCatalogOffer(rate.FieldId,"rate","Alícuota",rate.Source!.Catalog!.Value,1,"Manual","Alícuotas",
            [new(general,"21 %",null,Value: "21"),new(Guid.CreateVersion7(),"10,5 %",null,Value: "10.5")],ValueType: "Decimal");

        var answers = FormRecordAnswerPolicy.Normalize(new FormVersionSchema(1,[group,amount,rate,vat]),
            new Dictionary<string,string> { ["items"]=$$"""[{"amount":"1000.10","rate":"{{general:D}}"}]""" },[offer])!;

        Assert.Equal(210.02m,answers.Single(answer => answer.Type == "group").Rows![0].Single(answer => answer.FieldId == vat.FieldId).Decimal);
    }

    [Fact]
    public void An_answer_sent_for_a_calculation_is_invalid()
    {
        var invoice = Build();
        Assert.Null(Capture(invoice.Schema,new() { ["total"]="1" }));
        Assert.Null(Capture(invoice.Schema,new() { ["items"]="""[{"quantity":"1","price":"2","amount":"2"}]""" }));
    }
}
