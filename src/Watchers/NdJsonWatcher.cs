using System;
using System.IO;
using System.Reflection;
using System.Collections.Concurrent;
using System.Threading;
using NUnit.Framework;
using System.Threading.Channels;

public class ReadyFileWatcher
{
    protected readonly string _folderPath;
    protected readonly string _filter;
    private readonly FileSystemWatcher _watcher;
    private readonly TimeSpan _debounceDelay;
    protected readonly Func<string, WatcherChangeTypes, CancellationToken, Task> _handler;
    private readonly Dictionary<string, CancellationTokenSource> _pendingHandlerTasks = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    public FileSystemWatcher Watcher => _watcher;

    public ReadyFileWatcher(string folderPath, string filter, TimeSpan debounceDelay, Func<string, WatcherChangeTypes, CancellationToken, Task> handler)
    {
        _folderPath = folderPath;
        _filter = filter;
        _watcher = new FileSystemWatcher(folderPath, filter) {
            EnableRaisingEvents = true,
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Size

        };
        _debounceDelay = debounceDelay;
        _handler = handler;
        _watcher.Created += OnFileChanged;
        _watcher.Changed += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
    }

    private async void OnFileChanged(object _, FileSystemEventArgs fileEvent)
    {
        Console.WriteLine($"On file changed {this}");
        await _semaphore.WaitAsync();
        try
        {
            if (_pendingHandlerTasks.TryGetValue(fileEvent.FullPath, out var pendingCancellationTokenSource))
            {
                pendingCancellationTokenSource.Cancel();
            }

            var handlerCancellationTokenSource = new CancellationTokenSource();
            _pendingHandlerTasks[fileEvent.FullPath] = handlerCancellationTokenSource;

            _ = Task.Run(async () =>
            {
                await Task.Delay(_debounceDelay, handlerCancellationTokenSource.Token);
                try
                {
                    await _handler(fileEvent.FullPath, fileEvent.ChangeType, handlerCancellationTokenSource.Token);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception in file change handler: " + e.Message + fileEvent.FullPath);
                }
                await _semaphore.WaitAsync();
                if (_pendingHandlerTasks.TryGetValue(fileEvent.FullPath, out var existingCancellationTokenSource) && existingCancellationTokenSource == handlerCancellationTokenSource)
                {
                    _pendingHandlerTasks.Remove(fileEvent.FullPath);
                }
                _semaphore.Release();

            }, handlerCancellationTokenSource.Token);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}

class StartupWatcher : ReadyFileWatcher
{
    public StartupWatcher(string folderPath, string filter, TimeSpan debounceDelay, Func<string, WatcherChangeTypes, CancellationToken, Task> handler): base(folderPath, filter, debounceDelay, handler)
    {
        Start();
    }
    public void Start()
    {
        var existingFiles = Directory.GetFiles(_folderPath, _filter);
        foreach (var f in existingFiles)
        {
            Console.WriteLine($"Load existing from {_folderPath} {_filter} {f}");
            _handler(f, WatcherChangeTypes.Created, default);
        }
    }

}

public class NdjsonGzWatcher
{
    private readonly string _ndjsonFolderPath;
    private readonly string _databaseFolderPath;
    private readonly ReadyFileWatcher _watcher;

    public NdjsonGzWatcher(string ndjsonFolderPath, string databaseFolderPath)
    {
        _ndjsonFolderPath = ndjsonFolderPath;
        _databaseFolderPath = databaseFolderPath;
        _watcher = new ReadyFileWatcher(_ndjsonFolderPath, "*.ndjson.gz", TimeSpan.FromSeconds(1), OnNewNdjsonGz);
    }

    internal async Task OnNewNdjsonGz(string filePath, WatcherChangeTypes c, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string ndjsonGzFileName = Path.GetFileName(filePath);
        string databaseFileName = ndjsonGzFileName[..^9] + "db";
        int randomNumber = new Random().Next(10000, 99999);
        string tempDatabaseFileName = Path.Combine(_databaseFolderPath, $"{databaseFileName}{randomNumber}.temp");
        string finalDatabaseFilename = Path.Combine(_databaseFolderPath, databaseFileName);
        try {
            Console.WriteLine($"populating new db from {filePath}");
            DatabasePopulator.PopulateDatabase(filePath, tempDatabaseFileName, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempDatabaseFileName, finalDatabaseFilename, overwrite: true);
            File.SetLastWriteTimeUtc(finalDatabaseFilename, DateTime.UtcNow);
            Console.WriteLine($"swapped file into {finalDatabaseFilename}");
        } finally {
            File.Delete(tempDatabaseFileName);
        }
    }
}

[TestFixture]
public class NdjsonGzWatcherTests
{
    [Test]
    public async Task ReadyFileWatcher_HandlerIsCalledAfterDebounceDelay()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), "ReadyFileWatcherTest");
        Directory.CreateDirectory(folderPath);
        var handlerCalled = false;
        var watcher = new ReadyFileWatcher(folderPath, "*.txt", TimeSpan.FromMilliseconds(100), async (path, token, _) =>
        {
            handlerCalled = true;
            await Task.CompletedTask;
        });
        File.WriteAllText(Path.Combine(folderPath, "test.txt"), "test");
        await Task.Delay(200);
        Assert.That(handlerCalled);
        Directory.Delete(folderPath, true);
    }

    [Test]
    public async Task NdjsonGzWatcher_CancellationTokenCancelsPopulation()
    {
        var ndjsonFolderPath = Path.Combine(Path.GetTempPath(), "NdjsonGzWatcherCancellationTest");
        var databaseFolderPath = Path.Combine(Path.GetTempPath(), "NdjsonGzWatcherCancellationTestDb");
        Directory.CreateDirectory(ndjsonFolderPath);
        Directory.CreateDirectory(databaseFolderPath);
        var watcher = new NdjsonGzWatcher(ndjsonFolderPath, databaseFolderPath);
        var ndjsonGzFilePath = Path.Combine(ndjsonFolderPath, "test.ndjson.gz");
        var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await watcher.OnNewNdjsonGz(ndjsonGzFilePath, WatcherChangeTypes.Created, cts.Token));
        Assert.That(!File.Exists(Path.Combine(databaseFolderPath, "test.db")));
        Directory.Delete(ndjsonFolderPath, true);
        Directory.Delete(databaseFolderPath, true);
    }

}
