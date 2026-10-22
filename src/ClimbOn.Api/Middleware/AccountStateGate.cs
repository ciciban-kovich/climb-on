namespace ClimbOn.Api.Middleware;

// Design §2 'Request pipeline', after the session: a suspended account gets the S95 response, and a
// deactivated one may only cancel the deletion and log out (S39.3). Suspension and deletion arrive in M2;
// until then every account is active, so every request passes.
public sealed class AccountStateGate(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context) => next(context);
}
