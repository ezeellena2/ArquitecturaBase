using System.ComponentModel.DataAnnotations;
using ArquitecturaBase.Application.Modules.BusinessForms.Configuration;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormsOptionsTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(25, true)]
    [InlineData(26, false)]
    public void Installation_file_limit_stays_between_one_and_twenty_five_mib(int limit, bool valid)
    {
        var options = new BusinessFormsOptions { MaxFileSizeMiB = limit };
        Assert.Equal(valid, Validator.TryValidateObject(options, new ValidationContext(options), [], true));
    }
}
