namespace OpenCsms.Suite.Web;

using OpenCsms.Domain;
using OpenCsms.Suite.Support;
using ProtoTest.Web;

/// <summary>The bounded waits the dashboard journeys give a screen or a fact to settle.</summary>
public static class OpenCsmsDashboard
{
    /// <summary>How long a screen or fact may take to appear or change.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);
}

/// <summary>
/// The dashboard shell every signed-in screen renders: brand, the session facts, sign-out and the
/// navigation. The public status page shares the shell but has no signed-in session, so it exposes
/// the sign-in link instead.
/// </summary>
public abstract class DashboardPage : WebPage
{
    public WebElement Brand => Element(By.TestId("brand"));

    public WebElement SessionUser => Element(By.TestId("session-user"));

    public WebElement SessionTenant => Element(By.TestId("session-tenant"));

    public WebElement SessionRole => Element(By.TestId("session-role"));

    public WebElement SignOut => Element(By.TestId("sign-out"));

    public WebElement SignInLink => Element(By.TestId("sign-in-link"));

    public WebElement NavStations => Element(By.TestId("nav-stations"));

    public WebElement NavInvoices => Element(By.TestId("nav-invoices"));

    public WebElement NavTariffs => Element(By.TestId("nav-tariffs"));

    public WebElement NavStatus => Element(By.TestId("nav-status"));
}

/// <summary>The sign-in screen: the account's email and password exchanged for the cookie session.</summary>
public sealed class SignInPage : DashboardPage
{
    public WebElement Page => Element(By.TestId("sign-in-page"));

    public WebElement Email => Element(By.TestId("sign-in-email"));

    public WebElement Password => Element(By.TestId("sign-in-password"));

    public WebElement Submit => Element(By.TestId("sign-in-submit"));

    public WebElement Error => Element(By.TestId("sign-in-error"));
}

/// <summary>The operator's stations, each row linking to the station's sessions.</summary>
public sealed class StationsPage : DashboardPage
{
    public WebElement Page => Element(By.TestId("stations-page"));

    public WebElement Title => Element(By.TestId("stations-title"));

    public WebElement Empty => Element(By.TestId("stations-empty"));

    public WebComponentCollection<StationRow> Rows => Components<StationRow>(By.TestId("station-row"));

    /// <summary>The one row carrying the given station name; more or fewer than one is an error.</summary>
    public StationRow Station(string name) => Rows.Matching(By.HasText(name), $"Station[{name}]");
}

public sealed class StationRow : WebComponent
{
    public WebElement Link => Element(By.TestId("station-link"));

    public WebElement ChargePoint => Element(By.TestId("station-charge-point"));

    public WebElement Connectors => Element(By.TestId("station-connectors"));

    public WebElement LastSeen => Element(By.TestId("station-last-seen"));
}

/// <summary>One station and its sessions, newest first.</summary>
public sealed class StationDetailPage : DashboardPage
{
    public WebElement Page => Element(By.TestId("station-page"));

    public WebElement Title => Element(By.TestId("station-title"));

    public WebElement ChargePoint => Element(By.TestId("station-charge-point"));

    public WebElement Connectors => Element(By.TestId("station-connectors"));

    public WebElement LastSeen => Element(By.TestId("station-last-seen"));

    public WebElement Back => Element(By.TestId("station-back"));

    public WebElement StationError => Element(By.TestId("station-error"));// The failed load panel.

    public WebElement SessionsEmpty => Element(By.TestId("sessions-empty"));

    public WebElement SessionsTable => Element(By.TestId("sessions-table"));

    public WebComponentCollection<SessionRow> Sessions => Components<SessionRow>(By.TestId("session-row"));

    public WebElement RemoteIdTag => Element(By.TestId("remote-id-tag"));

    public WebElement RemoteConnector => Element(By.TestId("remote-connector"));

    public WebElement RemoteStart => Element(By.TestId("remote-start"));

    public WebElement RemoteStartResult => Element(By.TestId("remote-start-result"));

    public WebElement RemoteStartError => Element(By.TestId("remote-start-error"));

    public WebElement StopResult => Element(By.TestId("session-stop-result"));

