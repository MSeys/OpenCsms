namespace OpenCsms.Infrastructure.Export;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenCsms.Application.Ports;

/// <summary>
/// The export registration: the application's export port over the ClosedXML writer. Called from
/// the infrastructure's one entry point, so a composition root still calls a single extension.
/// </summary>
internal static class ExportServiceCollectionExtensions
{
    public static IServiceCollection AddInvoiceExport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IInvoiceExportWriter, ClosedXmlInvoiceExportWriter>();
        return services;
    }
}
