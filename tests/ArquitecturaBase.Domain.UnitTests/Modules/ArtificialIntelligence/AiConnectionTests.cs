using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Domain.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiConnectionTests
{
    [Fact]
    public void Removing_the_credential_pauses_the_connection_and_advances_one_revision()
    {
        var connection = AiConnection.Create();
        Assert.True(connection.Update(AiProvider.OpenAI, "model", "ciphertext", true).IsSuccess);
        Assert.True(connection.Update(AiProvider.OpenAI, "model", null, false).IsSuccess);
        Assert.Null(connection.ProtectedApiKey);
        Assert.False(connection.Enabled);
        Assert.Equal(2, connection.Revision);
    }

    [Fact]
    public void An_enabled_connection_requires_a_credential_without_mutating_the_previous_state()
    {
        var connection = AiConnection.Create();
        var result = connection.Update(AiProvider.Anthropic, "model", null, true);
        Assert.Equal(AiConnectionErrors.CredentialRequired, result.Error);
        Assert.Equal(0, connection.Revision);
    }

    [Fact]
    public void The_model_is_trimmed_and_unknown_providers_are_rejected()
    {
        var connection = AiConnection.Create();
        Assert.True(connection.Update(AiProvider.Anthropic, "  model  ", null, false).IsSuccess);
        Assert.Equal("model", connection.Model);
        Assert.True(connection.Update((AiProvider)99, "model", null, false).IsFailure);
        Assert.Equal(AiProvider.Anthropic, connection.Provider);
        Assert.Equal(1, connection.Revision);
    }

    [Fact]
    public void Diagnostic_strings_do_not_expose_protected_credentials()
    {
        var connection = AiConnection.Create();
        connection.Update(AiProvider.OpenAI, "model", "secret-ciphertext", false);
        Assert.DoesNotContain("secret-ciphertext", connection.ToString(), StringComparison.Ordinal);
    }
}
