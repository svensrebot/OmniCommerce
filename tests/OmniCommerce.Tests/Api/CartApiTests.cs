using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniCommerce.Api.Application;
using OmniCommerce.Tests.Infrastructure;

namespace OmniCommerce.Tests.Api;

// Real HTTP requests through the full ASP.NET Core pipeline against a real, throwaway SQL Server database.
public class CartApiTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    private const string Mug = "a1000000-0000-0000-0000-000000000003";
    private const string Tee = "a1000000-0000-0000-0000-000000000001";
    private const string Notebook = "a1000000-0000-0000-0000-000000000004";

    private HttpClient? _client;
    private HttpClient Client => _client ??= sql.CreateClient();

    public static TheoryData<string> MutationKinds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var kind in new[] { "add", "set", "remove", "clear" })
            {
                data.Add(kind);
            }

            return data;
        }
    }

    public static TheoryData<string, string> MalformedCases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var kind in new[] { "add", "set", "remove", "clear" })
            {
                foreach (var variant in new[] { "unquoted", "weak", "wildcard", "list", "too-short", "non-hex" })
                {
                    data.Add(kind, variant);
                }
            }

            return data;
        }
    }

    [SqlServerFact]
    public async Task CreateCart_Returns201WithLocationAndStrongETag()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/carts");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cart = await response.Content.ReadFromJsonAsync<CartResponse>();
        Assert.Empty(cart!.Items);
        Assert.EndsWith($"/api/carts/{cart.Id}", response.Headers.Location!.ToString());
        Assert.Matches("^\"[0-9a-f]{16}\"$", ETagOf(response));
    }

    [SqlServerFact]
    public async Task AddItem_WithCurrentETag_Returns200WithNewETagAndPersistsTheItem()
    {
        var (cart, etag) = await CreateAsync();

        var response = await SendAsync(HttpMethod.Post, $"/api/carts/{cart.Id}/items", etag, new { productId = Mug, quantity = 2 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newEtag = ETagOf(response);
        Assert.NotEqual(etag, newEtag);
        var body = await response.Content.ReadFromJsonAsync<CartResponse>();
        var line = Assert.Single(body!.Items);
        Assert.Equal(("MUG-CER-350", 2), (line.Sku, line.Quantity));

        var (reread, rereadEtag) = await GetAsync(cart.Id);
        Assert.Equal(newEtag, rereadEtag);
        Assert.Equal(2, Assert.Single(reread.Items).Quantity);
    }

    [SqlServerFact]
    public async Task AddingTheSameProductTwice_MergesTheQuantity()
    {
        var (id, etag) = await PopulatedCartAsync();

        var response = await SendAsync(HttpMethod.Post, $"/api/carts/{id}/items", etag, new { productId = Mug, quantity = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CartResponse>();
        Assert.Equal(5, Assert.Single(body!.Items).Quantity);
    }

    [SqlServerFact]
    public async Task SetRemoveAndClear_WithCurrentETag_SucceedAndEachReturnsANewETag()
    {
        var (id, etag) = await PopulatedCartAsync();
        var seen = new List<string> { etag };

        var set = await SendAsync(HttpMethod.Put, $"/api/carts/{id}/items/{Mug}", seen[^1], new { quantity = 7 });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(7, Assert.Single((await set.Content.ReadFromJsonAsync<CartResponse>())!.Items).Quantity);
        seen.Add(ETagOf(set));

        var add = await SendAsync(HttpMethod.Post, $"/api/carts/{id}/items", seen[^1], new { productId = Tee, quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        seen.Add(ETagOf(add));

        var remove = await SendAsync(HttpMethod.Delete, $"/api/carts/{id}/items/{Mug}", seen[^1]);
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Equal("TSHIRT-BLK-M", Assert.Single((await remove.Content.ReadFromJsonAsync<CartResponse>())!.Items).Sku);
        seen.Add(ETagOf(remove));

        var clear = await SendAsync(HttpMethod.Delete, $"/api/carts/{id}/items", seen[^1]);
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.Empty((await clear.Content.ReadFromJsonAsync<CartResponse>())!.Items);
        seen.Add(ETagOf(clear));

        Assert.Equal(seen.Count, seen.Distinct().Count());
        var (final, finalEtag) = await GetAsync(id);
        Assert.Empty(final.Items);
        Assert.Equal(seen[^1], finalEtag);
    }

    [SqlServerTheory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    public async Task InvalidQuantity_Returns400_AndLeavesTheCartUnchanged(int quantity)
    {
        var (id, etag) = await PopulatedCartAsync();

        var add = await SendAsync(HttpMethod.Post, $"/api/carts/{id}/items", etag, new { productId = Tee, quantity });
        var set = await SendAsync(HttpMethod.Put, $"/api/carts/{id}/items/{Mug}", etag, new { quantity });

        await AssertProblemAsync(add, HttpStatusCode.BadRequest);
        await AssertProblemAsync(set, HttpStatusCode.BadRequest);
        await AssertUnchangedAsync(id, etag);
    }

    [SqlServerFact]
    public async Task MissingOrEmptyProductId_Returns400_AndKeepsTheETag()
    {
        var (id, etag) = await PopulatedCartAsync();

        var missing = await SendAsync(HttpMethod.Post, $"/api/carts/{id}/items", etag, new { quantity = 1 });
        var empty = await SendAsync(HttpMethod.Post, $"/api/carts/{id}/items", etag, new { productId = Guid.Empty, quantity = 1 });

        await AssertProblemAsync(missing, HttpStatusCode.BadRequest);
        await AssertProblemAsync(empty, HttpStatusCode.BadRequest);
        await AssertUnchangedAsync(id, etag);
    }

    [SqlServerFact]
    public async Task MalformedJsonBody_Returns400_AndKeepsTheETag()
    {
        var (id, etag) = await PopulatedCartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/carts/{id}/items")
        {
            Content = new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await Client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        await AssertUnchangedAsync(id, etag);
    }

    [SqlServerFact]
    public async Task MissingQuantity_Returns400()
    {
        var (id, etag) = await PopulatedCartAsync();

        var response = await SendAsync(HttpMethod.Post, $"/api/carts/{id}/items", etag, new { productId = Tee });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        await AssertUnchangedAsync(id, etag);
    }

    [SqlServerTheory]
    [MemberData(nameof(MutationKinds))]
    public async Task MissingIfMatch_Returns428_AndLeavesTheCartUnchanged(string kind)
    {
        var (id, etag) = await PopulatedCartAsync();

        var response = await MutateAsync(kind, id, ifMatch: null);

        await AssertProblemAsync(response, HttpStatusCode.PreconditionRequired);
        await AssertUnchangedAsync(id, etag);
    }

    [SqlServerTheory]
    [MemberData(nameof(MalformedCases))]
    public async Task MalformedIfMatch_Returns400_AndLeavesTheCartUnchanged(string kind, string variant)
    {
        var (id, etag) = await PopulatedCartAsync();
        var header = variant switch
        {
            "unquoted" => etag.Trim('"'),
            "weak" => $"W/{etag}",
            "wildcard" => "*",
            "list" => $"{etag}, {etag}",
            "too-short" => "\"abcd\"",
            "non-hex" => "\"zzzzzzzzzzzzzzzz\"",
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };

        var response = await MutateAsync(kind, id, header);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        await AssertUnchangedAsync(id, etag);
    }

    [SqlServerTheory]
    [MemberData(nameof(MutationKinds))]
    public async Task StaleETag_Returns412_AndLeavesTheCartUnchanged(string kind)
    {
        var (cart, staleEtag) = await CreateAsync();
        var id = cart.Id;
        var first = await SendAsync(HttpMethod.Post, $"/api/carts/{id}/items", staleEtag, new { productId = Mug, quantity = 2 });
        var currentEtag = ETagOf(first);

        var response = await MutateAsync(kind, id, staleEtag);

        await AssertProblemAsync(response, HttpStatusCode.PreconditionFailed);
        Assert.False(response.Headers.Contains("ETag"));
        await AssertUnchangedAsync(id, currentEtag);
    }

    [SqlServerFact]
    public async Task AfterA412_RetryingWithTheFreshETagSucceeds()
    {
        var (id, staleEtag) = await PopulatedCartAsync();
        var (_, freshEtag) = await GetAsync(id);
        var other = await SendAsync(HttpMethod.Post, $"/api/carts/{id}/items", freshEtag, new { productId = Tee, quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);

        var rejected = await MutateAsync("add", id, staleEtag);
        var (_, currentEtag) = await GetAsync(id);
        var retried = await MutateAsync("add", id, currentEtag);

        Assert.Equal(HttpStatusCode.PreconditionFailed, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
    }

    [SqlServerFact]
    public async Task UnknownCart_Returns404()
    {
        var id = Guid.NewGuid();
        var validLookingEtag = "\"0000000000000001\"";

        await AssertProblemAsync(await SendAsync(HttpMethod.Get, $"/api/carts/{id}"), HttpStatusCode.NotFound);
        foreach (var kind in new[] { "add", "set", "remove", "clear" })
        {
            await AssertProblemAsync(await MutateAsync(kind, id, validLookingEtag), HttpStatusCode.NotFound);
        }
    }

    [SqlServerFact]
    public async Task RemovingALineThatIsNotInTheCart_Returns404_AndKeepsTheETag()
    {
        var (id, etag) = await PopulatedCartAsync();

        var response = await SendAsync(HttpMethod.Delete, $"/api/carts/{id}/items/{Notebook}", etag);

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
        await AssertUnchangedAsync(id, etag);
    }

    [SqlServerFact]
    public async Task UnknownProduct_Returns422_AndKeepsTheETag()
    {
        var (id, etag) = await PopulatedCartAsync();

        var response = await SendAsync(HttpMethod.Post, $"/api/carts/{id}/items", etag, new { productId = Guid.NewGuid(), quantity = 1 });

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity);
        await AssertUnchangedAsync(id, etag);
    }

    [SqlServerFact]
    public async Task TwoSimultaneousRequestsWithTheSameETag_OnlyOneCommits()
    {
        for (var round = 0; round < 10; round++)
        {
            var (id, etag) = await PopulatedCartAsync();

            using var start = new ManualResetEventSlim();
            var first = Task.Run(() => { start.Wait(); return SendAsync(HttpMethod.Post, $"/api/carts/{id}/items", etag, new { productId = Tee, quantity = 1 }); });
            var second = Task.Run(() => { start.Wait(); return SendAsync(HttpMethod.Post, $"/api/carts/{id}/items", etag, new { productId = Notebook, quantity = 1 }); });
            start.Set();
            var responses = await Task.WhenAll(first, second);

            Assert.Equal(
                [HttpStatusCode.OK, HttpStatusCode.PreconditionFailed],
                responses.Select(r => r.StatusCode).OrderBy(s => (int)s));

            var winner = responses.Single(r => r.StatusCode == HttpStatusCode.OK);
            var (final, finalEtag) = await GetAsync(id);
            Assert.Equal(2, final.Items.Count); // the pre-existing mug plus exactly one of the racing lines
            Assert.Equal(ETagOf(winner), finalEtag);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? ifMatch = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> MutateAsync(string kind, Guid id, string? ifMatch) => kind switch
    {
        "add" => SendAsync(HttpMethod.Post, $"/api/carts/{id}/items", ifMatch, new { productId = Tee, quantity = 1 }),
        "set" => SendAsync(HttpMethod.Put, $"/api/carts/{id}/items/{Mug}", ifMatch, new { quantity = 9 }),
        "remove" => SendAsync(HttpMethod.Delete, $"/api/carts/{id}/items/{Mug}", ifMatch),
        "clear" => SendAsync(HttpMethod.Delete, $"/api/carts/{id}/items", ifMatch),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string ETagOf(HttpResponseMessage response) => response.Headers.GetValues("ETag").Single();

    private async Task<(CartResponse Cart, string ETag)> CreateAsync()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/carts");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<CartResponse>())!, ETagOf(response));
    }

    private async Task<(CartResponse Cart, string ETag)> GetAsync(Guid id)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/carts/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<CartResponse>())!, ETagOf(response));
    }

    // A cart holding exactly one line (2 mugs), so any accepted mutation would visibly change it.
    private async Task<(Guid Id, string ETag)> PopulatedCartAsync()
    {
        var (cart, etag) = await CreateAsync();
        var response = await SendAsync(HttpMethod.Post, $"/api/carts/{cart.Id}/items", etag, new { productId = Mug, quantity = 2 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (cart.Id, ETagOf(response));
    }

    private async Task AssertUnchangedAsync(Guid id, string expectedEtag)
    {
        var (cart, etag) = await GetAsync(id);
        Assert.Equal(expectedEtag, etag);
        var line = Assert.Single(cart.Items);
        Assert.Equal((Guid.Parse(Mug), 2), (line.ProductId, line.Quantity));
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)expected, json.RootElement.GetProperty("status").GetInt32());
    }
}
