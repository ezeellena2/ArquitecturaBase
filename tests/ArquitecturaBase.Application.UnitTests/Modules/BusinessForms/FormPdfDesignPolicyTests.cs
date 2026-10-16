using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormPdfDesignPolicyTests
{
    [Fact]
    public void Original_name_is_required_and_optional_translation_falls_back_as_a_whole()
    {
        var id = Guid.NewGuid();
        var design = new FormPdfDesign(new Dictionary<string,string> { ["es"] = " Visita " }, [id], true,
            new Dictionary<string,string> { ["es"] = " Pie ", ["en"] = " " }, true);
        var normalized = FormPdfDesignPolicy.Normalize(design, "es", [id]);
        Assert.True(normalized.IsSuccess);
        Assert.Equal("Visita", normalized.Value.Name["es"]);
        Assert.Equal("Pie", normalized.Value.Footer!["es"]);
        Assert.Equal(new[] { "es" }, FormPdfDesignPolicy.CompleteCultures(normalized.Value, "es"));
        var incomplete = normalized.Value with { Name = new Dictionary<string,string> { ["es"]="Visita", ["en"]="Visit" } };
        Assert.Equal(new[] { "es" }, FormPdfDesignPolicy.CompleteCultures(incomplete, "es"));
        var complete = incomplete with { Footer = new Dictionary<string,string> { ["es"]="Pie", ["en"]="Footer" } };
        Assert.Equal(new[] { "es", "en" }, FormPdfDesignPolicy.CompleteCultures(complete, "es"));
        Assert.True(FormPdfDesignPolicy.Normalize(design with { Name = new Dictionary<string,string> { ["en"]="Visit" } }, "es", [id]).IsFailure);
    }
    [Fact]
    public void Selected_questions_are_unique_known_and_do_not_implicitly_include_new_questions()
    {
        var first=Guid.NewGuid(); var later=Guid.NewGuid();
        var design=new FormPdfDesign(new Dictionary<string,string> { ["es"]="Visita" }, [first], false, null, false);
        Assert.Equal(new[] { first }, FormPdfDesignPolicy.Normalize(design,"es",[first,later]).Value.QuestionIds);
        Assert.True(FormPdfDesignPolicy.Normalize(design with { QuestionIds=[first,first] },"es",[first,later]).IsFailure);
        Assert.True(FormPdfDesignPolicy.Normalize(design with { QuestionIds=[Guid.NewGuid()] },"es",[first,later]).IsFailure);
        Assert.True(FormPdfDesignPolicy.Normalize(design with { QuestionIds=[] },"es",[first,later]).IsSuccess);
    }
    [Fact]
    public void An_English_original_remains_the_only_complete_copy_when_a_selected_label_is_not_translated()
    {
        var id=Guid.NewGuid();
        var design=new FormPdfDesign(new Dictionary<string,string>{["en"]="Visit",["es"]="Visita"},[id],true,null,true);
        var schema=new FormVersionSchema(1,[new(id,"question","shortText",new Dictionary<string,string>{["en"]="Question"},null,false,80,null)]);
        Assert.Equal(new[]{"en"},FormPdfDesignPolicy.CompleteCultures(design,"en",schema));
        Assert.True(FormPdfDesignPolicy.Normalize(design with {Footer=new Dictionary<string,string>{["es"]="Pie"}},"en",[id]).IsFailure);
    }

    [Theory]
    [InlineData(161, 0)]
    [InlineData(1, 1001)]
    public void Content_limits_are_validated_without_truncating(int nameLength,int footerLength)
    {
        var design=new FormPdfDesign(new Dictionary<string,string> { ["en"]=new('a',nameLength) }, [], true,
            footerLength==0?null:new Dictionary<string,string> { ["en"]=new('b',footerLength) }, true);
        Assert.True(FormPdfDesignPolicy.Normalize(design,"en",[]).IsFailure);
    }
}
