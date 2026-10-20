using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace ClimbOn.Application.Tokens;

// What a link token proves when it is used.
public enum LinkPurpose
{
    EmailConfirmation,
}

// A token as stored: only its SHA-256 hash, never the token itself.
public sealed record LinkToken(byte[] Hash, Guid AccountId, LinkPurpose Purpose, DateTimeOffset CreatedAt);

public interface ILinkTokenStore
{
    // Saved with the request's other changes, in one transaction (IAccountStore.AddAsync).
    void Add(LinkToken token);
}

// Design §3 'Unguessable links': 256-bit random tokens, base64url so they fit a URL fragment unescaped.
public static class LinkTokens
{
    private const int TokenBytes = 32;

    public static string Create() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
