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
/// The folders in the side menu (CP5-A). Any active account reads it; whoever cannot open forms (see, decide or load responses) gets
/// it empty. An own menu holds its forms and a group per subfolder with everything below it, so it never goes past two levels; a
/// folder inside a panel is one group with every form below it. Only active forms, alphabetically, and nothing empty.
/// </summary>
internal sealed class FormMenuService(ICurrentUser currentUser, IPermissionService permissions, IUserReader users,
    IFormFolderReader reader, ILogger<FormMenuService> logger) : IFormMenuService
{
    private static readonly FormMenuResponse Empty = new([], [], []);

    public Task<Result<FormMenuResponse>> GetAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormMenuResponse>>(logger, "GetFormMenu", async () =>
        {
            if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
            if (await users.FindByIdAsync(actor, cancellationToken) is not { IsActive: true }) return FormDefinitionErrors.Forbidden;
            if (!await permissions.HasPermissionAsync(actor, P.RecordsRead, cancellationToken)
                && !await permissions.HasPermissionAsync(actor, P.ApprovalsDecide, cancellationToken)
                && !await permissions.HasPermissionAsync(actor, P.RecordsCreate, cancellationToken)) return Empty;
            var folders = await reader.ListAsync(cancellationToken);
            var placed = folders.Where(folder => folder.MenuPlacement != nameof(FormFolderMenuPlacement.None)).ToArray();
            if (placed.Length == 0) return Empty;
            var nodes = FormFolderService.Nodes(folders);
            var subtrees = folders.ToDictionary(folder => folder.Id, folder => FormFolderTree.Subtree(folder.Id, nodes));
            var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
            var rows = await reader.ListMenuFormsAsync(placed.SelectMany(folder => subtrees[folder.Id]).Distinct().ToArray(), culture, cancellationToken);
            var byFolder = rows.ToLookup(row => row.FolderId);
            IReadOnlyList<FormMenuFormResponse> Forms(IEnumerable<Guid> inside) => FormFolderService.Alphabetical(
                inside.SelectMany(id => byFolder[id]).DistinctBy(row => row.DefinitionId).Select(row => new FormMenuFormResponse(row.DefinitionId, row.Name)),
                form => form.Name, form => form.Id).ToArray();
            IReadOnlyList<FormMenuGroupResponse> Groups(IEnumerable<FormFolderRow> groupFolders) => FormFolderService.Alphabetical(groupFolders,
                    folder => folder.Name, folder => folder.Id)
                .Select(folder => new FormMenuGroupResponse(folder.Id, folder.Name, Forms(subtrees[folder.Id])))
                .Where(group => group.Forms.Count > 0).ToArray();
            var menus = FormFolderService.Alphabetical(placed.Where(folder => folder.MenuPlacement == nameof(FormFolderMenuPlacement.Own)),
                    folder => folder.Name, folder => folder.Id)
                .Select(folder => new FormMenuSectionResponse(folder.Id, folder.Name, folder.MenuIcon ?? BusinessFormFolder.DefaultMenuIcon,
                    Forms([folder.Id]), Groups(folders.Where(child => child.ParentId == folder.Id))))
                .Where(menu => menu.Forms.Count > 0 || menu.Groups.Count > 0).ToArray();
            return new FormMenuResponse(menus,
                Groups(placed.Where(folder => folder.MenuPlacement == nameof(FormFolderMenuPlacement.Forms))),
                Groups(placed.Where(folder => folder.MenuPlacement == nameof(FormFolderMenuPlacement.Administration))));
        });
}
