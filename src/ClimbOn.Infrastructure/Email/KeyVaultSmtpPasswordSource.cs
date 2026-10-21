using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;

namespace ClimbOn.Infrastructure.Email;

// C50: the TEM key is the Key Vault secret email-smtp-password, read through the managed identity
// (user-assigned client ID from AZURE_CLIENT_ID). Nothing is read until a send needs it, so a missing
// vault or secret fails the send attempt, never startup. CachedSmtpPasswordSource caches the value.
public sealed class KeyVaultSmtpPasswordSource(IConfiguration configuration) : ISmtpPasswordSource
{
    public const string SecretName = "email-smtp-password";

    public const string VaultUriKey = "KeyVault:Uri";

    private readonly Lazy<SecretClient> client = new(() => new SecretClient(
        new Uri(configuration[VaultUriKey] ?? throw new InvalidOperationException($"{VaultUriKey} is not configured.")),
        new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = configuration["AZURE_CLIENT_ID"] })));

    public async Task<string> GetAsync(CancellationToken cancellationToken) =>
        (await client.Value.GetSecretAsync(SecretName, cancellationToken: cancellationToken)).Value.Value;
}
