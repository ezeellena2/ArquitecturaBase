using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormConditionChoiceCapacityTests
{
    [Theory]
    [InlineData(250)]
    [InlineData(501)]
    [InlineData(10000)]
    public async Task Conditions_read_every_manual_item_and_reuse_the_catalog_for_two_questions(int count)
    {
        var fixture = new FormRuleFixture();
        var catalogId = Guid.NewGuid();
        var labels = new Dictionary<string, string> { ["es"] = "Local", ["en"] = "Site" };
        var first = new CanonicalFormField(Guid.NewGuid(), "first", "choice", labels,
            null, false, null, null, new(catalogId));
        var second = first with { FieldId = Guid.NewGuid(), Key = "second" };
        fixture.Catalogs[catalogId] = new(catalogId, "own-catalog", "{}", "{}", "Manual",
            1, true, default, null, null, null, count);
        var items = Enumerable.Range(1, count).Select(index =>
        {
            var number = index.ToString(CultureInfo.InvariantCulture);
            return new FormCatalogItemResponse(Guid.NewGuid(), "CODE-" + number,
                new Dictionary<string, string?> { ["es"] = "Opción " + number, ["en"] = "Option " + number },
                new Dictionary<string, string?>(), index, index % 2 == 0);
        }).ToArray();
        fixture.ManualItems[catalogId] = items;
        var policy = new FormConditionChoicePolicy(fixture, fixture);
        var actor = fixture.User.UserId!.Value;
        var ct = TestContext.Current.CancellationToken;

        var oneQuestion = await policy.ReadAsync(actor, [first], "en", ct);
        var readsForOneQuestion = fixture.ManualAllItemReads;
        var twoQuestions = await policy.ReadAsync(actor, [first, second], "en", ct);

        Assert.Equal(count, Assert.Single(oneQuestion).Value.Count);
        Assert.Equal(2, twoQuestions.Count);
        Assert.Equal(items.Select(item => item.Id), twoQuestions[first.FieldId].Select(item => item.Id));
        Assert.Equal(items.Select(item => item.Text["en"]), twoQuestions[first.FieldId].Select(item => item.Label));
        Assert.Same(twoQuestions[first.FieldId], twoQuestions[second.FieldId]);
        Assert.Equal(1, readsForOneQuestion);
        Assert.Equal(readsForOneQuestion, fixture.ManualAllItemReads - readsForOneQuestion);
        Assert.Equal(0, fixture.ManualPageReads);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }
}
