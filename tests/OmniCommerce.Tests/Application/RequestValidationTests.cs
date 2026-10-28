using System.ComponentModel.DataAnnotations;
using OmniCommerce.Api.Application;
using OmniCommerce.Api.Domain;

namespace OmniCommerce.Tests.Application;

public class RequestValidationTests
{
    private static bool IsValid(object request) =>
        Validator.TryValidateObject(request, new ValidationContext(request), [], validateAllProperties: true);

    [Theory]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(1, true)]
    [InlineData(Cart.MaxItemQuantity, true)]
    [InlineData(Cart.MaxItemQuantity + 1, false)]
    public void AddItemRequest_QuantityMustBeWithinRange(int quantity, bool expected) =>
        Assert.Equal(expected, IsValid(new AddCartItemRequest { ProductId = Guid.NewGuid(), Quantity = quantity }));

    [Fact]
    public void AddItemRequest_EmptyProductId_IsInvalid() =>
        Assert.False(IsValid(new AddCartItemRequest { ProductId = Guid.Empty, Quantity = 1 }));

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(3, true)]
    public void SetQuantityRequest_QuantityMustBeWithinRange(int quantity, bool expected) =>
        Assert.Equal(expected, IsValid(new SetCartItemQuantityRequest { Quantity = quantity }));
}
