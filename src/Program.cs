using System.CommandLine;
using ReadLine = System.ReadLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Sprache;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

using Extism.Sdk;

var manifest = new Manifest(new PathWasmSource("./bcp47.wasm"));
Plugin.ConfigureCustomLogging(LogLevel.Info);

using (var plugin = new Plugin(manifest, new HostFunction[] {
    HostFunction.FromMethod("db_lookup", IntPtr.Zero, (CurrentPlugin plugin, long reqOffset) =>
    {
        var key = plugin.ReadString(reqOffset);
        Console.WriteLine($"Looking up key={key}");
        var reqObject = System.Text.Json.JsonSerializer.Deserialize<LookupRequest>(key);
        Console.WriteLine($"Parsed key={System.Text.Json.JsonSerializer.Serialize(reqObject)}");
        if (reqObject?.Code == "en") {
            var resJson = System.Text.Json.JsonSerializer.Serialize(new LookupResponse { Concept = new Concept { Code = reqObject.Code, Properties = new List<Property> { new Property { Code = "en", Value = new ValueString {Value = "English"}} } } });
            return plugin.WriteString(resJson);
        } else  if (reqObject?.Code == "US") {
            var resJson = System.Text.Json.JsonSerializer.Serialize(new LookupResponse { Concept = new Concept { Code = reqObject.Code, Properties = new List<Property> { new Property { Code = "en", Value = new ValueString {Value = "USA"}} } } });
            return plugin.WriteString(resJson);
        } else {
            return plugin.WriteString(@"{""concept"": null}");
        }
    }),
 }, withWasi: true))
{

    var output = plugin.Call("parse", @"{""code"":""en-US-u-bad-one-seventeen-x-okpriv""}");
    Console.WriteLine(output);
    output = plugin.Call("parse", @"{""code"":""en-US-u-bad-one-sevenn-x-okpriv""}");
    Console.WriteLine(output);
    output = plugin.Call("parse", @"{""code"":""en-US-x-okpriv""}");
    Console.WriteLine(output);
}
Plugin.DrainCustomLogs(line => Console.WriteLine(line));

var rootCommand = new RootCommand("TermSqlite");
var dbFolder = new Option<string>(
    "--dbs",
    description: "Path to the folder containing SQLite files.",
    getDefaultValue: () => Directory.GetCurrentDirectory());
rootCommand.AddOption(dbFolder);

var ndjsonFolder = new Option<string?>(
    "--ndjsons",
    description: "Path to the folder containing .ndjson.gz files",
    getDefaultValue: () => null);

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";

var serializeOptions = new System.Text.Json.JsonSerializerOptions
{
    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    WriteIndented = true

};


rootCommand.AddOption(ndjsonFolder);

rootCommand.SetHandler((dbFolder, ndjsonFolder) =>
{
    string connectionString = "Data Source=file::memory:?immutable=true;Pooling=False";
    var sqliteManager = new SqliteManager(dbFolder, connectionString);
    var dbWatcherReady = new StartupWatcher(dbFolder, "*", TimeSpan.FromSeconds(.5), sqliteManager.HandleFileChange);
    var ndjsonWatcher = (ndjsonFolder != null) ? new NdjsonGzWatcher(ndjsonFolder, dbFolder) : null;

    var builder = WebApplication.CreateBuilder(args);
    // builder.Logging.Configure(LogLevel.Information);

    var app = builder.Build();
    Console.WriteLine($"Environment: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}");
    app.MapGet("/ValueSet/$vcl", (HttpContext httpContext) =>
    {
        var system = httpContext.Request.Query["system"].Single()!;
        var query = httpContext.Request.Query["query"].Single()!;

        var results = VclManager.ExecuteAsync(system, query, sqliteManager);

        return Results.Stream(async (stream) =>
        {
            try
            {
                await foreach (var concept in results)
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(concept, serializeOptions);
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(json + "\n"));
                    await stream.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                var errorJson = System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message });
                await stream.WriteAsync(Encoding.UTF8.GetBytes(errorJson + "\n"));
                await stream.FlushAsync();
                await Task.Run(() => httpContext.Abort());
            }

        }, "application/json");
    });

    app.MapGet("/", (HttpContext httpContext) =>
    {
        var response = new
        {
            status = "OK",
            huh = "Check out /ValueSet/$vcl?system=http://www.nlm.nih.gov/research/umls/rxnorm&query=*:tty=SBD,{{term=tylenol}}"
        };

        return Results.Json(response, serializeOptions, statusCode: 200);
    });


    if (!Console.IsInputRedirected)
    {
        Console.WriteLine("Enter a blank line to exit.");
        _ = Task.Run(() =>
        {
            app.Run($"http://0.0.0.0:{port}");
        });


        while (true)
        {
            string input = ReadLine.Read("> ");
            ReadLine.AddHistory(input);
            var r = sqliteManager.QueryConcept(input != "" ? input : "LP14082-9", "http://loinc.org"); 
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(r, serializeOptions));

            if (string.IsNullOrWhiteSpace(input))
                break;
        }
    }
    else
    {
        Console.WriteLine("Running without CLI interaction. Press Ctrl+C to exit.");
        app.Run($"http://0.0.0.0:{port}");
    }

}, dbFolder, ndjsonFolder);

await rootCommand.InvokeAsync(args);
