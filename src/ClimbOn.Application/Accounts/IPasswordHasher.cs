namespace ClimbOn.Application.Accounts;

// C42: a salted, slow hash; the plain-text password is never stored.
public interface IPasswordHasher
{
    string Hash(string password);

    // True when password matches hash. A null hash (no such account) is never matched, but costs
    // the same work as a real one, so the response time does not tell the two apart.
    bool Verify(string? hash, string password);
}
