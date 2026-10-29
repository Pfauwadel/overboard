using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Threading;

namespace Overboard.Distribution;

/// <summary>
/// One Overboard per user and data directory. A later launch hands its file (or nothing, meaning
/// "show the board") to the running instance through a named pipe that only the same user can open,
/// then exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string Name = $"overboard-{Environment.UserName}-{Hash(AppInfo.DataDirectory)}";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    public SingleInstance()
    {
        _mutex = new Mutex(true, Name, out var first);
        IsFirst = first;
    }

    public bool IsFirst { get; }

    /// <summary>Sends the file given on the command line, if any, to the running instance. Returns whether it got it.</summary>
    public static bool Forward(string[] args)
    {
        var path = args.FirstOrDefault(File.Exists) is { } file ? Path.GetFullPath(file) : "";
        // The running instance may still be starting: retry for a few seconds.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                pipe.Connect(500);
                using var writer = new StreamWriter(pipe, Encoding.UTF8);
                writer.WriteLine(path);
                return true;
            }
            catch (Exception e) when (e is TimeoutException or IOException)
            {
                Thread.Sleep(300);
            }
        }
        return false;
    }

    /// <summary>Receives later launches on the UI thread: their file path, or null to just show Overboard.</summary>
    public void Listen(Action<string?> launched) => _ = Task.Run(async () =>
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                var line = await reader.ReadLineAsync(_stop.Token);
                Dispatcher.UIThread.Post(() => launched(string.IsNullOrWhiteSpace(line) ? null : line));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A launch that went away mid-message: wait for the next one.
            }
        }
    });

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToUpperInvariant())))[..12].ToLowerInvariant();

    public void Dispose()
    {
        _stop.Cancel();
        _mutex.Dispose();
    }
}
