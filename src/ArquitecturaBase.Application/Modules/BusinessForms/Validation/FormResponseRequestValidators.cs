using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;
internal sealed class FormNavigationRequestValidator() : PagedRequestValidator<FormNavigationRequest>(FormNavigationRequest.SortableFields);
internal sealed class ListFormRecordsRequestValidator() : PagedRequestValidator<ListFormRecordsRequest>(ListFormRecordsRequest.SortableFields);
