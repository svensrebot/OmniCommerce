using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using OmniCommerce.Api.Application;

namespace OmniCommerce.Api.Controllers;

// Development-only PoC: no authentication, and cart ownership is not enforced.
// GET and POST /api/carts return a strong ETag. Every mutation requires it back in If-Match.
// Order of checks for mutations: model validation (400), If-Match header (428/400), cart (404),
// version (412), then the rest (item/product errors).
[ApiController]
[Route("api/carts")]
public class CartsController(ICartService carts) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CartResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CartResponse>> Create(CancellationToken ct)
    {
        var result = await carts.CreateAsync(ct);
        SetETag(result);
        return CreatedAtAction(nameof(Get), new { cartId = result.Cart!.Id }, result.Cart);
    }

    [HttpGet("{cartId:guid}")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CartResponse>> Get(Guid cartId, CancellationToken ct) =>
        ToActionResult(await carts.GetAsync(cartId, ct));

    [HttpPost("{cartId:guid}/items")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public Task<ActionResult<CartResponse>> AddItem(Guid cartId, AddCartItemRequest request, CancellationToken ct) =>
        Mutate(version => carts.AddItemAsync(cartId, version, request, ct));

    [HttpPut("{cartId:guid}/items/{productId:guid}")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public Task<ActionResult<CartResponse>> SetItemQuantity(
        Guid cartId, Guid productId, SetCartItemQuantityRequest request, CancellationToken ct) =>
        Mutate(version => carts.SetItemQuantityAsync(cartId, version, productId, request.Quantity, ct));

    [HttpDelete("{cartId:guid}/items/{productId:guid}")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public Task<ActionResult<CartResponse>> RemoveItem(Guid cartId, Guid productId, CancellationToken ct) =>
        Mutate(version => carts.RemoveItemAsync(cartId, version, productId, ct));

    [HttpDelete("{cartId:guid}/items")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public Task<ActionResult<CartResponse>> Clear(Guid cartId, CancellationToken ct) =>
        Mutate(version => carts.ClearAsync(cartId, version, ct));

    private async Task<ActionResult<CartResponse>> Mutate(Func<byte[], Task<CartResult>> action)
    {
        switch (CartETag.ParseIfMatch(Request.Headers.IfMatch.ToArray()!, out var version))
        {
            case IfMatchStatus.Missing:
                return Fail(StatusCodes.Status428PreconditionRequired,
                    "This request requires an If-Match header with the cart's current ETag. GET the cart to obtain it.");
            case IfMatchStatus.Invalid:
                return Fail(StatusCodes.Status400BadRequest,
                    "If-Match must contain exactly one strong ETag as returned by the API (for example \"0000000000004e21\"). Weak tags, \"*\" and lists are not supported.");
        }

        return ToActionResult(await action(version));
    }

    private ActionResult<CartResponse> ToActionResult(CartResult result)
    {
        if (result.IsSuccess)
        {
            SetETag(result);
            return Ok(result.Cart);
        }

        return result.Outcome switch
        {
            CartOutcome.CartNotFound => Fail(StatusCodes.Status404NotFound, "Cart not found."),
            CartOutcome.ItemNotFound => Fail(StatusCodes.Status404NotFound, "The cart has no line for this product."),
            CartOutcome.ProductNotFound => Fail(StatusCodes.Status422UnprocessableEntity, "The product does not exist."),
            CartOutcome.QuantityLimitExceeded => Fail(
                StatusCodes.Status422UnprocessableEntity,
                $"A cart line may hold at most {Domain.Cart.MaxItemQuantity} units."),
            CartOutcome.PreconditionFailed => Fail(
                StatusCodes.Status412PreconditionFailed,
                "The cart has changed since the supplied ETag was issued. Nothing was modified. GET the cart for its current ETag and retry."),
            _ => throw new InvalidOperationException($"Unhandled outcome {result.Outcome}."),
        };
    }

    private void SetETag(CartResult result) => Response.Headers.ETag = CartETag.Format(result.RowVersion!);

    private ObjectResult Fail(int statusCode, string detail) =>
        Problem(detail: detail, statusCode: statusCode, title: ReasonPhrases.GetReasonPhrase(statusCode));
}
