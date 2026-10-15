using System.ComponentModel.DataAnnotations;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Configuration;

public sealed class BusinessFormsOptions
{
    [Range(1, 25)]
    public int MaxFileSizeMiB { get; init; } = 25;
}
