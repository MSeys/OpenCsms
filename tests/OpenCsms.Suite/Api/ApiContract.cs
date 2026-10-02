namespace OpenCsms.Suite.Api;

using System.Net;
using System.Text.Json;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

/// <summary>
/// The committed OpenAPI document is the API's contract and the source of its coverage report, so it
/// must list exactly the paths the API serves. A change that adds or removes an endpoint regenerates it.
/// </summary>
[Application(CsmsTargets.Api)]
public sealed class ApiContract
{
    [ProtoTest]
    public async Task TheCommittedContractListsEveryPathTheApiServes()
    {
        using var response = await Proto.Context.Rest().GetAsync("/swagger/v1/swagger.json");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);

        var served = Paths(response.Content);
        var committed = Paths(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "opencsms.openapi.json")));

        Assert.That(served, Is.EquivalentTo(committed),
            "regenerate tests/OpenCsms.Suite/opencsms.openapi.json from /swagger/v1/swagger.json");
    }

    private static string[] Paths(string document)
    {
        using var json = JsonDocument.Parse(document);
        return [.. json.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name)];
    }
}
