using System.CommandLine;
using ReadLine = System.ReadLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Sprache;
using System.Text;

var rootCommand = new RootCommand("TermSql");
var dbFolder = new Option<string>(
    "--dbs",
    description: "Path to the folder containing SQLite files.",
    getDefaultValue: () => Directory.GetCurrentDirectory());
rootCommand.AddOption(dbFolder);

var ndjsonFolder = new Option<string>(
    "--ndjsons",
    description: "Path to the folder containing .ndjson.gz files",
    getDefaultValue: () => null);

var serializeOptions = new System.Text.Json.JsonSerializerOptions
{
    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
};


rootCommand.AddOption(ndjsonFolder);

rootCommand.SetHandler(async (dbFolder, ndjsonFolder) =>
{
    string connectionString = "Data Source=file::memory:?immutable=true;Pooling=False";
    var sqliteManager = new SqliteManager(dbFolder, connectionString);
    var dbWatcherReady = new StartupWatcher(dbFolder, "*.db", TimeSpan.FromSeconds(.5), sqliteManager.HandleFileChange);
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


    app.MapGet("/", async (HttpContext httpContext) =>
    {

        var dbNamesToAttach = sqliteManager.Dbs.Keys.ToList(); // Adjust the logic to select DBs as needed
        var randomCodeSystems = sqliteManager.Dbs.OrderBy(x => Guid.NewGuid()).Take(2).ToList();
        var randomDbs = randomCodeSystems.Select(dbentry => dbentry.Value).DistinctBy(db => db.Name).ToList();

        // Console.WriteLine("Code Systems:");
        foreach (var cs in randomCodeSystems)
        {
            // Console.WriteLine($"codesytstem: {cs}");
        }

        string query = string.Join(" UNION ", randomDbs.Select(db => $"SELECT * FROM {db.Name}.Concepts "));
        // Console.WriteLine($"QQ {query}");
        var dbNamesToAttach2 = randomDbs.Select(db => db.Name).ToList();

        httpContext.Response.StatusCode = 200;
        httpContext.Response.ContentType = "application/json";

        // create cancelation token
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromMilliseconds(5000));
        CancellationToken cancellationToken = cancellationTokenSource.Token;
        await using var streamWriter = new StreamWriter(httpContext.Response.Body);
        try
        {
            await foreach (var row in sqliteManager.QueryAsync(query, new Dictionary<string, object>(), dbNamesToAttach2, cancellationToken))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(row);
                await streamWriter.WriteLineAsync(json);
                await streamWriter.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            var errorJson = System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message });
            await streamWriter.WriteLineAsync(errorJson);
            await streamWriter.FlushAsync();
        }
    });
    Task.Run(() =>
    {
        app.Run();
    });

    Console.WriteLine("Enter a blank line to exit.");
    while (true)
    {

        string input = ReadLine.Read("> ");
        ReadLine.AddHistory(input);


        if (string.IsNullOrWhiteSpace(input))
            break;

        if (sqliteManager.Dbs.Count >= 1)
        {
            var randomCodeSystems = sqliteManager.Dbs.OrderBy(x => Guid.NewGuid()).Take(2).ToList();
            var randomDbs = randomCodeSystems.Select(dbentry => dbentry.Value).DistinctBy(db => db.Name).ToList();

            Console.WriteLine("DBS");
            foreach (var d in randomDbs)
            {
                Console.WriteLine($"dbfile: {d.Name}");
            }

            Console.WriteLine("Code Systems:");
            foreach (var cs in randomCodeSystems)
            {
                Console.WriteLine($"codesytstem: {cs}");
                Console.WriteLine($"{String.Join(", ", cs.Value.CodeSystems.Select(x => x.CanonicalUrl))}");
            }

            string query = "select * from (" + string.Join(" UNION ", randomDbs.Select(db => $"SELECT * FROM {db.Name}.Concepts")) + ") order by RANDOM() limit 10";

            Console.WriteLine(query);
            var dbNamesToAttach = randomDbs.Select(db => db.Name).ToList();
            Console.WriteLine($"Querying databases: {string.Join(", ", dbNamesToAttach)}");
            Console.WriteLine($"query from thread {Environment.CurrentManagedThreadId}");
            var results = sqliteManager.QueryAsync(query, new Dictionary<string, object>(), dbNamesToAttach);

            Console.WriteLine("Results:");
            await foreach (var row in results)
            {
                Console.WriteLine($"ID: {row["id"]}, Code: {row["code"]}, Display: {row["display"]}");
            }
        }
        else
        {
            Console.WriteLine("At least one codesystem required.");
        }

        Console.WriteLine();
    }
}, dbFolder, ndjsonFolder);

await rootCommand.InvokeAsync(args);