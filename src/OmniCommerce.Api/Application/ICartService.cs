namespace OmniCommerce.Api.Application;

public enum CartOutcome
{
    Success,
    CartNotFound,
    ItemNotFound,
    ProductNotFound,
    QuantityLimitExceeded,
    PreconditionFailed,
}

public record CartResult(CartOutcome Outcome, CartResponse? Cart = null, byte[]? RowVersion = null)
{
    public bool IsSuccess => Outcome == CartOutcome.Success;
}

// Every mutation takes the rowversion the caller last saw (from If-Match) and succeeds only if the
// cart still has exactly that version.
public interface ICartService
{
    Task<CartResult> CreateAsync(CancellationToken ct);
    Task<CartResult> GetAsync(Guid cartId, CancellationToken ct);
    Task<CartResult> AddItemAsync(Guid cartId, byte[] expectedVersion, AddCartItemRequest request, CancellationToken ct);
    Task<CartResult> SetItemQuantityAsync(Guid cartId, byte[] expectedVersion, Guid productId, int quantity, CancellationToken ct);
    Task<CartResult> RemoveItemAsync(Guid cartId, byte[] expectedVersion, Guid productId, CancellationToken ct);
    Task<CartResult> ClearAsync(Guid cartId, byte[] expectedVersion, CancellationToken ct);
}
