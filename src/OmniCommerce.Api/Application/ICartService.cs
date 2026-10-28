namespace OmniCommerce.Api.Application;

public enum CartOutcome
{
    Success,
    CartNotFound,
    ItemNotFound,
    ProductNotFound,
    QuantityLimitExceeded,
    Conflict,
}

public record CartResult(CartOutcome Outcome, CartResponse? Cart = null)
{
    public bool IsSuccess => Outcome == CartOutcome.Success;
}

public interface ICartService
{
    Task<CartResponse> CreateAsync(CancellationToken ct);
    Task<CartResult> GetAsync(Guid cartId, CancellationToken ct);
    Task<CartResult> AddItemAsync(Guid cartId, AddCartItemRequest request, CancellationToken ct);
    Task<CartResult> SetItemQuantityAsync(Guid cartId, Guid productId, int quantity, CancellationToken ct);
    Task<CartResult> RemoveItemAsync(Guid cartId, Guid productId, CancellationToken ct);
    Task<CartResult> ClearAsync(Guid cartId, CancellationToken ct);
}
