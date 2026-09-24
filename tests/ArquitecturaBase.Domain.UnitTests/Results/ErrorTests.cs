using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.UnitTests.Results;

public sealed class ErrorTests
{
    [Fact]
    public void Factories_set_the_error_type()
    {
        Assert.Equal(ErrorType.Failure, Error.Failure("A.B.C", "d").Type);
        Assert.Equal(ErrorType.Validation, Error.Validation("A.B.C", "d").Type);
        Assert.Equal(ErrorType.Unauthorized, Error.Unauthorized("A.B.C", "d").Type);
        Assert.Equal(ErrorType.Forbidden, Error.Forbidden("A.B.C", "d").Type);
        Assert.Equal(ErrorType.NotFound, Error.NotFound("A.B.C", "d").Type);
        Assert.Equal(ErrorType.Conflict, Error.Conflict("A.B.C", "d").Type);
        Assert.Equal(ErrorType.TooManyRequests, Error.TooManyRequests("A.B.C", "d").Type);
    }

    [Fact]
    public void Errors_with_the_same_data_are_equal()
    {
        Assert.Equal(Error.Conflict("A.B.C", "d"), Error.Conflict("A.B.C", "d"));
    }

    [Fact]
    public void Metadata_is_kept()
    {
        var metadata = new Dictionary<string, object?> { ["attemptsLeft"] = 3 };

        var error = Error.Validation("Auth.LoginCode.Invalid", "Invalid code.", metadata);

        Assert.Equal(3, error.Metadata!["attemptsLeft"]);
    }

    [Fact]
    public void Validation_error_groups_messages_by_field()
    {
        var error = new ValidationError(new Dictionary<string, string[]> { ["email"] = ["Invalid."] });

        Assert.Equal(ValidationError.ErrorCode, error.Code);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal("Invalid.", Assert.Single(error.Errors["email"]));
    }

    [Fact]
    public void A_validation_error_can_keep_the_code_of_a_business_rule_and_still_point_to_its_field()
    {
        var error = new ValidationError(
            "Users.Invitation.ConsentRequired",
            "The person must have accepted WhatsApp messages.",
            new Dictionary<string, string[]> { ["invitation.consent"] = ["Confirm it."] });

        Assert.Equal("Users.Invitation.ConsentRequired", error.Code);
        Assert.Equal("The person must have accepted WhatsApp messages.", error.Description);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal("Confirm it.", Assert.Single(error.Errors["invitation.consent"]));
    }
}
