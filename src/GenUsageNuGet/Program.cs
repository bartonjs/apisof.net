using Microsoft.Extensions.Hosting;
using Terrajobst.ApiCatalog.ActionsRunner;

var builder = ConsoleHost.CreateApplicationBuilder();

builder.AddApisOfDotNetPathProvider();
builder.AddApisOfDotNetStore();
builder.AddScratchFileProvider();
builder.AddMainWithCommands();

var consoleCts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    if (eventArgs.SpecialKey == ConsoleSpecialKey.ControlC)
    {
        Console.WriteLine("Ctrl+C, cancelling.");
        eventArgs.Cancel = true;
        consoleCts.Cancel();
    }
};

var app = builder.Build();
await app.RunAsync(consoleCts.Token);
