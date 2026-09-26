namespace OpenCsms.Suite.Support;

using System.Net;
using OpenCsms.Contracts;
using ProtoTest.Core;
using ProtoTest.Data;
using ProtoTest.Rest;

/// <summary>
/// The one shape every prerequisite provisioner shares: POST the request through the product's
/// front door, require the created status and return the answered body with its identity. A
/// subclass names the route and the identity, so the three provisioners cannot drift from each
/// other. The route is the existing REST API; only the mechanics ride ProtoTest.Data.
/// </summary>
public abstract class CsmsApiProvisioner<TRequest, TResponse> : IProtoDataProvisioner<TRequest, TResponse>
{
    protected abstract string Url { get; }

    protected abstract string IdentityOf(TResponse response);

    public async ValueTask<ProtoDataProvisioningResult<TResponse>> CreateAsync(
        TRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(context);
        using var response = await context.Execution.Rest(CsmsTargets.Api)
            .Body(value)
            .PostAsync(Url, ct: cancellationToken);
        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        var created = response.ReadAsJson<TResponse>()
            ?? throw new InvalidOperationException(
                $"Registering {typeof(TResponse).Name} answered an empty body.");
        return new ProtoDataProvisioningResult<TResponse>(created, IdentityOf(created));
    }
}

/// <summary>Registers a tariff through <c>POST /api/tariffs</c>.</summary>
public sealed class TariffProvisioner : CsmsApiProvisioner<RegisterTariffRequest, TariffResponse>
{
    protected override string Url => "/api/tariffs";

    protected override string IdentityOf(TariffResponse response) => response.Id.ToString();
}

/// <summary>Registers a station through <c>POST /api/stations</c>.</summary>
public sealed class StationProvisioner : CsmsApiProvisioner<RegisterStationRequest, StationResponse>
{
    protected override string Url => "/api/stations";

    protected override string IdentityOf(StationResponse response) => response.Id.ToString();
}

/// <summary>
/// Provisions a dashboard account through <c>POST /api/users</c>. The login is the test-facing
/// identity; the password stays in the orchestration's locals, never in the trace identity.
/// </summary>
public sealed class UserProvisioner : CsmsApiProvisioner<CreateUserRequest, UserResponse>
{
    protected override string Url => "/api/users";

    protected override string IdentityOf(UserResponse response) => response.Email;
}
