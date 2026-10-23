using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Common.Validation;

public sealed class FieldErrorsTests
{
    [Fact]
    public void Explicit_metadata_preserves_the_conflict_and_the_localized_field_error()
    {
        var source = RoleErrors.HasUsers(7);
        var result = FieldErrors.OnWithMetadata(source, "fields[0].isActive");

        Assert.Equal(source.Code, result.Code);
        Assert.Equal(ErrorType.Conflict, result.Type);
        Assert.Equal(7, result.Metadata![RoleErrors.UserCountKey]);
        Assert.Single(result.Errors["fields[0].isActive"]);
    }

    [Fact]
    public void Existing_callers_keep_their_validation_status_and_do_not_copy_metadata()
    {
        var result = FieldErrors.On(RoleErrors.HasUsers(7), "name");
        Assert.Equal(ErrorType.Validation, result.Type);
        Assert.Null(result.Metadata);
    }
}
