namespace ClimbOn.Infrastructure.Email;

public interface ISmtpPasswordSource
{
    Task<string> GetAsync(CancellationToken cancellationToken);
}
