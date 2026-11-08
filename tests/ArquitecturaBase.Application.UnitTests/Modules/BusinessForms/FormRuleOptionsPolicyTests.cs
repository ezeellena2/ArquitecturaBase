using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormRuleOptionsPolicyTests
{
    [Fact]
    public async Task Rule_management_gets_all_manual_choices_and_localized_filter_metadata_without_catalog_or_record_permissions()
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es");
        try
        {
            var fixture = new FormRuleFixture();
            var manualId = Guid.NewGuid();
            var filtersId = Guid.NewGuid();
            var manualField = new CanonicalFormField(Guid.NewGuid(),"manual","choice",new Dictionary<string,string> { ["es"] = "Local", ["en"] = "Site" },null,false,null,null,new(manualId));
            var filtersField = manualField with { FieldId = Guid.NewGuid(), Key = "users", Source = new(filtersId) };
            var label = new Dictionary<string,string?> { ["es"] = "Local completo" };
            fixture.Catalogs[manualId] = new(manualId,"manual","{}","{}","es","Manual",1,true,default,null,null,null,250);
            fixture.ManualItems[manualId] = Enumerable.Range(0,250).Select(index => new FormCatalogItemResponse(Guid.NewGuid(),index.ToString(CultureInfo.InvariantCulture),label,new Dictionary<string,string?>(),index+1,index%2==0,null,new Dictionary<string,string>(),new Dictionary<string,IReadOnlyDictionary<string,string>>())).ToArray();
            fixture.Catalogs[filtersId] = new(filtersId,"filters","{}","{}","es","Filters",1,true,default,null,null,null,null,
                FormDraftPolicy.Serialize(new FormCatalogFilterDefinition("active",null,[],false,"name",false)));
            var activeId = Guid.NewGuid();
            fixture.FilterPreview = new([new(activeId,"true",null)],1,false);
            Assert.True(fixture.Definition.SetDraft(fixture.Definition.Revision,fixture.Definition.Name,fixture.Definition.NameJson,
                fixture.Definition.Prefix,fixture.Definition.Culture,fixture.Definition.SubmitLabelJson,
                FormDraftPolicy.Serialize(new FormQuestionSchema(1,[manualField,filtersField])),2,false).IsSuccess);
            var result = await fixture.Service.GetOptionsAsync(fixture.Definition.Id,TestContext.Current.CancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Equal(250,result.Value.ConditionChoices[manualField.FieldId].Count);
            Assert.All(result.Value.ConditionChoices[manualField.FieldId],choice=>Assert.Equal("Local completo",choice.Label));
            Assert.Equal(1,fixture.ManualAllItemReads);
            Assert.Equal(0,fixture.ManualPageReads);
            Assert.Equal(activeId,Assert.Single(result.Value.ConditionChoices[filtersField.FieldId]).Id);
            Assert.Equal("Activo",Assert.Single(result.Value.ConditionChoices[filtersField.FieldId]).Label);
            Assert.Equal(fixture.User.UserId,fixture.PreviewActor);
            Assert.Equal(0,fixture.UnitOfWork.Transactions);
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }
    [Fact]
    public async Task The_options_bring_the_block_with_its_rows_and_its_questions_behind_it_with_the_choices_of_their_catalog()
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es");
        try
        {
            var fixture = new FormRuleFixture();
            var catalogId = Guid.NewGuid();
            static Dictionary<string,string> Label(string text) => new() { ["es"] = text, ["en"] = text };
            static FormCatalogItemResponse Item(string code, string label, int position) => new(Guid.NewGuid(), code, new Dictionary<string,string?> { ["es"] = label },
                new Dictionary<string,string?>(), position, true, null, new Dictionary<string,string>(), new Dictionary<string,IReadOnlyDictionary<string,string>>());
            var remito = new CanonicalFormField(Guid.NewGuid(), "remito", "shortText", Label("Remito"), null, true, 80, null);
            var block = new CanonicalFormField(Guid.NewGuid(), "items", "group", Label("Artículos"), null, false, null, null, Rows: new(1, 10));
            var article = new CanonicalFormField(Guid.NewGuid(), "article", "choice", Label("Artículo"), null, true, null, null, new(catalogId), Repeated: true);
            var quantity = new CanonicalFormField(Guid.NewGuid(), "quantity", "decimal", Label("Cantidad"), null, true, null, null, Repeated: true);
            fixture.Catalogs[catalogId] = new(catalogId, "manual", "{}", "{}", "es", "Manual", 1, true, default, null, null, null, 2);
            fixture.ManualItems[catalogId] = [Item("SEM", "Semilla", 1), Item("FER", "Fertilizante", 2)];
            Assert.True(fixture.Definition.SetDraft(fixture.Definition.Revision, fixture.Definition.Name, fixture.Definition.NameJson,
                fixture.Definition.Prefix, fixture.Definition.Culture, fixture.Definition.SubmitLabelJson,
                FormDraftPolicy.Serialize(new FormQuestionSchema(1, [remito, block, article, quantity])), 3, false).IsSuccess);

            var result = await fixture.Service.GetOptionsAsync(fixture.Definition.Id, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal([remito.FieldId, block.FieldId, article.FieldId, quantity.FieldId], result.Value.Questions.Select(question => question.FieldId));
            Assert.Equal(new FormRowSettings(1, 10), result.Value.Questions[1].Rows);
            Assert.All(result.Value.Questions.Skip(2), question => Assert.True(question.Repeated));
            Assert.Equal(["Semilla", "Fertilizante"], result.Value.ConditionChoices[article.FieldId].Select(choice => choice.Label));
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }
}
