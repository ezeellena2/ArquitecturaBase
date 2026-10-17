using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;

public interface IFormPdfRenderer
{
    Task<byte[]> RenderAsync(FormPdfRenderRequest request, string culture, bool preview, CancellationToken cancellationToken);
}
