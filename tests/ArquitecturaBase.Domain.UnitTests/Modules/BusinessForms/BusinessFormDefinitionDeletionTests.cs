using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormDefinitionDeletionTests
{
    [Fact]
    public void A_definition_uses_interceptor_owned_soft_deletion_metadata()
    {
        var definition = BusinessFormDefinition.Create();
        var metadata = Assert.IsAssignableFrom<ISoftDeletable>(definition);
        Assert.False(metadata.IsDeleted); Assert.Null(metadata.DeletedAtUtc); Assert.Null(metadata.DeletedBy);
    }
}
