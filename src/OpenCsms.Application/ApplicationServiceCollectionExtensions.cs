namespace OpenCsms.Application;

using Microsoft.Extensions.DependencyInjection;
using OpenCsms.Application.Billing;
using OpenCsms.Application.Catalog;
using OpenCsms.Application.ChargePoints;
using OpenCsms.Application.Commands;
using OpenCsms.Application.Identity;
using OpenCsms.Application.Sessions;

/// <summary>
/// The application layer's one registration: the use cases and the read surface of
/// <see cref="OpenCsms.Application"/>. The composition roots call it next to the infrastructure
/// extensions that own their ports (the store in the data extension, the publisher in the messaging
/// extension), so the Api and the billing worker resolve the same services over the same ports.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Registers the use cases; the ports come from the infrastructure extensions.</summary>
    public static IServiceCollection AddCsmsApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<TariffRegistration>();
        services.AddScoped<TariffEditing>();
        services.AddScoped<StationRegistration>();
        services.AddScoped<TariffReads>();
        services.AddScoped<StationReads>();
        services.AddScoped<ConnectorReads>();

        services.AddScoped<StationSeen>();
        services.AddScoped<ConnectorStatusReport>();
        services.AddScoped<TransactionStart>();
        services.AddScoped<TransactionMeterValues>();
        services.AddScoped<TransactionStop>();

        services.AddScoped<SessionStart>();
        services.AddScoped<SessionMeterValues>();
        services.AddScoped<SessionEnding>();
        services.AddScoped<SessionReads>();

        services.AddScoped<OperatorCommands>();
        services.AddScoped<InvoiceIssuance>();
        services.AddScoped<InvoiceReads>();
        services.AddScoped<MonthlyInvoiceExport>();

        services.AddScoped<UserRegistration>();
        services.AddScoped<UserAuthentication>();
        services.AddScoped<TenantRegistration>();
        services.AddScoped<TenantAuthentication>();
        return services;
    }
}