    public WebElement StopError => Element(By.TestId("session-stop-error"));
}

public sealed class SessionRow : WebComponent
{
    public WebElement Connector => Element(By.TestId("session-connector"));

    public WebElement Started => Element(By.TestId("session-started"));

    public WebElement Ended => Element(By.TestId("session-ended"));

    public WebElement Energy => Element(By.TestId("session-energy"));

    public WebElement State => Element(By.TestId("session-state"));

    public WebElement Invoice => Element(By.TestId("session-invoice"));

    public WebElement InvoiceNone => Element(By.TestId("session-invoice-none"));

    /// <summary>The operator's stop button; rendered for open sessions of the operator role only.</summary>
    public WebElement Stop => Element(By.TestId("session-stop"));
}

/// <summary>The operator's invoices, newest first, with the monthly export download.</summary>
public sealed class InvoicesPage : DashboardPage
{
    public WebElement Page => Element(By.TestId("invoices-page"));

    public WebElement Title => Element(By.TestId("invoices-title"));

    public WebElement Empty => Element(By.TestId("invoices-empty"));

    public WebComponentCollection<InvoiceRow> Rows => Components<InvoiceRow>(By.TestId("invoice-row"));

    public WebElement ExportMonth => Element(By.TestId("export-month"));

    public WebElement ExportDownload => Element(By.TestId("export-download"));

    public WebElement ExportError => Element(By.TestId("export-error"));
}

public sealed class InvoiceRow : WebComponent
{
    public WebElement Link => Element(By.TestId("invoice-link"));

    public WebElement Session => Element(By.TestId("invoice-session"));

    public WebElement Energy => Element(By.TestId("invoice-energy"));

    public WebElement Total => Element(By.TestId("invoice-total"));
}

/// <summary>The operator's tariffs; the edit form below renders for the operator role only.</summary>
public sealed class TariffsPage : DashboardPage
{
    public WebElement Page => Element(By.TestId("tariffs-page"));

    public WebElement Title => Element(By.TestId("tariffs-title"));

    public WebElement Empty => Element(By.TestId("tariffs-empty"));

    public WebComponentCollection<TariffRow> Rows => Components<TariffRow>(By.TestId("tariff-row"));

    /// <summary>The first row's edit button; absent for the viewer role.</summary>
    public WebElement FirstEditButton => Element(By.TestId("tariff-edit"));

    /// <summary>The read-only note viewers see instead of the edit buttons.</summary>
    public WebElement ReadonlyNote => Element(By.TestId("tariffs-readonly"));

    public WebElement EditForm => Element(By.TestId("tariff-form"));

    public WebElement EnergyInput => Element(By.TestId("tariff-energy-input"));

    public WebElement StartInput => Element(By.TestId("tariff-start-input"));

    public WebElement IdleInput => Element(By.TestId("tariff-idle-input"));

    public WebElement GraceInput => Element(By.TestId("tariff-grace-input"));

    public WebElement Save => Element(By.TestId("tariff-save"));

    public WebElement Cancel => Element(By.TestId("tariff-cancel"));

    public WebElement FormError => Element(By.TestId("tariff-form-error"));
}

public sealed class TariffRow : WebComponent
{
    public WebElement Name => Element(By.TestId("tariff-name"));

    public WebElement Energy => Element(By.TestId("tariff-energy"));

    public WebElement StartFee => Element(By.TestId("tariff-start-fee"));

    public WebElement IdleFee => Element(By.TestId("tariff-idle-fee"));
}

/// <summary>One invoice as the billing worker calculated it: every calculation line.</summary>
public sealed class InvoiceDetailPage : DashboardPage
{
    public WebElement Page => Element(By.TestId("invoice-page"));

    public WebElement Title => Element(By.TestId("invoice-title"));

    public WebElement Issued => Element(By.TestId("invoice-issued"));

    public WebElement Session => Element(By.TestId("invoice-session"));

    public WebElement Energy => Element(By.TestId("invoice-energy"));

    public WebElement EnergyAmount => Element(By.TestId("invoice-energy-amount"));

