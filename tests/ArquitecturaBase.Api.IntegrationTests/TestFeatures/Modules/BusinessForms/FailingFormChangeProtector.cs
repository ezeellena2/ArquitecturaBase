using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
internal sealed class FailingFormChangeProtector : IFormChangeProtector
{
    public byte[] Protect(FormChangeProtectionContext context, string canonicalRequest, FormDraftCommitResponse response) => throw new InvalidOperationException("Expected form protection failure.");
    public FormProtectedChange Unprotect(FormChangeProtectionContext context, byte[] protectedResult) => throw new InvalidOperationException("Expected form protection failure.");
    public bool Matches(FormProtectedChange stored, string canonicalRequest) => false;
}
