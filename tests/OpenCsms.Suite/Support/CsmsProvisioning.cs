namespace OpenCsms.Suite.Support;

using OpenCsms.Contracts;
using OpenCsms.Domain;
using ProtoTest.Core;
using ProtoTest.Data;

/// <summary>
/// The per-test prerequisite orchestration every operator setup shares: tariff, station and
/// dashboard account through the product's front door, with the mechanics on ProtoTest.Data
/// provisioners. Names arrive computed (unique per test), so reruns against a database that
/// outlives the test process keep their own rows. The API has no delete route, so provisioned
/// rows stay; uniqueness is what keeps tests independent.
/// </summary>
public static class CsmsProvisioning
{
    /// <summary>Provisions the tariff, station and operator admin, and returns the operator the test reads.</summary>
    public static async Task<CsmsOperator> ProvisionOperatorAsync(
        ProtoExecutionContext context,
        string tenantId,
        string chargePointId,
        string stationName,
        string tariffName,
        int connectorCount,
        string loginEmail,
        string loginPassword,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tariff = await context.Data().For<RegisterTariffRequest>()
            .With(request => request.TenantId, tenantId)
            .With(request => request.Name, tariffName)
            .With(request => request.EnergyPricePerKwh, 0.40m)
            .With(request => request.StartFee, 1.50m)
            .With(request => request.IdleFeePerHour, 2.00m)
            .With(request => request.IdleGracePeriod, (TimeSpan?)TimeSpan.FromMinutes(10))
            .CreateAsync<TariffResponse>(cancellationToken);

        var station = await context.Data().For<RegisterStationRequest>()
            .With(request => request.TenantId, tenantId)
            .With(request => request.ChargePointId, chargePointId)
            .With(request => request.Name, stationName)
            .With(request => request.ConnectorCount, connectorCount)
            .With(request => request.TariffId, tariff.Id)
            .CreateAsync<StationResponse>(cancellationToken);

        await ProvisionUserAsync(
            context, tenantId, loginEmail, displayName, loginPassword, UserRoles.Operator, cancellationToken);

        return new CsmsOperator(
            tenantId,
            chargePointId,
            stationName,
            tariff.Id,
            station.Id,
            connectorCount,
            loginEmail,
            loginPassword);
    }

    /// <summary>Provisions one dashboard account and returns the answered user.</summary>
    public static async ValueTask<UserResponse> ProvisionUserAsync(
        ProtoExecutionContext context,
        string tenantId,
        string email,
        string displayName,
        string password,
        string role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await context.Data().For<CreateUserRequest>()
            .With<string?>(request => request.TenantId, tenantId)
            .With<string?>(request => request.Email, email)
            .With<string?>(request => request.DisplayName, displayName)
            .With<string?>(request => request.Password, password)
            .With<string?>(request => request.Role, role)
            .CreateAsync<UserResponse>(cancellationToken);
    }
}