    public WebElement StartFee => Element(By.TestId("invoice-start-fee"));

    public WebElement IdleHours => Element(By.TestId("invoice-idle-hours"));

    public WebElement IdleFee => Element(By.TestId("invoice-idle-fee"));

    public WebElement Total => Element(By.TestId("invoice-total"));
}

/// <summary>The public status page: no account, the same shell.</summary>
public sealed class StatusPage : WebPage
{
    public WebElement Page => Element(By.TestId("status-page"));

    public WebElement Title => Element(By.TestId("status-title"));

    public WebElement SignInLink => Element(By.TestId("sign-in-link"));

    public WebElement SessionUser => Element(By.TestId("session-user"));

    public WebComponentCollection<StatusStationCard> Stations => Components<StatusStationCard>(By.TestId("status-station"));

    /// <summary>The one card carrying the given station name; more or fewer than one is an error.</summary>
    public StatusStationCard Station(string name) => Stations.Matching(By.HasText(name), $"Station[{name}]");
}

public sealed class StatusStationCard : WebComponent
{
    public WebElement Name => Element(By.TestId("status-station-name"));

    public WebElement ChargePoint => Element(By.TestId("status-station-charge-point"));

    public WebElement LastSeen => Element(By.TestId("status-station-last-seen"));

    public WebElement NoConnectors => Element(By.TestId("status-connector-none"));

    public WebComponentCollection<ConnectorRow> Connectors => Components<ConnectorRow>(By.TestId("status-connector"));
}

public sealed class ConnectorRow : WebComponent
{
    public WebElement State => Element(By.TestId("status-connector-state"));
}

/// <summary>
/// Signs the browser in as the viewer <see cref="CsmsViewerAttribute"/> provisioned, and waits for
/// the shell's session facts to prove the cookie exchange landed with the viewer role.
/// </summary>
public sealed class CsmsViewerLogin : IWebLoginStrategy
{
    public async ValueTask LoginAsync(WebLoginContext context, CancellationToken cancellationToken = default)
    {
        var viewer = context.Execution.Resolve<CsmsViewer>();
        var page = context.Web.Page<SignInPage>();
        await page.OpenAsync("/sign-in", cancellationToken);
        await page.Page.Should.BeVisibleAsync(OpenCsmsDashboard.Wait, cancellationToken);
        await page.Flow("Sign in as the viewer")
            .Fill(signIn => signIn.Email, viewer.LoginEmail)
            .Fill(signIn => signIn.Password, viewer.LoginPassword)
            .Click(signIn => signIn.Submit)
            .RunAsync(cancellationToken);
        await page.SessionUser.Should.BeVisibleAsync(OpenCsmsDashboard.Wait, cancellationToken);
        await page.SessionRole.Should.HaveTextAsync(UserRoles.Viewer, OpenCsmsDashboard.Wait, cancellationToken);
    }
}
/// <summary>
/// Signs the browser in through the dashboard's own sign-in screen with the account
/// <see cref="OpenCsms.Suite.Support.CsmsOperatorAttribute"/> provisioned, and waits for the shell's
/// session facts to prove the cookie exchange landed.
/// </summary>
public sealed class CsmsOperatorLogin : IWebLoginStrategy
{
    public async ValueTask LoginAsync(WebLoginContext context, CancellationToken cancellationToken = default)
    {
        var op = context.Execution.Resolve<CsmsOperator>();
        var page = context.Web.Page<SignInPage>();
        await page.OpenAsync("/sign-in", cancellationToken);
        await page.Page.Should.BeVisibleAsync(OpenCsmsDashboard.Wait, cancellationToken);
        await page.Flow("Sign in as the operator")
            .Fill(signIn => signIn.Email, op.LoginEmail)
            .Fill(signIn => signIn.Password, op.LoginPassword)
            .Click(signIn => signIn.Submit)
            .RunAsync(cancellationToken);
        await page.SessionUser.Should.BeVisibleAsync(OpenCsmsDashboard.Wait, cancellationToken);
        await page.SessionRole.Should.HaveTextAsync(UserRoles.Operator, OpenCsmsDashboard.Wait, cancellationToken);
    }
}
