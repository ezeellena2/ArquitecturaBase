using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.UnitTests.Common;

public sealed class EntityTests
{
    private sealed class Product : Entity
    {
        public Product()
        {
        }

        public Product(Guid id)
            : base(id)
        {
        }
    }

    private sealed class Order(Guid id) : Entity(id);

    [Fact]
    public void New_entity_gets_a_version_7_guid()
    {
        var product = new Product();

        Assert.Equal(7, product.Id.Version);
    }

    [Fact]
    public void Entities_with_the_same_type_and_id_are_equal()
    {
        var id = Guid.CreateVersion7();
        var first = new Product(id);
        var second = new Product(id);

        var equalByOperator = first == second;

        Assert.Equal(first, second);
        Assert.True(equalByOperator);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Entities_of_different_types_are_not_equal_even_with_the_same_id()
    {
        var id = Guid.CreateVersion7();

        Assert.NotEqual<Entity>(new Product(id), new Order(id));
    }

    [Fact]
    public void Empty_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Product(Guid.Empty));
    }
}
