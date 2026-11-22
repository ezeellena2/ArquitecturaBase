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

    // RC1-A: Home conversations last 30 days unless the administrator shortens them, never beyond the installation's ceiling.
    [Fact]
    public void Home_conversations_last_thirty_days_until_the_administrator_shortens_them()
    {
        var connection = AiConnection.Create();
        Assert.Equal(30, connection.ConversationRetentionDays);

        Assert.True(connection.Update(AiProvider.OpenAI, "model", null, false, conversationRetentionDays: 7, conversationRetentionCeilingDays: 30).IsSuccess);

        Assert.Equal(7, connection.ConversationRetentionDays);
        Assert.Equal(1, connection.Revision);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(31, 30)]
    [InlineData(11, 10)]
    public void A_retention_outside_one_and_the_ceiling_keeps_the_connection(int days, int ceiling)
    {
        var connection = AiConnection.Create();

        var result = connection.Update(AiProvider.Anthropic, "other", null, false, days, ceiling);

        Assert.Equal(AiConnectionErrors.RetentionInvalid, result.Error);
        Assert.Equal((AiProvider.OpenAI, "", 30, 0L), (connection.Provider, connection.Model, connection.ConversationRetentionDays, connection.Revision));
    }

    [Fact]
    public void Diagnostic_strings_do_not_expose_protected_credentials()
    {
        var connection = AiConnection.Create();
        connection.Update(AiProvider.OpenAI, "model", "secret-ciphertext", false);
        Assert.DoesNotContain("secret-ciphertext", connection.ToString(), StringComparison.Ordinal);
    }
}
