namespace OpenCsms.Api;

using Microsoft.AspNetCore.Antiforgery;

/// <summary>
/// The anti-forgery check for the dashboard's cookie-authenticated mutations. The SPA fetches a
/// request token from <c>GET /api/auth/xsrf</c> and sends it back in the <c>X-XSRF-TOKEN</c> header;
/// a mutation without a valid token is a 400, answered before the use case runs. Reads (and the
/// file download, which is a GET) need no token.
/// </summary>
internal sealed class ValidateAntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["The request is missing its anti-forgery token."]
            });
        }

        return await next(context);
    }
}
