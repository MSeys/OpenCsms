namespace OpenCsms.Suite.Support;

using ProtoTest.Http;

/// <summary>Attaches the test tenant's machine key to every REST request.</summary>
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
