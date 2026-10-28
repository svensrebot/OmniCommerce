using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OmniCommerce.Api.Domain;
using OmniCommerce.Api.Infrastructure;

namespace OmniCommerce.Api.Application;

public class CartService(CommerceDbContext db, TimeProvider clock) : ICartService
{
    public async Task<CartResult> CreateAsync(CancellationToken ct)
    {
        var cart = Cart.Create(clock.GetUtcNow());
        db.Carts.Add(cart);
        await db.SaveChangesAsync(ct);
        return await SuccessAsync(cart, ct);
    }

    public async Task<CartResult> GetAsync(Guid cartId, CancellationToken ct)
    {
        var cart = await LoadAsync(cartId, ct);
        return cart is null ? new(CartOutcome.CartNotFound) : await SuccessAsync(cart, ct);
    }

    public Task<CartResult> AddItemAsync(Guid cartId, byte[] expectedVersion, AddCartItemRequest request, CancellationToken ct) =>
        MutateAsync(cartId, expectedVersion, async (cart, now) =>
        {
            if (!await db.Products.AnyAsync(p => p.Id == request.ProductId, ct))
            {
                return CartOutcome.ProductNotFound;
            }

            var existing = cart.Items.FirstOrDefault(i => i.ProductId == request.ProductId)?.Quantity ?? 0;
            if (existing + request.Quantity > Cart.MaxItemQuantity)
            {
                return CartOutcome.QuantityLimitExceeded;
            }

            cart.AddItem(request.ProductId, request.Quantity, now);
            return CartOutcome.Success;
        }, ct);

    public Task<CartResult> SetItemQuantityAsync(Guid cartId, byte[] expectedVersion, Guid productId, int quantity, CancellationToken ct) =>
        MutateAsync(cartId, expectedVersion, (cart, now) => Task.FromResult(
            cart.SetItemQuantity(productId, quantity, now) ? CartOutcome.Success : CartOutcome.ItemNotFound), ct);

    // Removing a line the cart does not have is a 404 and changes nothing (the cart version is kept).
    public Task<CartResult> RemoveItemAsync(Guid cartId, byte[] expectedVersion, Guid productId, CancellationToken ct) =>
        MutateAsync(cartId, expectedVersion, (cart, now) => Task.FromResult(
            cart.RemoveItem(productId, now) ? CartOutcome.Success : CartOutcome.ItemNotFound), ct);

    public Task<CartResult> ClearAsync(Guid cartId, byte[] expectedVersion, CancellationToken ct) =>
        MutateAsync(cartId, expectedVersion, (cart, now) =>
        {
            cart.Clear(now);
            return Task.FromResult(CartOutcome.Success);
        }, ct);

    private Task<Cart?> LoadAsync(Guid cartId, CancellationToken ct) =>
        db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == cartId, ct);

    // One SQL transaction per mutation. Its first statement is
    //   UPDATE Carts SET UpdatedAt = @now WHERE Id = @id AND RowVersion = @expected
    // so the version check and the parent-cart write are a single atomic statement. It also takes the
    // cart row lock first, which serializes concurrent writers: a request holding the same stale
    // version blocks until the winner commits, then matches 0 rows and is rejected. Item changes
    // follow in the same transaction; any non-success outcome or failure rolls everything back.
    private async Task<CartResult> MutateAsync(
        Guid cartId, byte[] expectedVersion, Func<Cart, DateTimeOffset, Task<CartOutcome>> apply, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var updated = await db.Carts
            .Where(c => c.Id == cartId && c.RowVersion == expectedVersion)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, now), ct);

        if (updated == 0)
        {
            var exists = await db.Carts.AnyAsync(c => c.Id == cartId, ct);
            return new(exists ? CartOutcome.PreconditionFailed : CartOutcome.CartNotFound);
        }

        // Read after the gate update, inside the transaction: sees the new RowVersion and no other writer can intervene.
        var cart = await LoadAsync(cartId, ct) ?? throw new InvalidOperationException("Cart vanished inside the transaction.");

        var outcome = await apply(cart, now);
        if (outcome != CartOutcome.Success)
        {
            return new(outcome);
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(CartOutcome.PreconditionFailed);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A duplicate (CartId, ProductId) line can only come from a writer that changed the cart first.
            return new(CartOutcome.PreconditionFailed);
        }

        return await SuccessAsync(cart, ct);
    }

    private async Task<CartResult> SuccessAsync(Cart cart, CancellationToken ct) =>
        new(CartOutcome.Success, await ToResponseAsync(cart, ct), cart.RowVersion);

    private async Task<CartResponse> ToResponseAsync(Cart cart, CancellationToken ct)
    {
        var productIds = cart.Items.Select(i => i.ProductId).ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var items = cart.Items
            .Select(i => new CartItemResponse(i.ProductId, products[i.ProductId].Sku, products[i.ProductId].Name, i.Quantity))
            .OrderBy(i => i.Sku)
            .ToList();

        return new CartResponse(cart.Id, cart.CreatedAt, cart.UpdatedAt, cart.ExpiresAt, items);
    }
}
