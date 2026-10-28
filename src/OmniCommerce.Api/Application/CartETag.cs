namespace OmniCommerce.Api.Application;

public enum IfMatchStatus
{
    Valid,
    Missing,
    Invalid,
}

// Strong entity-tag for a cart: the 8-byte SQL Server rowversion as 16 lowercase hex digits in
// quotes, e.g. "0000000000004e21". It is an encoded version, not a secret or an authentication token.
public static class CartETag
{
    private const int VersionBytes = 8;

    public static string Format(byte[] rowVersion)
    {
        if (rowVersion.Length != VersionBytes)
        {
            throw new ArgumentException($"A rowversion has exactly {VersionBytes} bytes.", nameof(rowVersion));
        }

        return $"\"{Convert.ToHexString(rowVersion).ToLowerInvariant()}\"";
    }

    // Accepts exactly one strong entity-tag. Weak tags (W/"..."), "*" and lists are unsupported.
    public static IfMatchStatus ParseIfMatch(IReadOnlyCollection<string> headerValues, out byte[] rowVersion)
    {
        rowVersion = [];

        if (headerValues.Count == 0)
        {
            return IfMatchStatus.Missing;
        }

        if (headerValues.Count > 1)
        {
            return IfMatchStatus.Invalid;
        }

        var value = headerValues.Single().Trim();
        if (value.Length != 2 * VersionBytes + 2 || value[0] != '"' || value[^1] != '"')
        {
            return IfMatchStatus.Invalid;
        }

        var hex = value.AsSpan(1, 2 * VersionBytes);
        foreach (var c in hex)
        {
            if (!Uri.IsHexDigit(c))
            {
                return IfMatchStatus.Invalid;
            }
        }

        rowVersion = Convert.FromHexString(hex);
        return IfMatchStatus.Valid;
    }
}
