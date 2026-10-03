namespace OpenCsms.Api;

using OpenCsms.Application.Identity;
using OpenCsms.Contracts;

/// <summary>
/// The machine surface's bootstrap. A tenant registers here - the one anonymous management route -
/// and the answer carries the machine key once; every later management call carries that key in the
/// <c>X-Api-Key</c> header. Only the key's hash is stored, so the answer is the only time the raw
/// key exists.
/// </summary>
internal static class TenantEndpoints
{
    public static void MapTenantEndpoints(this IEndpointRouteBuilder api)
        => api.MapPost("/tenants", RegisterAsync).WithTags("Tenants")
            .Produces<TenantRegistrationResponse>(StatusCodes.Status201Created);

    private static async Task<IResult> RegisterAsync(
        RegisterTenantRequest request,
        TenantRegistration registration,
        CancellationToken cancellationToken)
    {
        try
        {
            var registered = await registration.RegisterAsync(
                new RegisterTenantCommand(request.Name),
                cancellationToken);
            return Results.Created(
                $"/api/tenants/{registered.Tenant.Id}",
                new TenantRegistrationResponse(
                    registered.Tenant.Id,
                    registered.Tenant.Name,
                    registered.ApiKey));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [exception.ParamName ?? "request"] = [exception.Message]
            });
        }
    }
}
