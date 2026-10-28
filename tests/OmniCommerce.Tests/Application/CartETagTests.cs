using OmniCommerce.Api.Application;

namespace OmniCommerce.Tests.Application;

public class CartETagTests
{
    private static readonly byte[] Version = [0, 0, 0, 0, 0, 0, 0x4e, 0x21];

    [Fact]
    public void Format_ProducesQuotedLowercaseHexStrongTag()
    {
        var etag = CartETag.Format(Version);

        Assert.Equal("\"0000000000004e21\"", etag);
        Assert.False(etag.StartsWith("W/"));
    }

    [Fact]
    public void Format_WrongLength_Throws() =>
        Assert.Throws<ArgumentException>(() => CartETag.Format([1, 2, 3]));

    [Fact]
    public void Parse_RoundTripsFormattedValue()
    {
        var status = CartETag.ParseIfMatch([CartETag.Format(Version)], out var parsed);

        Assert.Equal(IfMatchStatus.Valid, status);
        Assert.Equal(Version, parsed);
    }

    [Theory]
    [InlineData("\"0000000000004E21\"")]
    [InlineData("  \"0000000000004e21\"  ")]
    public void Parse_AcceptsUppercaseHexAndSurroundingWhitespace(string header) =>
        Assert.Equal(IfMatchStatus.Valid, CartETag.ParseIfMatch([header], out _));

    [Fact]
    public void Parse_NoHeader_IsMissing() =>
        Assert.Equal(IfMatchStatus.Missing, CartETag.ParseIfMatch([], out _));

    [Theory]
    [InlineData("")]
    [InlineData("0000000000004e21")]                 // unquoted
    [InlineData("\"0000000000004e21")]               // unterminated
    [InlineData("\"4e21\"")]                         // too short
    [InlineData("\"0000000000004e2100\"")]           // too long
    [InlineData("\"000000000000zzzz\"")]             // not hex
    [InlineData("W/\"0000000000004e21\"")]           // weak validators are not allowed for If-Match
    [InlineData("*")]                                // wildcard unsupported
    [InlineData("\"0000000000004e21\", \"0000000000004e22\"")]  // list unsupported
    public void Parse_MalformedOrUnsupported_IsInvalid(string header) =>
        Assert.Equal(IfMatchStatus.Invalid, CartETag.ParseIfMatch([header], out _));

    [Fact]
    public void Parse_RepeatedHeaderLines_IsInvalid() =>
        Assert.Equal(
            IfMatchStatus.Invalid,
            CartETag.ParseIfMatch(["\"0000000000004e21\"", "\"0000000000004e21\""], out _));
}
