using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Extism.Sdk;
using Microsoft.AspNetCore.Mvc;
using System.Data;


public record CodeSystem(string CanonicalUrl, string? CanonicalVersion, string? ResourceJson);
public record Db(string FileName, string Name, List<CodeSystem> CodeSystems);
public record Wasm(string FileName, CodeSystem CodeSystem, Plugin Plugin);



public class SqliteManager
{
    private readonly string _folderPath;
    private readonly string _connectionString;
    public ConcurrentDictionary<string, Db> Dbs { get; private set; }
    public ConcurrentDictionary<string, Wasm> Wasms { get; private set; }


    public SqliteManager(string folderPath, string connectionString)
    {
        _folderPath = folderPath;
        _connectionString = connectionString;
        Dbs = new ConcurrentDictionary<string, Db>();
        Wasms = new ConcurrentDictionary<string, Wasm>();
    }

    public async Task HandleFileChange(string filePath, WatcherChangeTypes changeType, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("SQLite manager file change");
        Console.WriteLine(filePath);
        Console.WriteLine(changeType);
        string dbName = Path.GetFileNameWithoutExtension(filePath);

        if (Path.GetExtension(filePath).Equals(".db", StringComparison.OrdinalIgnoreCase))
        {
            if ((changeType & WatcherChangeTypes.Deleted) != 0)
            {
                Dbs.TryRemove(dbName, out var removed);
                return;
            }
            try
            {

                using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {

                    Console.WriteLine($"changed dbfile {dbName}");
                    LoadCodeSystems(dbName);

                }
            }
            catch (IOException ex)
            {
                Task.Delay(200).ContinueWith(_ => HandleFileChange(filePath, changeType));
            }

        }
        else if (Path.GetExtension(filePath).Equals(".wasm", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string wasmName = Path.GetFileNameWithoutExtension(filePath);
                var manifest = new Manifest(new PathWasmSource(filePath));
                String? canonicalUrl = null;
                String? canonicalVersion = null;
                var plugin = new Plugin(manifest, new HostFunction[] {
                    HostFunction.FromMethod("db_lookup", IntPtr.Zero, (CurrentPlugin plugin, long reqOffset) =>
                    {
                        var key = plugin.ReadString(reqOffset);
                        Console.WriteLine($"Looking up key={key} on {canonicalUrl} {canonicalVersion}");
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
                }, withWasi: true);
                var metadataJson = plugin.Call("metadata", "");
                var codeSystemJson = System.Text.Json.JsonDocument.Parse(metadataJson);
                canonicalUrl = codeSystemJson.RootElement.GetProperty("url").GetString();
                canonicalVersion = codeSystemJson.RootElement.TryGetProperty("version", out var versionElement) ? versionElement.GetString() : null;
                var codeSystemRecord = new CodeSystem(canonicalUrl!, canonicalVersion, metadataJson);

                var wasmRecord = new Wasm(wasmName, codeSystemRecord, plugin);
                Wasms.AddOrUpdate(wasmName, wasmRecord, (_, __) => wasmRecord);
                Console.WriteLine("Loaded wasm");

                // print all entries in Wasms
                foreach (var entry in Wasms)
                {
                    Console.WriteLine(entry.Key);
                    Console.WriteLine(entry.Value.FileName);
                    Console.WriteLine(entry.Value.CodeSystem);
                }

                var output = plugin.Call("parse", @"{""code"":""en-US""}");
                Console.WriteLine(output);

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        return;
    }

    private void LoadCodeSystems(string dbFileNameBase)
    {
        Console.WriteLine($"Loading new CS {dbFileNameBase}");
        string dbFile = Path.Combine(_folderPath, $"{dbFileNameBase}.db");
        using (var connection = new SqliteConnection($"Data Source={dbFile}"))
        {
            connection.Open();
            using (var command = new SqliteCommand("SELECT canonical_url, canonical_version, resource_json FROM CodeSystems", connection))
            using (var reader = command.ExecuteReader())
            {
                var codeSystems = new List<CodeSystem>();
                while (reader.Read())
                {
                    var codeSystem = new CodeSystem(
                        CanonicalUrl: reader.GetString(0),
                        CanonicalVersion: reader.GetString(1),
                        ResourceJson: reader.GetString(2)
                    );
                    codeSystems.Add(codeSystem);
                }

                var dbName = Regex.Replace(dbFileNameBase, @"[^a-zA-Z0-9_]", "");



                Dbs.AddOrUpdate(dbFileNameBase,
                    new Db(Name: dbName, FileName: dbFileNameBase, CodeSystems: codeSystems),
                    (key, oldValue) => new Db(Name: dbName, FileName: dbFileNameBase, CodeSystems: codeSystems));
            }
            connection.Close();
        }
    }

    public Db? GetDbByCanonicalSystem(string canonicalUrl, string? canonicalVersion = null)
    {
        try
        {
            return Dbs.Values.First(db =>
                db.CodeSystems.Any(cs =>
                    cs.CanonicalUrl == canonicalUrl &&
                    (canonicalVersion == null || cs.CanonicalVersion == canonicalVersion)
                )
            );
        }
        catch
        {
            return null;
        }
    }


    public Wasm? GedPluginByCanonicalSystem(string canonicalUrl, string? canonicalVersion = null)
    {
        try
        {
            return Wasms.Values.First(db =>
                    db.CodeSystem.CanonicalUrl == canonicalUrl &&
                    (canonicalVersion == null || db.CodeSystem.CanonicalVersion == canonicalVersion)
                );
        }
        catch
        {
            return null;
        }
    }

    public Concept? QueryConcept(string code, string canonicalUrl, string? canonicalVersion = null)
    {
        var db = GetDbByCanonicalSystem(canonicalUrl, canonicalVersion);
        if (db == null)
        {
            return null;
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        AttachDatabase(connection, db);

        var query = @$"
        SELECT c.code, c.display, p.property_code, pt.type, p.target_value
        FROM {db.Name}.Concepts c
        LEFT JOIN {db.Name}.ConceptProperty p ON c.id = p.concept_id
        LEFT JOIN {db.Name}.PropertyTypes pt ON pt.code = p.property_code
        WHERE c.code = @code";

        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("@code", code);

        using var reader = command.ExecuteReader();
        if (!reader.HasRows)
        {
            return null;
        }

        var concept = new Concept { Code = code };
        while (reader.Read())
        {
            concept.Display ??= reader.GetString("display");
            AddProperty(concept, reader);
        }

        return concept;
    }

    private void AttachDatabase(SqliteConnection connection, Db db)
    {
        var attachCommand = $"ATTACH 'file:{Path.Combine(_folderPath, $"{db.FileName}.db")}?immutable=true' AS '{db.Name}';";
        using var command = new SqliteCommand(attachCommand, connection);
        command.ExecuteNonQuery();
    }

    private void AddProperty(Concept concept, SqliteDataReader reader)
    {
        if (reader.IsDBNull("property_code"))
        {
            return;
        }

        var property = new Property
        {
            Code = reader.GetString("property_code"),
            Value = CreateValue(reader)
        };

        concept.Properties.Add(property);
    }

    private ValueX CreateValue(SqliteDataReader reader)
    {
        return reader.GetString("type") switch
        {
            "code" => new ValueCode { Value = reader.GetString("target_value") },
            "coding" => new ValueCoding
            {
                Value = new Coding
                {
                    System = reader.GetString("target_value"),
                    Code = reader.GetString("target_value"),
                    Display = reader.GetString("value_coding_display")
                }
            },
            "string" => new ValueString { Value = reader.GetString("target_value") },
            "decimal" => new ValueDecimal { Value = reader.GetString("target_value") },
            _ => throw new Exception("Unknown property type")
        };
    }
    public async IAsyncEnumerable<Dictionary<string, object>> QueryAsync(string query, IReadOnlyDictionary<string, object> queryParams, List<string> dbNamesToAttach, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var MAX_CYCLES = 1_500_000_000;
        var CHECK_CYCLES = 1_000_000_000;
        var cid = new Random().Next();
        Console.WriteLine($"{cid} Channel  created on thread {Thread.CurrentThread.ManagedThreadId}");
        var channel = Channel.CreateBounded<Dictionary<string, object>>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        var start = DateTime.Now;
        Console.WriteLine($"Execute query {query}");

        bool budgetExceed = false;
        _ = Task.Run(async () =>
        {
            using var connection = new SqliteConnection(_connectionString);
            try
            {
                Console.WriteLine($"{cid} sqlite for  running on thread {Thread.CurrentThread.ManagedThreadId}");
                connection.Open();
                var elapsed_cycles = 0;

                SQLitePCL.raw.sqlite3_progress_handler(connection.Handle, CHECK_CYCLES, (_) =>
                {
                    elapsed_cycles += CHECK_CYCLES;
                    if (cancellationToken.IsCancellationRequested || elapsed_cycles >= MAX_CYCLES)
                    {
                        Console.WriteLine($"{cid} sqlite explicitly canceling");
                        budgetExceed = true;
                        return -1;
                    }
                    return 0;
                }, null);

                foreach (var dbName in dbNamesToAttach)
                {
                    string fileName = Dbs.Values.Where(db => db.Name == dbName).Select(db => db.FileName).First();
                    var attachCommand = $"ATTACH 'file:{Path.Combine(_folderPath, $"{fileName}.db")}?immutable=true' AS '{dbName}';";
                    Console.WriteLine($"Attach ${attachCommand}");
                    using var command = new SqliteCommand(attachCommand, connection);
                    command.ExecuteNonQuery();
                }

                using var queryCommand = new SqliteCommand(query, connection);
                foreach (var param in queryParams)
                {
                    queryCommand.Parameters.AddWithValue(param.Key, param.Value);
                }

                using var reader = queryCommand.ExecuteReader();
                while (reader.Read())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.GetValue(i);
                    }

                    await channel.Writer.WriteAsync(row, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw new Exception("Canceled for timeout");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                if (budgetExceed)
                {
                    throw new Exception("Canceled for exceeding computation budget");
                }
                else
                {
                    throw ex;
                }
            }
            finally
            {
                Console.WriteLine($"{cid} done sqlite for  running on thread {Thread.CurrentThread.ManagedThreadId}");
                foreach (var dbName in dbNamesToAttach)
                {
                    var attachCommand = $"DETACH '{dbName}';";
                    using (var command = new SqliteCommand(attachCommand, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
                connection.Close();
            }
        }, cancellationToken).ContinueWith(task =>
        {
            channel.Writer.Complete(task.Exception);
        });

        await foreach (var item in channel.Reader.ReadAllAsync(CancellationToken.None))
        {
            yield return item;
        }
        Console.WriteLine($"{cid} done channel for  running on thread {Thread.CurrentThread.ManagedThreadId}");
    }
}
