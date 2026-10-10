using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppFlowUncertainAccountTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(WhatsAppFlowStep.Create)]
    [InlineData(WhatsAppFlowStep.Upload)]
    [InlineData(WhatsAppFlowStep.Publish)]
    public void A_temporary_account_failure_during_reconciliation_never_reopens_an_uncertain_effect(WhatsAppFlowStep effectStep)
    {
        var binding = WhatsAppFlowBinding.Create(Guid.NewGuid(), Guid.NewGuid(), "es", "test", []);
        long fence = 1;
        Assert.Equal(WhatsAppFlowStep.Create, binding.Begin(fence, Now).Value);
        if (effectStep != WhatsAppFlowStep.Create)
        {
            Assert.True(binding.RecordEffect(fence, Now, "123", true, false, false).IsSuccess);
            Assert.Equal(WhatsAppFlowStep.Upload, binding.Begin(++fence, Now).Value);
            if (effectStep == WhatsAppFlowStep.Publish)
            {
                Assert.True(binding.RecordEffect(fence, Now, null, true, false, false).IsSuccess);
                Assert.Equal(WhatsAppFlowStep.Validate, binding.Begin(++fence, Now).Value);
                Assert.True(binding.RecordVerification(fence, Now, false, true, true, true, true).IsSuccess);
                Assert.Equal(WhatsAppFlowStep.Publish, binding.Begin(++fence, Now).Value);
            }
        }

        Assert.True(binding.RecordEffect(fence, Now, null, false, false, true).IsSuccess);
        Assert.Equal(WhatsAppFlowStep.Verify, binding.Begin(++fence, Now.AddMinutes(2)).Value);
        Assert.True(binding.RetryWithoutEffect(fence).IsSuccess);
        Assert.Equal(WhatsAppFlowPublicationState.Uncertain, binding.State);
        Assert.Equal(WhatsAppFlowStep.Verify, binding.Begin(++fence, Now.AddMinutes(4)).Value);
        Assert.Equal(effectStep, binding.Step);
        Assert.Equal(effectStep == WhatsAppFlowStep.Create ? null : "123", binding.MetaFlowId);
    }
}
