using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

public sealed record FormPreviewQuery
{
    [FromQuery(Name = "culture")]
    public string? Culture { get; init; }
}
