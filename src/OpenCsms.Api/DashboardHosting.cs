namespace OpenCsms.Api;

using System.Net;
using Microsoft.Extensions.FileProviders;

/// <summary>
/// Serves the built operator dashboard from <c>Csms:Ui:Path</c> (default <c>../OpenCsms.Dashboard/dist</c>,
/// relative to the content root) at the site root: static files, then a fallback that answers the SPA's
/// own routes with <c>index.html</c>. The API, the OCPP edge and the tooling keep their namespaces -
/// an unmatched path under them is a 404, never a page. The .NET build never invokes Node: a missing
/// build answers with the command that produces it, and everything else keeps working untouched.
/// </summary>
public static class DashboardHosting
{
    public const string DefaultPath = "../OpenCsms.Dashboard/dist";
    public const string SettingKey = "Csms:Ui:Path";

    private static readonly string[] ReservedPrefixes = ["/api", "/ocpp", "/healthz", "/swagger"];

    public static void UseDashboard(this WebApplication app)
    {
        var configured = app.Configuration[SettingKey];
        var path = string.IsNullOrWhiteSpace(configured) ? DefaultPath : configured.Trim();
        var folder = Path.GetFullPath(
            Path.IsPathRooted(path) ? path : Path.Combine(app.Environment.ContentRootPath, path));

        if (File.Exists(Path.Combine(folder, "index.html")))
        {
            var provider = new PhysicalFileProvider(folder);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = provider });
            app.MapFallback(async context =>
            {
                if (IsDashboardPath(context.Request.Path))
                {
                    await context.Response.SendFileAsync(Path.Combine(folder, "index.html"));
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status404NotFound;
            });
            return;
        }

        // A folder without a build is an instruction, not an error: the API and the OCPP gateway are unaffected.
        app.MapFallback(async context =>
        {
            if (!IsDashboardPath(context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(NotBuiltPage(folder));
        });
    }

    private static bool IsDashboardPath(PathString path)
        => !ReservedPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    private static string NotBuiltPage(string folder) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>OpenCSMS dashboard · not built</title>
          <style>
            * { box-sizing: border-box; }
            body { margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 24px;
                   background: #eef2f1; color: #16232c;
                   font: 15px/1.6 "Segoe UI", system-ui, -apple-system, sans-serif; }
            main { max-width: 620px; background: #fff; border: 1px solid #d9e2e0; border-radius: 12px; padding: 40px; }
            h1 { margin: 0 0 8px; font-size: 22px; letter-spacing: -0.01em; }
            p { margin: 0 0 16px; }
            code, pre { font-family: ui-monospace, "Cascadia Mono", Consolas, monospace; font-size: 14px; }
            code { background: #eef2f1; border: 1px solid #d9e2e0; border-radius: 6px; padding: 1px 6px; }
            pre { background: #16232c; color: #eef2f1; border-radius: 8px; padding: 14px 16px; overflow-x: auto; }
            ol { padding-left: 20px; margin: 0; }
            li { margin-bottom: 6px; }
            .where { color: #5f7180; font-size: 13px; margin-top: 18px; }
          </style>
        </head>
        <body>
          <main data-testid="ui-not-built">
            <h1>Dashboard not built</h1>
            <p>The operator dashboard has no build at <code>{{WebUtility.HtmlEncode(folder)}}</code>.</p>
            <ol>
              <li>Open a terminal in <code>src/OpenCsms.Dashboard</code>.</li>
              <li><code>npm ci</code></li>
              <li><code>npm run build</code></li>
            </ol>
            <p class="where">Or run <code>pwsh eng/build-dashboard.ps1</code>. Set <code>{{SettingKey}}</code> to serve a build from somewhere else. The API under <code>/api</code> and the OCPP gateway under <code>/ocpp</code> are unaffected.</p>
          </main>
        </body>
        </html>
        """;
}
