namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence;

internal sealed class BusinessFormCatalogUserView
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string[] Roles { get; init; } = [];

    public bool Active { get; init; }
}
