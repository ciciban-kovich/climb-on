using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Xunit;

namespace ClimbOn.Api.Tests;

// C45 for exceptions thrown inside Program's own pipeline. ErrorTestHost's endpoints sit in a startup
// filter, outside it, so an error writer added in Program.cs would answer here and not there.
public sealed class ProgramPipelineErrorTests
{
    public enum Failure
    {
        Unhandled,
        DatabaseUnreachable,
    }

    [Theory]
    [InlineData("Production", Failure.Unhandled, HttpStatusCode.InternalServerError, "internal_error")]
    [InlineData("Production", Failure.DatabaseUnreachable, HttpStatusCode.ServiceUnavailable, "database_unavailable")]
    [InlineData("Development", Failure.Unhandled, HttpStatusCode.InternalServerError, "internal_error")]
    [InlineData("Development", Failure.DatabaseUnreachable, HttpStatusCode.ServiceUnavailable, "database_unavailable")]
    public async Task Exception_in_program_endpoint_has_code(
        string environment, Failure failure, HttpStatusCode status, string code)
    {
        await using var factory = new ThrowingHealthFactory(environment, failure);
        using var client = factory.CreateClient();

        var (response, body) = await ErrorTestHost.SendAsync(client, new HttpRequestMessage(HttpMethod.Get, "/health"));

        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(code, body?.Code);
            Assert.False(string.IsNullOrWhiteSpace(body?.Message));
        }
    }

    // /health is mapped in Program.cs; its HealthCheckService is replaced by one that throws.
    private sealed class ThrowingHealthFactory(string environment, Failure failure) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureTestServices(services => services.AddSingleton<HealthCheckService>(new ThrowingHealthCheckService(failure)));
        }
    }

    private sealed class ThrowingHealthCheckService(Failure failure) : HealthCheckService
    {
        public override Task<HealthReport> CheckHealthAsync(
            Func<HealthCheckRegistration, bool>? predicate, CancellationToken cancellationToken = default) =>
            throw (failure switch
            {
                Failure.Unhandled => new InvalidOperationException("unhandled"),
                _ => (Exception)new NpgsqlException("unreachable"),
            });
    }
}
