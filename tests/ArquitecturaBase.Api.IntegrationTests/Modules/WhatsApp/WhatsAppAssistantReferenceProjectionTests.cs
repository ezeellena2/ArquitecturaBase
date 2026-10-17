using System.Reflection;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppAssistantReferenceProjectionTests
{
    [Fact]
    public void Reading_a_question_preserves_the_outgoing_identity_needed_to_accept_its_choice()
    {
        var now = new DateTime(2026,10,17,15,0,0,DateTimeKind.Utc);
        var request = WhatsAppAssistantRequest.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            WhatsAppAssistantRequestKind.Automatic,3,2,"{}",now,now.AddDays(1));
        var questionId=Guid.NewGuid();
        Assert.True(request.Ready(Guid.NewGuid(),null,now));Assert.True(request.AttachQuestion(questionId,now));
        var projection=typeof(WhatsAppAssistantReader).GetMethod("ReferenceRows",BindingFlags.NonPublic|BindingFlags.Static)!;
        var rows=Assert.IsAssignableFrom<IQueryable<WhatsAppAssistantReferenceRow>>(projection.Invoke(null,[new[]{request}.AsQueryable()]));
        var row=Assert.Single(rows);Assert.Equal(questionId,row.QuestionOutgoingId);Assert.Equal(request.RequestId,row.RequestId);
    }
}
