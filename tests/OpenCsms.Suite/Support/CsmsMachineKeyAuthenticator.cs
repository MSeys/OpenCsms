namespace OpenCsms.Suite.Support;

using ProtoTest.Http;

/// <summary>
/// Attaches the test's machine credential to every REST request, so a journey reads and acts through
/// the tenant the provisioning attributes registered. A test that proves the refusal disables this
/// with <c>WithoutAuth()</c>; a request that must act as another tenant replaces it with that
/// tenant's <see cref="ProtoTest.Http.Authenticators.ApiKeyAuthenticator"/>. The header is in the
/// framework's default sensitive-header set, so its value never reaches the trace.
/// </summary>
public sealed class CsmsMachineKeyAuthenticator : IProtoHttpAuthenticator
{
    public ValueTask AuthenticateAsync(
        ProtoHttpAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Test.TryResolve<CsmsMachine>() is { } machine)
        {
            context.Request.Headers.TryAddWithoutValidation(CsmsMachine.HeaderName, machine.ApiKey);
        }

        return ValueTask.CompletedTask;
    }
}
