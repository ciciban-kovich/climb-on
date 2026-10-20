namespace ClimbOn.Application.Accounts;

// C42: a salted, slow hash; the plain-text password is never stored.
public interface IPasswordHasher
{
    string Hash(string password);
}
