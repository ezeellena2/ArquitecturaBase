using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Rule_conditions_and_sheet_SQL_agree_for_accents_literal_patterns_typed_values_and_historical_presence()
    {
        using var actor=await OperatorAsync([BusinessFormsPermissions.RecordsRead]);var seeded=await SeedSheetAsync(actor,3);
        var option=Guid.NewGuid();var choice=Field("choice",false) with{Options=[new(option,new Dictionary<string,string>{{"es","Opción"},{"en","Choice"}})]};
        var photo=Field("photo",false) with{Files=new(0,2,"camera")};
        await factory.ExecuteDbContextAsync(async db=>
        {
            for(var index=0;index<seeded.RecordIds.Count;index++)
            {
                var id=seeded.RecordIds[index];var text=index==1?"Revisión área X":"Revisión ÁREA %_\\ extra";
                var record=await db.Set<BusinessFormRecord>().SingleAsync(record=>record.Id==id,Ct);
                var values=JsonSerializer.Deserialize<List<JsonElement>>(record.ValuesJson,SerializerOptions)!;
                values=values.Select(value=>value.GetProperty("fieldId").GetGuid()==seeded.Fields[1].FieldId?JsonSerializer.SerializeToElement(new{fieldId=seeded.Fields[1].FieldId,value=text},SerializerOptions):value).ToList();
                db.Entry(record).Property(record=>record.ValuesJson).CurrentValue=JsonSerializer.Serialize(values,SerializerOptions);
                var typed=await db.Set<BusinessFormRecordValue>().SingleAsync(value=>value.RecordId==id&&value.FieldId==seeded.Fields[1].FieldId,Ct);
                db.Entry(typed).Property(value=>value.TextValue).CurrentValue=text;
            }
            var definition=await db.Set<BusinessFormDefinition>().SingleAsync(definition=>definition.Id==seeded.Id,Ct);
            foreach(var field in new[]{choice,photo})db.Set<BusinessFormField>().Add(BusinessFormField.Register(field.FieldId,definition.Id,field.Key,field.Type));
            await db.SaveChangesAsync(Ct);
            var fields=seeded.Fields.Concat([choice,photo]).ToArray();var version=BusinessFormVersion.Create(definition.Id,2,"test-condition-parity");
            version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1,fields),SerializerOptions));version.SealPresentation(definition.NameJson,definition.SubmitLabelJson,definition.Culture,fields.Length);
            db.Set<BusinessFormVersion>().Add(version);await db.SaveChangesAsync(Ct);Assert.True(definition.ActivateVersion(definition.ActiveVersionId,version.Id).IsSuccess);
            var author=Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());var now=new DateTime(2026,10,16,13,0,0,DateTimeKind.Utc);
            for(var index=0;index<2;index++)
            {
                var receipt=Guid.NewGuid();var values=index==0?JsonSerializer.Serialize(new[]{new{fieldId=choice.FieldId,value=option}},SerializerOptions):"[]";
                var record=BusinessFormRecord.Create(definition.Id,version.Id,definition.AllocateRecordNumber(),author,receipt,now,values);
                db.Set<BusinessFormRecord>().Add(record);db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(receipt,definition.Id,version.Id,"test",record.Id,record.Number,now));
                if(index==0)
                {
                    db.Set<BusinessFormRecordValue>().Add(BusinessFormRecordValue.Create(record.Id,definition.Id,choice.FieldId,"choice",choiceId:option));
                    var file=StoredFile.Create(author,"photo.png","image/png",10,new string('0',64),"tests/condition-parity/"+record.Id,null);
                    db.Set<StoredFile>().Add(file);db.Set<BusinessFormRecordFile>().Add(BusinessFormRecordFile.Create(definition.Id,record.Id,photo.FieldId,file.Id,0));
                }
            }
            await db.SaveChangesAsync(Ct);
        });
        await factory.ExecuteScopeAsync(async services=>
        {
            var reader=services.GetRequiredService<IFormResponseReader>();var definition=await reader.FindDefinitionAsync(seeded.Id,Ct);Assert.NotNull(definition);
            var all=await reader.ListAsync(definition,new(){PageSize=100},Ct);Assert.Equal(5,all.TotalCount);
            (IReadOnlyList<FormRecordCondition> Conditions,long[] Expected)[] cases=
            [
                ([new(seeded.Fields[1].FieldId,"contains",["área"])],[1,2,3]),
                ([new(seeded.Fields[1].FieldId,"contains",["%_\\"])],[1,3]),
                ([new(seeded.Fields[1].FieldId,"equals",["Revisión área X"])],[2]),
                ([new(seeded.Fields[0].FieldId,"equals",["0.00"])],[1]),
                ([new(seeded.Fields[0].FieldId,"gte",["1"])],[2,3]),
                ([new(seeded.Fields[0].FieldId,"lte",["1"])],[1,2]),
                ([new(seeded.Fields[2].FieldId,"in",["false"])],[2]),
                ([new(seeded.Fields[2].FieldId,"in",["true"])],[1,3]),
                ([new(seeded.Fields[3].FieldId,"equals",["2026-10-16"])],[1,2,3]),
                ([new(seeded.Fields[3].FieldId,"gte",["2026-10-15"])],[1,2,3]),
                ([new(seeded.Fields[3].FieldId,"lte",["2026-10-15"])],[]),
                ([new(choice.FieldId,"in",[option.ToString()])],[4]),
                ([new(choice.FieldId,"answered",null)],[4]),
                ([new(choice.FieldId,"notAsked",null)],[1,2,3]),
                ([new(choice.FieldId,"unanswered",null)],[1,2,3,5]),
                ([new(photo.FieldId,"answered",null)],[4]),
                ([new(photo.FieldId,"unanswered",null)],[1,2,3,5]),
                ([new(photo.FieldId,"notAsked",null)],[1,2,3]),
                ([new(seeded.Fields[1].FieldId,"contains",["área"]),new(seeded.Fields[0].FieldId,"gte",["1"])],[2,3]),
            ];
            foreach(var (conditions,expected) in cases)
            {
                var sql=await reader.ListAsync(definition,new(){PageSize=100,Conditions=conditions},Ct);
                var memory=all.Items.Where(row=>FormRecordConditionEvaluator.Matches(conditions,new(row,"es"))).Select(row=>row.Record.Number).Order().ToArray();
                Assert.Equal(expected,memory);
                Assert.Equal(expected,sql.Items.Select(row=>row.Record.Number).Order());
            }
            foreach (var fieldId in new[] { choice.FieldId, photo.FieldId })
            {
                var presence = await reader.ListFacetsAsync(definition, new() { Dimension = "presence", FieldId = fieldId, Filter = new() }, Ct);
                var counts = presence.Items.ToDictionary(option => option.Id, option => option.Count);
                Assert.Equal(1, counts["answered"]);
                Assert.Equal(4, counts["unanswered"]);
                Assert.Equal(3, counts["notAsked"]);
            }
        });
    }
}
