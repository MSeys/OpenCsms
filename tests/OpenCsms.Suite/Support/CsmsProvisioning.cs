namespace OpenCsms.Suite.Support;

using OpenCsms.Contracts;
using OpenCsms.Domain;
using ProtoTest.Core;
using ProtoTest.Data;

/// <summary>
/// The per-test prerequisite orchestration every operator setup shares: register a tenant through
/// <c>POST /api/tenants</c>, then its tariff, station and dashboard account through the product's
/// front door, with the mechanics on ProtoTest.Data provisioners. The tenant's machine key is the
/// credential every one of those calls carries, and it stays in the test context for the test's own
/// management calls. Names arrive computed (unique per test), so reruns against a database that
/// outlives the test process keep their own rows. The API has no delete route, so provisioned rows
/// stay; uniqueness is what keeps tests independent.
/// </summary>
public static class CsmsProvisioning
{
    /// <summary>Registers the tenant and provisions its tariff, station and operator admin.</summary>
    public static async Task<CsmsOperator> ProvisionOperatorAsync(
        ProtoExecutionContext context,
        string tenantName,
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
        var tenant = await context.Data().For<RegisterTenantRequest>()
            .With(request => request.Name, tenantName)
            .CreateAsync<TenantRegistrationResponse>(cancellationToken);

        // The tenant's rows are provisioned under its own key. Provisioning a second tenant in the
        // same test restores the first tenant's credential afterwards, so the test keeps acting as
        // the operator its attribute registered.
        var previous = context.TryResolve<CsmsMachine>();
        context.SetContext(new CsmsMachine(tenant.TenantId, tenant.ApiKey));
        try
        {
            var (tariff, station) = await ProvisionTenantRowsAsync(
                context,
                chargePointId,
                stationName,
                tariffName,
                connectorCount,
                loginEmail,
                loginPassword,
                displayName,
                cancellationToken);
            return new CsmsOperator(
                tenant.TenantId,
                chargePointId,
                stationName,
                tariff.Id,
                station.Id,
                connectorCount,
                loginEmail,
                loginPassword,
                tenant.ApiKey);
        }
        finally
        {
            if (previous is not null)
            {
                context.SetContext(previous);
            }
        }
    }

    /// <summary>Provisions one dashboard account for the credential's tenant and returns the answered user.</summary>
    public static async ValueTask<UserResponse> ProvisionUserAsync(
        ProtoExecutionContext context,
        string email,
        string displayName,
        string password,
        string role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await context.Data().For<CreateUserRequest>()
            .With<string?>(request => request.Email, email)
            .With<string?>(request => request.DisplayName, displayName)
            .With<string?>(request => request.Password, password)
            .With<string?>(request => request.Role, role)
            .CreateAsync<UserResponse>(cancellationToken);
    }

    private static async Task<(TariffResponse Tariff, StationResponse Station)> ProvisionTenantRowsAsync(
        ProtoExecutionContext context,
        string chargePointId,
        string stationName,
        string tariffName,
        int connectorCount,
        string loginEmail,
        string loginPassword,
        string displayName,
        CancellationToken cancellationToken)
    {
        var tariff = await context.Data().For<RegisterTariffRequest>()
            .With(request => request.Name, tariffName)
            .With(request => request.EnergyPricePerKwh, 0.40m)
            .With(request => request.StartFee, 1.50m)
            .With(request => request.IdleFeePerHour, 2.00m)
            .With(request => request.IdleGracePeriod, (TimeSpan?)TimeSpan.FromMinutes(10))
            .CreateAsync<TariffResponse>(cancellationToken);

        var station = await context.Data().For<RegisterStationRequest>()
            .With(request => request.ChargePointId, chargePointId)
            .With(request => request.Name, stationName)
            .With(request => request.ConnectorCount, connectorCount)
            .With(request => request.TariffId, tariff.Id)
            .CreateAsync<StationResponse>(cancellationToken);

        await ProvisionUserAsync(
            context, loginEmail, displayName, loginPassword, UserRoles.Operator, cancellationToken);

        return (tariff, station);
    }
}
