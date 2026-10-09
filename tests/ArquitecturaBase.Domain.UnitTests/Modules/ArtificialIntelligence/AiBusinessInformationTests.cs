using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Domain.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiBusinessInformationTests
{
    [Fact]
    public void Saving_normalizes_facts_and_advances_revision_without_touching_connection()
    {
        var information = AiBusinessInformation.Create();
        var result = information.Update(" Shop ", " Repairs ", "", "", "", "", "");
        Assert.True(result.IsSuccess);
        Assert.Equal("Shop", information.Name);
        Assert.Equal("Repairs", information.Description);
        Assert.Equal(1, information.Revision);
        Assert.Equal(1, information.Scope);
    }

    [Theory]
    [InlineData("", "Repairs")]
    [InlineData("Shop", " ")]
    public void Missing_name_or_facts_does_not_mutate_the_saved_information(string name, string description)
    {
        var information = AiBusinessInformation.Create();
        information.Update("Shop", "Repairs", "", "", "", "", "");
        var result = information.Update(name, description, "", "", "", "", "");
        Assert.True(result.IsFailure);
        Assert.Equal("Shop", information.Name);
        Assert.Equal("Repairs", information.Description);
        Assert.Equal(1, information.Revision);
    }

    [Theory]
    [InlineData(0, 120)]
    [InlineData(1, 1000)]
    [InlineData(2, 500)]
    [InlineData(3, 500)]
    [InlineData(4, 2000)]
    [InlineData(5, 2000)]
    [InlineData(6, 4000)]
    public void Each_fact_has_a_bounded_length_and_rejection_preserves_revision(int field, int limit)
    {
        string[] fields = ["Shop", "Repairs", "", "", "", "", ""];
        fields[field] = new string('x', limit);
        var information = AiBusinessInformation.Create();
        Assert.True(information.Update(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], fields[6]).IsSuccess);
        fields[field] += "x";
        Assert.True(information.Update(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], fields[6]).IsFailure);
        Assert.Equal(1, information.Revision);
        Assert.DoesNotContain("Shop", information.ToString(), StringComparison.Ordinal);
    }
}
