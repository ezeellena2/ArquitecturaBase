using ArquitecturaBase.Application.Common.Validation;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Roles.UpdateRole;

internal sealed class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
    {
        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .Required()
            .MaxLength(ValidationRules.RoleNameMaxLength);

        RuleFor(command => command.Description).MaxLength(ValidationRules.RoleDescriptionMaxLength);

        RuleFor(command => command.Permissions).ValidPermissions();
    }
}
