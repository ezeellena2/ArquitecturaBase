using System.Globalization;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormOverviewService(ICurrentUser currentUser, IPermissionService permissions, IUserReader users,
    IFormOverviewReader reader, IRequestValidator validator, ILogger<FormOverviewService> logger) : IFormOverviewService
{
    public Task<Result<FormOverviewResponse>> ListAsync(ListFormOverviewRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormOverviewResponse>>(logger, "ListFormOverview", async () =>
        {
            if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
            var canRead = await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsRead, cancellationToken);
            var canDecide = await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.ApprovalsDecide, cancellationToken);
            if ((!canRead && !canDecide) || await users.FindByIdAsync(actor, cancellationToken) is not { IsActive: true })
                return FormDefinitionErrors.Forbidden;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            if (request.PendingOnly && !canDecide) return FormDefinitionErrors.Forbidden;
            var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
            var result = await reader.FindPageAsync(actor, request, culture, canRead, canDecide, cancellationToken);
            if (currentUser.UserId != actor || await users.FindByIdAsync(actor, cancellationToken) is not { IsActive: true }
                || canRead && !await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsRead, cancellationToken)
                || canDecide && !await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.ApprovalsDecide, cancellationToken))
                return FormDefinitionErrors.Forbidden;
            return result;
        });
}