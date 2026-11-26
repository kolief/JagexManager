using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace JagexManager;

public sealed class Relay : IDisposable
{
    private const int MessageLimit = 1024;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(1);

    private readonly CancellationTokenSource stop = new();

    public Relay(string pipe, Action<Command> heard) => _ = ListenAsync(pipe, heard);

    public static string Pipe { get; } = Named();

    public static bool Send(string pipe, Command command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(Patience);
            client.Write(Encoding.UTF8.GetBytes(command.Write()));
            client.Flush();
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose() => stop.Cancel();

    private async Task ListenAsync(string pipe, Action<Command> heard)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop.Token);

                using var reading = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                reading.CancelAfter(Patience);
                if (Command.Read(await ReadAsync(server, reading.Token)) is { } command)
                    heard(command);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                try
                {
                    await Task.Delay(Retry, stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private static async Task<string> ReadAsync(Stream server, CancellationToken cancel)
    {
        var buffer = new byte[MessageLimit];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = await server.ReadAsync(buffer.AsMemory(length), cancel)) > 0)
            length += read;

        return Encoding.UTF8.GetString(buffer, 0, length);
    }

    private static string Named()
    {
        using var self = Process.GetCurrentProcess();
        using var user = WindowsIdentity.GetCurrent();
        return $"JagexManager.{user.User?.Value}.{self.SessionId}";
    }
}
