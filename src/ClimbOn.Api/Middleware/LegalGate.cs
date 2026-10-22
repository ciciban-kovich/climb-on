namespace ClimbOn.Api.Middleware;

// Design §2 'Request pipeline', after the account-state gate: an account that has not accepted or acknowledged
// the current terms and privacy policy is refused, except for the requests S113 allows. Every account accepts the
// current versions at registration, and publishing a new version arrives in M2 (S114); until then every request passes.
public sealed class LegalGate(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context) => next(context);
}
