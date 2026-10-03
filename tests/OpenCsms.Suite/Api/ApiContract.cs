namespace OpenCsms.Suite.Api;

using System.Net;
using System.Text.Json.Nodes;
using OpenCsms.Suite.Support;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

/// <summary>
/// The committed OpenAPI document is the API's contract and the source of its coverage report, so it
/// must be exactly the document the API serves: its paths and the response schemas the property
/// coverage reads. Run with OPENCSMS_WRITE_CONTRACT=1 to write the served document over the committed one.
/// </summary>
[Application(CsmsTargets.Api)]
public sealed class ApiContract
{
    private const string ContractFile = "opencsms.openapi.json";

    [ProtoTest]
    public async Task TheCommittedContractIsTheDocumentTheApiServes()
    {
        using var response = await Proto.Context.Rest().GetAsync("/swagger/v1/swagger.json");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);

        if (Environment.GetEnvironmentVariable("OPENCSMS_WRITE_CONTRACT") == "1")
        {
            await File.WriteAllTextAsync(SourceContract(), response.Content);
        }

        var served = JsonNode.Parse(response.Content);
        var committed = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, ContractFile)));

        Assert.That(JsonNode.DeepEquals(served, committed), Is.True,
            $"regenerate tests/OpenCsms.Suite/{ContractFile}: run this test with OPENCSMS_WRITE_CONTRACT=1");
    }

    // The committed file next to this suite's project, found from the test output folder.
    private static string SourceContract()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenCsms.Suite.csproj")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("The OpenCsms.Suite project folder was not found."),
            ContractFile);
    }
}
