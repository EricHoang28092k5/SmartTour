using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;

namespace SmartTourCMS.Services;

public sealed class FileLogRunner : ILogRunner, IDisposable
{
    private readonly Channel<LogCommand> _channel;
    private readonly ChannelWriter<LogCommand> _writer;
    private readonly CancellationTokenSource _cts;
    private readonly Task _worker;
    private int _disposed;

    public FileLogRunner(IConfiguration configuration)
    {
        var custom = configuration["LogRunner:FilePath"];
        LogFilePath = string.IsNullOrWhiteSpace(custom)
            ? Path.Combine(Path.GetTempPath(), "SmartTour", "logqueue.txt")
            : Path.GetFullPath(custom);

        _cts = new CancellationTokenSource();
        _channel = Channel.CreateUnbounded<LogCommand>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _writer = _channel.Writer;
        var reader = _channel.Reader;
        var token = _cts.Token;
        _worker = Task.Run(async () =>
        {
            try
            {
                await foreach (var cmd in reader.ReadAllAsync(token))
                {
                    try
                    {
                        await ProcessAsync(cmd, token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        cmd.TrySetException(ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
        }, CancellationToken.None);
    }

    public string LogFilePath { get; }

    public async Task OverwriteAsync(string text, string? sessionId = null)
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var payload = BuildOverwritePayload(text, sessionId);
        var cmd = new OverwriteCommand(payload, tcs);
        Enqueue(cmd);
        await tcs.Task.ConfigureAwait(false);
    }

    public async Task AppendAsync(string text, string? sessionId = null)
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cmd = new AppendCommand(text, sessionId, tcs);
        Enqueue(cmd);
        await tcs.Task.ConfigureAwait(false);
    }

    public async Task<string> ReadAsync()
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new ReadTextCommand(tcs));
        return await tcs.Task.ConfigureAwait(false);
    }

    public async Task<byte[]> ReadBytesAsync()
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new ReadBytesCommand(tcs));
        return await tcs.Task.ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _writer.TryComplete();
        _cts.Cancel();
        try
        {
            _worker.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (AggregateException)
        {
            // ignore
        }

        _cts.Dispose();
    }

    private void Enqueue(LogCommand cmd)
    {
        if (!_writer.TryWrite(cmd))
            throw new InvalidOperationException("Log runner channel is completed.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
    }

    private static string BuildOverwritePayload(string text, string? sessionId) =>
        BuildSessionBlock(text, sessionId);

    private static string BuildSessionBlock(string text, string? sessionId)
    {
        var body = NormalizeNewlines(text);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var header = $"===== session:{sessionId} | {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====";
            body = header + Environment.NewLine + body;
        }

        return body;
    }

    private void EnsureLogDirectory()
    {
        var dir = Path.GetDirectoryName(LogFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    private static string NormalizeNewlines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
    }

    private async Task ProcessAsync(LogCommand cmd, CancellationToken cancellationToken)
    {
        switch (cmd)
        {
            case OverwriteCommand o:
                EnsureLogDirectory();
                await File.WriteAllTextAsync(LogFilePath, o.Payload, Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);
                o.Tcs.TrySetResult();
                break;
            case AppendCommand a:
                EnsureLogDirectory();
                var sep = File.Exists(LogFilePath) ? Environment.NewLine + Environment.NewLine : string.Empty;
                var block = BuildSessionBlock(a.Text, a.SessionId);
                await File.AppendAllTextAsync(LogFilePath, sep + block, Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);
                a.Tcs.TrySetResult();
                break;
            case ReadTextCommand r:
                if (!File.Exists(LogFilePath))
                    r.Tcs.TrySetResult(string.Empty);
                else
                    r.Tcs.TrySetResult(await File.ReadAllTextAsync(LogFilePath, Encoding.UTF8, cancellationToken)
                        .ConfigureAwait(false));
                break;
            case ReadBytesCommand b:
                if (!File.Exists(LogFilePath))
                    b.Tcs.TrySetResult(Array.Empty<byte>());
                else
                    b.Tcs.TrySetResult(await File.ReadAllBytesAsync(LogFilePath, cancellationToken)
                        .ConfigureAwait(false));
                break;
            default:
                throw new InvalidOperationException("Unknown log command.");
        }
    }

    private abstract class LogCommand
    {
        public abstract void TrySetException(Exception ex);
    }

    private sealed class OverwriteCommand(string Payload, TaskCompletionSource Tcs) : LogCommand
    {
        public string Payload { get; } = Payload;
        public TaskCompletionSource Tcs { get; } = Tcs;

        public override void TrySetException(Exception ex) => Tcs.TrySetException(ex);
    }

    private sealed class AppendCommand(string Text, string? SessionId, TaskCompletionSource Tcs) : LogCommand
    {
        public string Text { get; } = Text;
        public string? SessionId { get; } = SessionId;
        public TaskCompletionSource Tcs { get; } = Tcs;

        public override void TrySetException(Exception ex) => Tcs.TrySetException(ex);
    }

    private sealed class ReadTextCommand(TaskCompletionSource<string> Tcs) : LogCommand
    {
        public TaskCompletionSource<string> Tcs { get; } = Tcs;

        public override void TrySetException(Exception ex) => Tcs.TrySetException(ex);
    }

    private sealed class ReadBytesCommand(TaskCompletionSource<byte[]> Tcs) : LogCommand
    {
        public TaskCompletionSource<byte[]> Tcs { get; } = Tcs;

        public override void TrySetException(Exception ex) => Tcs.TrySetException(ex);
    }
}
