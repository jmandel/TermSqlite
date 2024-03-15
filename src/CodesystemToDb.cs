using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using SQLitePCL;
public class LoadAssemblyFile
{
    public static string AsString(string resourceName)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        using (Stream? stream = assembly.GetManifestResourceStream(resourceName))
        {
            if (stream == null)
            {
                throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
            }

            using (StreamReader reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
public class DatabasePopulator
{

    public static void PopulateDatabase(string ndjsonGzFilename, string databaseFilename, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Populating db");
        string schema = LoadAssemblyFile.AsString("TermSqlite.sqlite.schema.sqlite");
        string scriptTemplate = LoadAssemblyFile.AsString("TermSqlite.sqlite.populate.sqlite");

        string[] scriptParts = scriptTemplate.Split(new[] { ".import" }, StringSplitOptions.None);

        string preImportScript = scriptParts[0].Trim();

        string[] preImportLines = preImportScript.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
        preImportLines = preImportLines.Where(line => !line.TrimStart().StartsWith(".read")).ToArray();
        preImportScript = string.Join(Environment.NewLine, preImportLines);

        string postImportScript = string.Join(Environment.NewLine, scriptParts[1].Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Skip(1));


        var CHECK_CYCLES = 25_000_000;
        using (var connection = new SqliteConnection($"Data Source={databaseFilename};Pooling=false;"))
        {
            connection.Open();

            SQLitePCL.raw.sqlite3_progress_handler(connection.Handle, CHECK_CYCLES, (_) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine($"data longer sqlite explicitly canceling");
                    return -1;
                }
                return 0;
            }, null);

            using (var pragmaCommand = new SqliteCommand("PRAGMA journal_mode = off;", connection))
            using (var preImportCommand = new SqliteCommand(preImportScript, connection))
            using (var schemaCommand = new SqliteCommand(schema, connection))
            using (var insertCommand = new SqliteCommand("INSERT INTO RawData (json) VALUES (@json)", connection))
            using (var postImportCommand = new SqliteCommand(postImportScript, connection))
            {
                pragmaCommand.ExecuteNonQuery();

                Console.WriteLine($"connection to db {databaseFilename} for file {ndjsonGzFilename}");
                preImportCommand.ExecuteNonQuery();
                schemaCommand.ExecuteNonQuery();

                Console.WriteLine($"Importing {ndjsonGzFilename}");
                insertCommand.Parameters.Add("@json", SqliteType.Text);
                using (var gzipStream = new GZipStream(File.OpenRead(ndjsonGzFilename), CompressionMode.Decompress))
                using (var streamReader = new StreamReader(gzipStream))
                {
                    while (!streamReader.EndOfStream)
                    {
                        string line = streamReader.ReadLine()!;
                        insertCommand.Parameters["@json"].Value = line;
                        insertCommand.ExecuteNonQuery();
                    }
                }

                Console.WriteLine("Raw data imported; populating detailed schema");
                postImportCommand.ExecuteNonQuery();
                Console.WriteLine($"Finished importing {ndjsonGzFilename}");
            }

        }
    }


}

[TestFixture]
public class DatabasePopulatorTests
{
    [Test]
    public void PopulateDatabase_ValidInputs_ExecutesSuccessfully()
    {
        string currentDirectory = Path.Combine(TestContext.CurrentContext.TestDirectory, "../../..");
        string ndjsonGzFilename = Path.Combine(currentDirectory, "./fixtures/CodeSystem-rxnorm-claritin-only.ndjson.gz");
        string populateFilename = Path.Combine(currentDirectory, "./sqlite/populate.sqlite");
        string schemaFilename = Path.Combine(currentDirectory, "./sqlite/schema.sqlite");
        string databaseFilename = Path.Combine(Path.GetTempPath(), "test.db");
        try
        {
            DatabasePopulator.PopulateDatabase(ndjsonGzFilename, databaseFilename);
            Assert.That(File.Exists(databaseFilename));
            using (var connection = new SqliteConnection($"Data Source={databaseFilename}"))
            {
                connection.Open();
                string[] tables = { "CodeSystems", "Concepts", "PropertyTypes", "PropertyInstances" };
                foreach (string table in tables)
                {
                    using (var command = new SqliteCommand($"SELECT COUNT(*) FROM {table}", connection))
                    {
                        long count = (long)command.ExecuteScalar()!;
                        Assert.That(count, Is.GreaterThan(0), $"Table {table} should have rows");
                    }
                }
            }

        }
        finally
        {
            File.Delete(databaseFilename);

        }

    }
}
