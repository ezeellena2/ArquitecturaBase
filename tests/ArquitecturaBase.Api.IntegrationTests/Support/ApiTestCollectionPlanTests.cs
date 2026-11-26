using ArquitecturaBase.Api.IntegrationTests.Users;
using static ArquitecturaBase.Api.IntegrationTests.Support.ApiTestCollectionFactory;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

public sealed class ApiTestCollectionPlanTests
{
    [Fact]
    public void Classes_go_by_cases_to_the_collection_with_fewest_and_each_collection_weighs_what_its_classes_took()
    {
        var plan = ApiCollectionPlan.For(
            [new ApiTestClass("Slow", 6, 600), new ApiTestClass("Fast", 5, 30), new ApiTestClass("Medium", 4, 120), new ApiTestClass("Small", 1, 10)],
            count: 2);

        Assert.Equal([0, 1, 1, 0], new[] { "Slow", "Fast", "Medium", "Small" }.Select(name => plan.Collections[name]));
        Assert.Equal([610d, 150d], plan.Seconds);
    }

    [Fact]
    public void A_class_without_a_measurement_weighs_its_cases_at_the_average_of_the_measured_ones()
    {
        var plan = ApiCollectionPlan.For([new ApiTestClass("Measured", 10, 50), new ApiTestClass("New", 4, null)], count: 2);

        Assert.Equal(5, plan.SecondsPerCase);
        Assert.Equal([50d, 20d], plan.Seconds);
        Assert.Equal(15, plan.SecondsOf(new ApiTestClass("Elsewhere", 3, null)));
    }

    [Fact]
    public void Without_any_measurement_a_collection_weighs_its_cases()
    {
        var plan = ApiCollectionPlan.For([new ApiTestClass("First", 3, null), new ApiTestClass("Second", 2, null)], count: 2);

        Assert.Equal([3d, 2d], plan.Seconds);
    }

    [Fact]
    public void The_last_measurement_travels_in_the_assembly_with_the_classes_by_name()
    {
        Assert.True(Measured.ContainsKey(NameOf(typeof(UsersEndpointsTests))));
    }
}
