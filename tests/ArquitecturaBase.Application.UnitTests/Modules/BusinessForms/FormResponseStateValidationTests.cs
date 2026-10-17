using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormResponseStateValidationTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("Received", true)]
    [InlineData("PendingApproval", true)]
    [InlineData("Approved", true)]
    [InlineData("Rejected", true)]
    [InlineData("", false)]
    [InlineData("approved", false)]
    [InlineData("All", false)]
    [InlineData("Approved,Rejected", false)]
    public async Task State_is_optional_but_otherwise_one_of_the_four_exact_record_states(string? state, bool valid)
    {
        var request = JsonSerializer.Deserialize<ListFormRecordsRequest>(JsonSerializer.Serialize(new { state }), JsonSerializerOptions.Web)!;
        var result = await new ListFormRecordsRequestValidator().ValidateAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(valid, result.IsValid);
        if (valid)
            Assert.Equal(state, JsonSerializer.SerializeToElement(request, JsonSerializerOptions.Web).GetProperty("state").GetString());
        else
            Assert.Contains(result.Errors, error => error.PropertyName == "State");
    }
}
