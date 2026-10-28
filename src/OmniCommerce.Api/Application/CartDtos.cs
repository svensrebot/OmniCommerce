using System.ComponentModel.DataAnnotations;
using OmniCommerce.Api.Domain;

namespace OmniCommerce.Api.Application;

public record AddCartItemRequest : IValidatableObject
{
    public Guid ProductId { get; init; }

    [Range(1, Cart.MaxItemQuantity)]
    public int Quantity { get; init; }

    // A missing productId deserializes to Guid.Empty; report it as a validation error (400), not as an unknown product.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ProductId == Guid.Empty)
        {
            yield return new ValidationResult("ProductId is required.", [nameof(ProductId)]);
        }
    }
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
