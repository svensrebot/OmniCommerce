using Microsoft.AspNetCore.Mvc;
using OmniCommerce.Api.Application;

namespace OmniCommerce.Api.Controllers;

// Development-only PoC: no authentication, and cart ownership is not enforced.
[ApiController]
[Route("api/carts")]
[Produces("application/json")]
public class CartsController(ICartService carts) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CartResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CartResponse>> Create(CancellationToken ct)
    {
        var cart = await carts.CreateAsync(ct);
        return CreatedAtAction(nameof(Get), new { cartId = cart.Id }, cart);
    }

    [HttpGet("{cartId:guid}")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CartResponse>> Get(Guid cartId, CancellationToken ct) =>
        ToActionResult(await carts.GetAsync(cartId, ct));

    [HttpPost("{cartId:guid}/items")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CartResponse>> AddItem(Guid cartId, AddCartItemRequest request, CancellationToken ct) =>
        ToActionResult(await carts.AddItemAsync(cartId, request, ct));

    [HttpPut("{cartId:guid}/items/{productId:guid}")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CartResponse>> SetItemQuantity(
        Guid cartId, Guid productId, SetCartItemQuantityRequest request, CancellationToken ct) =>
        ToActionResult(await carts.SetItemQuantityAsync(cartId, productId, request.Quantity, ct));

    [HttpDelete("{cartId:guid}/items/{productId:guid}")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CartResponse>> RemoveItem(Guid cartId, Guid productId, CancellationToken ct) =>
        ToActionResult(await carts.RemoveItemAsync(cartId, productId, ct));

    [HttpDelete("{cartId:guid}/items")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CartResponse>> Clear(Guid cartId, CancellationToken ct) =>
        ToActionResult(await carts.ClearAsync(cartId, ct));

    private ActionResult<CartResponse> ToActionResult(CartResult result) => result.Outcome switch
    {
        CartOutcome.Success => Ok(result.Cart),
        CartOutcome.CartNotFound => Fail(StatusCodes.Status404NotFound, "Cart not found."),
        CartOutcome.ItemNotFound => Fail(StatusCodes.Status404NotFound, "The cart has no line for this product."),
        CartOutcome.ProductNotFound => Fail(StatusCodes.Status422UnprocessableEntity, "The product does not exist."),
        CartOutcome.QuantityLimitExceeded => Fail(
            StatusCodes.Status422UnprocessableEntity,
            $"A cart line may hold at most {Domain.Cart.MaxItemQuantity} units."),
        CartOutcome.Conflict => Fail(
            StatusCodes.Status409Conflict, "The cart was modified concurrently. Fetch the cart and retry."),
        _ => throw new InvalidOperationException($"Unhandled outcome {result.Outcome}."),
    };

    private ObjectResult Fail(int statusCode, string detail) => Problem(detail: detail, statusCode: statusCode);
}
