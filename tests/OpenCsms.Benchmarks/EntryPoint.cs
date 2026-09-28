// The benchmark's entry point is a real host-building Main, the same shape the API and the worker
// use: ProtoTest's worker resolver discovers an entry point by running it and capturing the host it
// builds, so the harness process behaves like a host rather than a plain console program.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenCsms.Benchmarks;

// The API entry point runs in this process through a host factory resolver with an empty argument
// list, and the harness directory is not where the API project sits; its content root is only the
// dashboard bundle. The factory checks this setting before its own discovery.
Environment.SetEnvironmentVariable("TEST_CONTENTROOT_OPENCSMS_API", AppContext.BaseDirectory);

var builder = Host.CreateApplicationBuilder();
builder.Services.AddHostedService<BenchmarkHostedService>();
builder.Build().Run();
