using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.Roles;
using FluentValidation;

namespace ArquitecturaBase.Application.Validation.Roles;

internal sealed class UpdateRoleRequestValidator : AbstractValidator<UpdateRoleRequest>
{
    public UpdateRoleRequestValidator()
    {
        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .Required()
            .MaxLength(ValidationRules.RoleNameMaxLength);

        RuleFor(request => request.Description).MaxLength(ValidationRules.RoleDescriptionMaxLength);

        RuleFor(request => request.Permissions).ValidPermissions();
    }
}
