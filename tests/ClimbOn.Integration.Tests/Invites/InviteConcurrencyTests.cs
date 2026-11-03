using System.Net;
using System.Net.Http.Json;
using ClimbOn.Api.Climber;
using ClimbOn.Domain.Invites;
using ClimbOn.Integration.Tests.Harness;
using Xunit;

namespace ClimbOn.Integration.Tests.Invites;

// Design §2 'Concurrency for pair and count rules': conflicting requests run truly in parallel against PostgreSQL at
// READ COMMITTED. A deadlock (40P01) or any other database failure answers 500 or 503, which every test here refuses.
public sealed class InviteConcurrencyTests(ApiFactory factory) : IntegrationTest(factory)
{
    // Each race runs on fresh climbers this many times, so an interleaving that one run happens to miss gets more
    // chances.
    private const int Rounds = 3;

    private readonly InviteClimbers climbers = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Spec", "S34")]
    public async Task Parallel_mutual_invites_create_one_connection()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var a = await climbers.ClimberAsync();
            var b = await climbers.ClimberAsync();

            var results = await AllAsync(climbers.InviteAsync(a, b.Id), climbers.InviteAsync(b, a.Id));

            Assert.All(results, result => Assert.Equal(HttpStatusCode.OK, result.Status));
            Assert.Equal(
                [InviteEndpoints.Connected, InviteEndpoints.Invited],
                results.Select(result => result.Body!.Outcome).Order(StringComparer.Ordinal));
            Assert.Equal(1, await climbers.ConnectionsAsync(a, b));
            Assert.Equal(0, await climbers.PendingAsync(a, b));
        }
    }

    [Fact]
    [Trait("Spec", "S31")]
    public async Task Eleven_parallel_invites_leave_ten_pending()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var a = await climbers.ClimberAsync();
            var others = await climbers.ClimbersAsync(InvitePolicy.MaxPendingSent + 1);

            var results = await AllAsync([.. others.Select(other => climbers.InviteAsync(a, other.Id))]);

            Assert.Equal(InvitePolicy.MaxPendingSent, results.Count(result => result.Status == HttpStatusCode.OK));
            var refused = Assert.Single(results, result => result.Status != HttpStatusCode.OK);
            Assert.Equal(HttpStatusCode.Conflict, refused.Status);
            Assert.Equal(InvitePolicy.LimitReachedCode, refused.Code);
            Assert.Equal(InvitePolicy.MaxPendingSent, await climbers.PendingSentAsync(a));
        }
    }

    [Fact]
    [Trait("Spec", "S31")]
    public async Task Parallel_invites_from_one_sender_never_deadlock()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var a = await climbers.ClimberAsync();
            var others = await climbers.ClimbersAsync(InvitePolicy.MaxPendingSent);

            var results = await AllAsync([.. others.Select(other => climbers.InviteAsync(a, other.Id))]);

            Assert.All(results, result => Assert.Equal(HttpStatusCode.OK, result.Status));
            Assert.Equal(InvitePolicy.MaxPendingSent, await climbers.PendingSentAsync(a));
        }
    }

    // b accepts a's invite while b also invites a, which S34 would turn into the same acceptance. Whichever runs
    // second finds the first's result: an invite no longer pending, or a connection.
    [Fact]
    [Trait("Spec", "S34")]
    [Trait("SpecPartial", "S7")]
    public async Task Parallel_accept_and_mutual_invite_create_one_connection()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var a = await climbers.ClimberAsync();
            var b = await climbers.ClimberAsync();
            var invite = (await climbers.InvitedAsync(a, b)).InviteId!.Value;

            var results = await AllAsync(climbers.AcceptAsync(b, invite), climbers.InviteAsync(b, a.Id));

            var (accept, mutual) = (results[0], results[1]);
            Assert.True(
                (accept.Status == HttpStatusCode.NoContent && mutual.Status == HttpStatusCode.Conflict
                    && mutual.Code == InvitePolicy.AlreadyConnectedCode)
                || (mutual.Status == HttpStatusCode.OK && mutual.Body!.Outcome == InviteEndpoints.Connected
                    && accept.Status == HttpStatusCode.Conflict && accept.Code == Invite.NotPendingCode),
                $"accept {accept.Status} {accept.Code}, invite {mutual.Status} {mutual.Code}");
            Assert.Equal(1, await climbers.ConnectionsAsync(a, b));
            Assert.Equal(0, await climbers.PendingAsync(a, b));
        }
    }

    // Starts every request before awaiting any, and reads each answer.
    private static async Task<Result[]> AllAsync(params Task<HttpResponseMessage>[] requests)
    {
        var responses = await Task.WhenAll(requests);
        var results = new Result[responses.Length];
        for (var i = 0; i < responses.Length; i++)
        {
            using var response = responses[i];
            results[i] = response.StatusCode switch
            {
                HttpStatusCode.OK => new Result(response.StatusCode, null, await response.Content.ReadFromJsonAsync<InviteResultResponse>(Ct)),
                HttpStatusCode.NoContent => new Result(response.StatusCode, null, null),
                _ => new Result(response.StatusCode, await InviteClimbers.CodeAsync(response), null),
            };
        }

        return results;
    }

    private sealed record Result(HttpStatusCode Status, string? Code, InviteResultResponse? Body);
}
