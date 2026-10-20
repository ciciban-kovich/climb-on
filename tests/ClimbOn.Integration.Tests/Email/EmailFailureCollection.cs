using Xunit;

namespace ClimbOn.Integration.Tests.Email;

// Every host's meter provider sees every ClimbOn.Email meter in the process, so tests that count
// email_send_failed run one at a time, never beside another test that makes a send fail.
[CollectionDefinition(Name)]
public sealed class EmailFailureCollection
{
    public const string Name = "Email failures";
}
