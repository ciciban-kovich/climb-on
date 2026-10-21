using ClimbOn.Application.Accounts;
using Microsoft.AspNetCore.Identity;

namespace ClimbOn.Infrastructure.Accounts;

// C42: ASP.NET Core Identity's hasher, used standalone. Its default format ignores the user argument.
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<IdentityPasswordHasher> hasher = new();

    // Checked when there is no account: a hash of a random password, made with the same parameters as every stored one.
    private readonly Lazy<string> standIn;

    public IdentityPasswordHasher() => standIn = new Lazy<string>(() => Hash(Guid.NewGuid().ToString("N")));

    public string Hash(string password) => hasher.HashPassword(this, password);

    public bool Verify(string? hash, string password) =>
        hasher.VerifyHashedPassword(this, hash ?? standIn.Value, password) != PasswordVerificationResult.Failed
        && hash is not null;
}
