using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Timeglass.Api;

/// <summary>
/// The secret a caller has to show to use the API: a random value made once per run of the app,
/// never stored except in the runtime file (<see cref="ApiRuntimeFile"/>).
/// </summary>
public sealed class ApiAccessToken
{
    private const int ByteLength = 32;

    private readonly byte[] _bytes;

    private ApiAccessToken(string value)
    {
        Value = value;
        _bytes = Encoding.UTF8.GetBytes(value);
    }

    /// <summary>The token as URL-safe text (base64url, 43 characters).</summary>
    public string Value { get; }

    /// <summary>A new token from 256 random bits.</summary>
    public static ApiAccessToken Create() => new(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ByteLength)));

    /// <summary>A token with a known value, for tests and tools that read the runtime file.</summary>
    public static ApiAccessToken FromValue(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new ApiAccessToken(value);
    }

    /// <summary>True if <paramref name="candidate"/> is this token. Takes the same time however much of it matches.</summary>
    public bool Matches(string? candidate) =>
        candidate is not null && CryptographicOperations.FixedTimeEquals(_bytes, Encoding.UTF8.GetBytes(candidate));

    /// <summary>Not the value, so a token never ends up in a log by accident.</summary>
    public override string ToString() => "ApiAccessToken";
}
