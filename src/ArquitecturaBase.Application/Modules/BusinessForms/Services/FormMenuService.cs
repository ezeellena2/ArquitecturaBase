using System.Globalization;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
using P = ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormsPermissions;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The folders of the side menu (CM1 to CM6). Every folder of the first level is a menu with its icon, the active forms directly in it
/// and a group per subfolder with the active forms directly in that one, all in alphabetical order. Any active account reads it: whoever
/// administers forms sees every folder, also the empty ones, every active form and how many catalogs each menu holds; whoever can open
/// forms (see, decide or load responses) sees only what they can open and nothing empty; anyone else gets it empty.
/// </summary>
internal sealed class FormMenuService(ICurrentUser currentUser, IPermissionService permissions, IUserReader users,
    IFormFolderReader reader, ILogger<FormMenuService> logger) : IFormMenuService
{
    private static readonly FormMenuResponse Empty = new(false, []);

    public Task<Result<FormMenuResponse>> GetAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormMenuResponse>>(logger, "GetFormMenu", async () =>
        {
            if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
            if (await users.FindByIdAsync(actor, cancellationToken) is not { IsActive: true }) return FormDefinitionErrors.Forbidden;
            var canManage = await permissions.HasPermissionAsync(actor, P.Manage, cancellationToken);
            var canReadRecords = await permissions.HasPermissionAsync(actor, P.RecordsRead, cancellationToken);
            var canCreate = await permissions.HasPermissionAsync(actor, P.RecordsCreate, cancellationToken);
            if (!canManage && !canReadRecords && !canCreate && !await permissions.HasPermissionAsync(actor, P.ApprovalsDecide, cancellationToken))
                return Empty;
            var folders = await reader.ListAsync(cancellationToken);
            if (folders.Count == 0) return new FormMenuResponse(canManage, []);
            var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
            var folderIds = folders.Select(folder => folder.Id).ToArray();
            var rows = canManage
                ? await reader.ListActiveMenuFormsAsync(folderIds, culture, cancellationToken)
                : await reader.ListMenuFormsAsync(folderIds, culture, actor, canReadRecords, canCreate, cancellationToken);
            var catalogs = canManage
                ? (await reader.ListMenuCatalogCountsAsync(cancellationToken)).ToDictionary(row => row.FolderId, row => row.Count)
                : [];
            var byFolder = rows.ToLookup(row => row.FolderId);
            var subfolders = folders.Where(folder => folder.ParentId is not null).ToLookup(folder => folder.ParentId!.Value);
            IReadOnlyList<FormMenuFormResponse> Forms(Guid folderId) => FormFolderService.Alphabetical(
                byFolder[folderId].Select(row => new FormMenuFormResponse(row.DefinitionId, row.Name)), form => form.Name, form => form.Id).ToArray();
            IEnumerable<FormFolderRow> InOrder(IEnumerable<FormFolderRow> items) => FormFolderService.Alphabetical(items, folder => folder.Name, folder => folder.Id);
            // The reader gives every folder of the first level its icon.
            var menus = InOrder(folders.Where(folder => folder.ParentId is null))
                .Select(folder => new FormMenuSectionResponse(folder.Id, folder.Name, folder.MenuIcon!,
                    folder.Revision, catalogs.GetValueOrDefault(folder.Id), Forms(folder.Id),
                    InOrder(subfolders[folder.Id]).Select(group => new FormMenuGroupResponse(group.Id, group.Name, group.Revision, Forms(group.Id)))
                        .Where(group => canManage || group.Forms.Count > 0).ToArray()))
                .Where(menu => canManage || menu.Forms.Count > 0 || menu.Groups.Count > 0).ToArray();
            return new FormMenuResponse(canManage, menus);
        });
}
