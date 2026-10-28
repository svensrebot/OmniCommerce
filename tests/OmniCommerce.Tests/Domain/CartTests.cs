using OmniCommerce.Api.Domain;

namespace OmniCommerce.Tests.Domain;

public class CartTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(5);
    private static readonly Guid ProductId = Guid.NewGuid();

    [Fact]
    public void AddItem_CreatesLineAndTouchesCart()
    {
        var cart = Cart.Create(T0);

        cart.AddItem(ProductId, 2, T1);

        var item = Assert.Single(cart.Items);
        Assert.Equal(ProductId, item.ProductId);
        Assert.Equal(cart.Id, item.CartId);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(T1, cart.UpdatedAt);
    }

    [Fact]
    public void AddItem_SameProductTwice_MergesQuantity()
    {
        var cart = Cart.Create(T0);

        cart.AddItem(ProductId, 2, T0);
        cart.AddItem(ProductId, 3, T1);

        Assert.Equal(5, Assert.Single(cart.Items).Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddItem_NonPositiveQuantity_Throws(int quantity)
    {
        var cart = Cart.Create(T0);

        Assert.Throws<ArgumentOutOfRangeException>(() => cart.AddItem(ProductId, quantity, T1));
        Assert.Empty(cart.Items);
    }

    [Fact]
    public void SetItemQuantity_NonPositive_ThrowsAndKeepsQuantity()
    {
        var cart = Cart.Create(T0);
        cart.AddItem(ProductId, 2, T0);

        Assert.Throws<ArgumentOutOfRangeException>(() => cart.SetItemQuantity(ProductId, 0, T1));
        Assert.Equal(2, cart.Items.Single().Quantity);
        Assert.Equal(T0, cart.UpdatedAt);
    }

    [Fact]
    public void SetItemQuantity_And_RemoveItem_ReportMissingLines()
    {
        var cart = Cart.Create(T0);

        Assert.False(cart.SetItemQuantity(ProductId, 1, T1));
        Assert.False(cart.RemoveItem(ProductId, T1));
        Assert.Equal(T0, cart.UpdatedAt);
    }

    [Fact]
    public void AddItem_MergedQuantityAboveMaximum_ThrowsAndKeepsQuantity()
    {
        var cart = Cart.Create(T0);
        cart.AddItem(ProductId, Cart.MaxItemQuantity, T0);

        Assert.Throws<ArgumentOutOfRangeException>(() => cart.AddItem(ProductId, 1, T1));
        Assert.Equal(Cart.MaxItemQuantity, cart.Items.Single().Quantity);
    }

    [Fact]
    public void Clear_RemovesAllLinesAndTouchesCart()
    {
        var cart = Cart.Create(T0);
        cart.AddItem(ProductId, 1, T0);
        cart.AddItem(Guid.NewGuid(), 1, T0);

        cart.Clear(T1);

        Assert.Empty(cart.Items);
        Assert.Equal(T1, cart.UpdatedAt);
    }

    [Fact]
    public void RemoveItem_RemovesLineAndTouchesCart()
    {
        var cart = Cart.Create(T0);
        cart.AddItem(ProductId, 1, T0);

        Assert.True(cart.RemoveItem(ProductId, T1));

        Assert.Empty(cart.Items);
        Assert.Equal(T1, cart.UpdatedAt);
    }
}
