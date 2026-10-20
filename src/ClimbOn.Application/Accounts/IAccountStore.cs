using ClimbOn.Domain.Accounts;

namespace ClimbOn.Application.Accounts;

public interface IAccountStore
{
    // Stores the account with its legal acknowledgements, in one transaction.
    Task AddAsync(Account account, CancellationToken cancellationToken);
}
