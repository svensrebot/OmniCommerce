using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OmniCommerce.Api.Domain;
using OmniCommerce.Api.Infrastructure;

namespace OmniCommerce.Api.Application;

public class CartService(CommerceDbContext db, TimeProvider clock) : ICartService
{
    public async Task<CartResponse> CreateAsync(CancellationToken ct)
    {
        var cart = Cart.Create(clock.GetUtcNow());
        db.Carts.Add(cart);
        await db.SaveChangesAsync(ct);
        return await ToResponseAsync(cart, ct);
    }

    public async Task<CartResult> GetAsync(Guid cartId, CancellationToken ct)
    {
        var cart = await LoadAsync(cartId, ct);
        return cart is null ? new(CartOutcome.CartNotFound) : await SuccessAsync(cart, ct);
    }

    public async Task<CartResult> AddItemAsync(Guid cartId, AddCartItemRequest request, CancellationToken ct)
    {
        var cart = await LoadAsync(cartId, ct);
        if (cart is null)
        {
            return new(CartOutcome.CartNotFound);
        }

        if (!await db.Products.AnyAsync(p => p.Id == request.ProductId, ct))
        {
            return new(CartOutcome.ProductNotFound);
        }

        var existing = cart.Items.FirstOrDefault(i => i.ProductId == request.ProductId)?.Quantity ?? 0;
        if (existing + request.Quantity > Cart.MaxItemQuantity)
        {
            return new(CartOutcome.QuantityLimitExceeded);
        }

        cart.AddItem(request.ProductId, request.Quantity, clock.GetUtcNow());
        return await SaveAsync(cart, ct);
    }

    public async Task<CartResult> SetItemQuantityAsync(Guid cartId, Guid productId, int quantity, CancellationToken ct)
    {
        var cart = await LoadAsync(cartId, ct);
        if (cart is null)
        {
            return new(CartOutcome.CartNotFound);
        }

        return cart.SetItemQuantity(productId, quantity, clock.GetUtcNow())
            ? await SaveAsync(cart, ct)
            : new(CartOutcome.ItemNotFound);
    }

    public async Task<CartResult> RemoveItemAsync(Guid cartId, Guid productId, CancellationToken ct)
    {
        var cart = await LoadAsync(cartId, ct);
        if (cart is null)
        {
            return new(CartOutcome.CartNotFound);
        }

        return cart.RemoveItem(productId, clock.GetUtcNow())
            ? await SaveAsync(cart, ct)
            : new(CartOutcome.ItemNotFound);
    }

    public async Task<CartResult> ClearAsync(Guid cartId, CancellationToken ct)
    {
        var cart = await LoadAsync(cartId, ct);
        if (cart is null)
        {
            return new(CartOutcome.CartNotFound);
        }

        cart.Clear(clock.GetUtcNow());
        return await SaveAsync(cart, ct);
    }

    private Task<Cart?> LoadAsync(Guid cartId, CancellationToken ct) =>
        db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == cartId, ct);

    // Item changes alone never touch the Carts row. Forcing UpdatedAt into the UPDATE makes every
    // mutation write the parent cart in the same SaveChanges transaction, and EF includes the
    // RowVersion token in that UPDATE's WHERE clause. Stage 4 sets the expected version here.
    private async Task<CartResult> SaveAsync(Cart cart, CancellationToken ct)
    {
        db.Entry(cart).Property(c => c.UpdatedAt).IsModified = true;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(CartOutcome.Conflict);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Concurrent insert of the same (CartId, ProductId) line.
            return new(CartOutcome.Conflict);
        }

        return await SuccessAsync(cart, ct);
    }

    private async Task<CartResult> SuccessAsync(Cart cart, CancellationToken ct) =>
        new(CartOutcome.Success, await ToResponseAsync(cart, ct));

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
