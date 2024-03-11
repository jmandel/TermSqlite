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


public record CodeSystem(string CanonicalUrl, string? CanonicalVersion, string? ResourceJson);
public record Db(string FileName, string Name, List<CodeSystem> CodeSystems);


public class SqliteManager
{
    private readonly string _folderPath;
    private readonly string _connectionString;
    public ConcurrentDictionary<string, Db> Dbs { get; private set; }

    public SqliteManager(string folderPath, string connectionString)
    {
        _folderPath = folderPath;
        _connectionString = connectionString;
        Dbs = new ConcurrentDictionary<string, Db>();
    }

    public async Task HandleFileChange(string filePath, WatcherChangeTypes changeType, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("SQLite manager file change");
        Console.WriteLine(filePath);
        Console.WriteLine(changeType);
        string dbName = Path.GetFileNameWithoutExtension(filePath);
        if ((changeType & WatcherChangeTypes.Deleted) != 0)
        {
            Db? removed = null;
            Dbs.TryRemove(dbName, out removed);
            return;
        }

        if (Path.GetExtension(filePath).Equals(".db", StringComparison.OrdinalIgnoreCase))
        {
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
        try {
            return Dbs.Values.First(db =>
                db.CodeSystems.Any(cs =>
                    cs.CanonicalUrl == canonicalUrl &&
                    (canonicalVersion == null || cs.CanonicalVersion == canonicalVersion)
                )
            );
        } catch  {
            return null;
        }
    }

    public async IAsyncEnumerable<Dictionary<string, object>> QueryAsync(string query, IReadOnlyDictionary<string,object> queryParams, List<string> dbNamesToAttach, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
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
                foreach (var param in queryParams) {
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
                if (budgetExceed) {
                    throw new Exception("Canceled for exceeding computation budget");
                } else {
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
