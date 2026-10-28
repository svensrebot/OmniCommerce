using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using OmniCommerce.Api.Domain;
using OmniCommerce.Api.Infrastructure;

namespace OmniCommerce.Tests.Infrastructure;

// Inspects the EF model only; building it never opens a connection, so no database is needed.
public class CommerceModelTests
{
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<CommerceDbContext>()
            .UseSqlServer("Server=unused;Database=unused")
            .Options;
        using var context = new CommerceDbContext(options);
        return context.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void CartRowVersion_IsSqlServerRowVersionConcurrencyToken()
    {
        var property = BuildModel().FindEntityType(typeof(Cart))!.FindProperty(nameof(Cart.RowVersion))!;

        Assert.True(property.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        Assert.Equal("rowversion", property.GetColumnType());
    }

    [Fact]
    public void CartItem_HasUniqueCartProductIndexAndPositiveQuantityCheck()
    {
        var entity = BuildModel().FindEntityType(typeof(CartItem))!;

        Assert.Contains(entity.GetIndexes(), i =>
            i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual([nameof(CartItem.CartId), nameof(CartItem.ProductId)]));
        Assert.Contains(entity.GetCheckConstraints(), c => c.Sql == "[Quantity] > 0");
    }

    [Fact]
    public void Product_SkuIsUnique_AndSeedIsDeterministic()
    {
        var entity = BuildModel().FindEntityType(typeof(Product))!;

        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(Product.Sku));

        var first = entity.GetSeedData().Select(s => s[nameof(Product.Id)]).OrderBy(x => x).ToList();
        var second = BuildModel().FindEntityType(typeof(Product))!.GetSeedData().Select(s => s[nameof(Product.Id)]).OrderBy(x => x).ToList();
        Assert.Equal(5, first.Count);
        Assert.Equal(first, second);
    }
}
