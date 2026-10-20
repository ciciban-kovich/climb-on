using ClimbOn.Application.Accounts;
using Microsoft.AspNetCore.Identity;

namespace ClimbOn.Infrastructure.Accounts;

// C42: ASP.NET Core Identity's hasher, used standalone. Its default format ignores the user argument.
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<IdentityPasswordHasher> hasher = new();

    public string Hash(string password) => hasher.HashPassword(this, password);
}
