using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
public interface IFormChangeProtector
{
    byte[] Protect(FormChangeProtectionContext context, string canonicalRequest, FormDraftCommitResponse response);
    FormProtectedChange Unprotect(FormChangeProtectionContext context, byte[] protectedResult);
    bool Matches(FormProtectedChange stored, string canonicalRequest);
}
