namespace OmniCommerce.Api.Domain;

public class CartItem
{
    private CartItem() { }

    internal CartItem(Guid id, Guid cartId, Guid productId, int quantity)
    {
        Id = id;
        CartId = cartId;
        ProductId = productId;
        Quantity = Cart.EnsurePositive(quantity);
    }

    public Guid Id { get; private set; }
    public Guid CartId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; internal set; }
}
