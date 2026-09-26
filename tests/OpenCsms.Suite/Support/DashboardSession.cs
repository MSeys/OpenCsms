namespace OpenCsms.Suite.Support;

using System.Net;
using ProtoTest.Core;
using ProtoTest.Rest;

/// <summary>
/// Signs the REST client in through the product's own endpoint, exactly as the SPA does, and fetches
/// the anti-forgery token the dashboard's cookie mutations carry. The cookie and the token live on
/// the test's client, so a test signs in once and then reads and mutates.
/// </summary>
public static class DashboardSession
{
    /// <summary>Signs in with the account's email and password; the cookie stays on the client.</summary>
    public static async Task SignInAsync(string email, string password)
    {
        using var response = await Proto.Context.Rest()
            .Body(new { email, password })
            .PostAsync("/api/auth/sign-in");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }

    /// <summary>Fetches the anti-forgery request token for the signed-in session.</summary>
    public static async Task<string> AntiforgeryTokenAsync()
    {
        using var response = await Proto.Context.Rest().GetAsync("/api/auth/xsrf");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        return response.ReadAsJson<XsrfTokenResponse>()?.RequestToken
            ?? throw new InvalidOperationException("The anti-forgery endpoint answered an empty body.");
    }

    private sealed record XsrfTokenResponse(string RequestToken);
}
