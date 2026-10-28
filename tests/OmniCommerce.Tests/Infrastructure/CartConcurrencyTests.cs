using Microsoft.Extensions.Logging.Abstractions;
using OmniCommerce.Api.Application;
using OmniCommerce.Api.Domain;
using OmniCommerce.Api.Infrastructure;

namespace OmniCommerce.Tests.Infrastructure;

public class CartConcurrencyTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    private static readonly Guid Mug = Guid.Parse("a1000000-0000-0000-0000-000000000003");
    private static readonly Guid Tee = Guid.Parse("a1000000-0000-0000-0000-000000000001");

    // Each call gets its own DbContext, like separate HTTP requests do.
    private async Task<T> WithService<T>(Func<CartService, Task<T>> action)
    {
        await using var db = sql.CreateContext();
        return await action(new CartService(db, TimeProvider.System, NullLogger<CartService>.Instance));
    }

    private async Task<(Guid Id, byte[] Version)> NewCartAsync()
    {
        var created = await WithService(s => s.CreateAsync(default));
        return (created.Cart!.Id, created.RowVersion!);
    }

    private static AddCartItemRequest Add(Guid product, int quantity) => new() { ProductId = product, Quantity = quantity };

    [SqlServerFact]
    public async Task Mutation_WithCurrentVersion_SucceedsAndReturnsNewVersion()
    {
        var (id, v1) = await NewCartAsync();

        var result = await WithService(s => s.AddItemAsync(id, v1, Add(Mug, 2), default));

        Assert.Equal(CartOutcome.Success, result.Outcome);
        Assert.NotEqual(v1, result.RowVersion);
        Assert.Equal(2, Assert.Single(result.Cart!.Items).Quantity);

        var reread = await WithService(s => s.GetAsync(id, default));
        Assert.Equal(result.RowVersion, reread.RowVersion);
    }

    [SqlServerFact]
    public async Task Mutation_WithStaleVersion_IsRejectedAndChangesNothing()
    {
        var (id, v1) = await NewCartAsync();
        var v2 = (await WithService(s => s.AddItemAsync(id, v1, Add(Mug, 2), default))).RowVersion!;

        var stale = await WithService(s => s.AddItemAsync(id, v1, Add(Tee, 1), default));
        var staleSet = await WithService(s => s.SetItemQuantityAsync(id, v1, Mug, 9, default));
        var staleRemove = await WithService(s => s.RemoveItemAsync(id, v1, Mug, default));
        var staleClear = await WithService(s => s.ClearAsync(id, v1, default));

        Assert.All([stale, staleSet, staleRemove, staleClear], r => Assert.Equal(CartOutcome.PreconditionFailed, r.Outcome));

        var current = await WithService(s => s.GetAsync(id, default));
        Assert.Equal(v2, current.RowVersion);
        var line = Assert.Single(current.Cart!.Items);
        Assert.Equal((Mug, 2), (line.ProductId, line.Quantity));
    }

    [SqlServerFact]
    public async Task EveryMutationKind_BumpsTheCartVersion()
    {
        var (id, v) = await NewCartAsync();
        var versions = new List<byte[]> { v };

        async Task Step(Func<CartService, byte[], Task<CartResult>> action)
        {
            var result = await WithService(s => action(s, versions[^1]));
            Assert.Equal(CartOutcome.Success, result.Outcome);
            versions.Add(result.RowVersion!);
        }

        await Step((s, ver) => s.AddItemAsync(id, ver, Add(Mug, 1), default));
        await Step((s, ver) => s.SetItemQuantityAsync(id, ver, Mug, 4, default));
        await Step((s, ver) => s.AddItemAsync(id, ver, Add(Tee, 1), default));
        await Step((s, ver) => s.RemoveItemAsync(id, ver, Tee, default));
        await Step((s, ver) => s.ClearAsync(id, ver, default));

        Assert.Equal(versions.Count, versions.Select(Convert.ToHexString).Distinct().Count());
    }

    [SqlServerFact]
    public async Task RemovingMissingLine_IsNotFound_AndKeepsVersion()
    {
        var (id, v1) = await NewCartAsync();

        var result = await WithService(s => s.RemoveItemAsync(id, v1, Mug, default));

        Assert.Equal(CartOutcome.ItemNotFound, result.Outcome);
        Assert.Equal(v1, (await WithService(s => s.GetAsync(id, default))).RowVersion);
    }

    [SqlServerFact]
    public async Task RejectedBusinessRule_RollsBackTheCartRowUpdate()
    {
        var (id, v1) = await NewCartAsync();
        var before = await WithService(s => s.GetAsync(id, default));

        var unknownProduct = await WithService(s => s.AddItemAsync(id, v1, Add(Guid.NewGuid(), 1), default));
        var missingLine = await WithService(s => s.SetItemQuantityAsync(id, v1, Tee, 1, default));

        Assert.Equal(CartOutcome.ProductNotFound, unknownProduct.Outcome);
        Assert.Equal(CartOutcome.ItemNotFound, missingLine.Outcome);
        var after = await WithService(s => s.GetAsync(id, default));
        Assert.Equal(before.RowVersion, after.RowVersion);
        Assert.Equal(before.Cart!.UpdatedAt, after.Cart!.UpdatedAt);

        var full = await WithService(s => s.AddItemAsync(id, v1, Add(Mug, Cart.MaxItemQuantity), default));
        var overLimit = await WithService(s => s.AddItemAsync(id, full.RowVersion!, Add(Mug, 1), default));

        Assert.Equal(CartOutcome.QuantityLimitExceeded, overLimit.Outcome);
        Assert.Equal(full.RowVersion, (await WithService(s => s.GetAsync(id, default))).RowVersion);
    }

    [SqlServerFact]
    public async Task UnknownCart_IsNotFound_NotPreconditionFailed()
    {
        var result = await WithService(s => s.ClearAsync(Guid.NewGuid(), new byte[8], default));

        Assert.Equal(CartOutcome.CartNotFound, result.Outcome);
    }

    [SqlServerFact]
    public async Task TwoRequestsWithTheSameVersion_CannotBothCommit()
    {
        for (var round = 0; round < 20; round++)
        {
            var (id, v1) = await NewCartAsync();

            // Different lines, so only the cart version (not the unique index) can decide the race.
            using var start = new ManualResetEventSlim();
            var first = Task.Run(() => { start.Wait(); return WithService(s => s.AddItemAsync(id, v1, Add(Mug, 1), default)); });
            var second = Task.Run(() => { start.Wait(); return WithService(s => s.AddItemAsync(id, v1, Add(Tee, 1), default)); });
            start.Set();
            var results = await Task.WhenAll(first, second);

            Assert.Equal(1, results.Count(r => r.Outcome == CartOutcome.Success));
            Assert.Equal(1, results.Count(r => r.Outcome == CartOutcome.PreconditionFailed));

            var final = await WithService(s => s.GetAsync(id, default));
            Assert.Single(final.Cart!.Items);
            Assert.Equal(results.Single(r => r.IsSuccess).RowVersion, final.RowVersion);
        }
    }

    [SqlServerFact]
    public async Task TwoRequestsAddingTheSameProduct_WithTheSameVersion_CannotBothCommit()
    {
        for (var round = 0; round < 20; round++)
        {
            var (id, v1) = await NewCartAsync();

            using var start = new ManualResetEventSlim();
            var tasks = Enumerable.Range(0, 2)
                .Select(_ => Task.Run(() => { start.Wait(); return WithService(s => s.AddItemAsync(id, v1, Add(Mug, 3), default)); }))
                .ToList();
            start.Set();
            var results = await Task.WhenAll(tasks);

            Assert.Equal(1, results.Count(r => r.IsSuccess));
            Assert.Equal(1, results.Count(r => r.Outcome == CartOutcome.PreconditionFailed));
            var final = await WithService(s => s.GetAsync(id, default));
            Assert.Equal(3, Assert.Single(final.Cart!.Items).Quantity);
        }
    }
}
