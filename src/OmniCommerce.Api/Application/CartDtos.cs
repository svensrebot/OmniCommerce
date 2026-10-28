using System.ComponentModel.DataAnnotations;
using OmniCommerce.Api.Domain;

namespace OmniCommerce.Api.Application;

public record AddCartItemRequest
{
    public Guid ProductId { get; init; }

    [Range(1, Cart.MaxItemQuantity)]
    public int Quantity { get; init; }
}

public record SetCartItemQuantityRequest
{
    [Range(1, Cart.MaxItemQuantity)]
    public int Quantity { get; init; }
}

public record CartItemResponse(Guid ProductId, string Sku, string Name, int Quantity);

public record CartResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<CartItemResponse> Items);
