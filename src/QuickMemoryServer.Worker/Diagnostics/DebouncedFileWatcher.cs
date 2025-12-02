using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;

namespace QuickMemoryServer.Worker.Diagnostics;

public sealed class DebouncedFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly IDisposable _subscription;
    private readonly ILogger<DebouncedFileWatcher> _logger;

    public DebouncedFileWatcher(string directory, string filter, TimeSpan debounceWindow, ILoggerFactory loggerFactory, CancellationToken cancellationToken, Action onChanged)
    {
        _logger = loggerFactory.CreateLogger<DebouncedFileWatcher>();
        _watcher = new FileSystemWatcher(directory, filter)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
        };

        var subject = new Subject<FileSystemEventArgs>();
        _watcher.Changed += (_, args) => subject.OnNext(args);
        _watcher.Created += (_, args) => subject.OnNext(args);
        _watcher.Renamed += (_, args) => subject.OnNext(args);
        _watcher.EnableRaisingEvents = true;

        _subscription = subject
            .Throttle(debounceWindow)
            .Subscribe(_ =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    onChanged();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing file change for {Filter} in {Directory}", filter, directory);
                }
            }, ex => _logger.LogError(ex, "DebouncedFileWatcher encountered an error."));

        cancellationToken.Register(Dispose);
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _watcher.Dispose();
    }
}
