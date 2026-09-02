using System.IO;
using System.IO.Pipes;

namespace MiniStopwatch.App;

internal sealed class SocialMediaPauseBridge : IDisposable
{
    public const string PipeName = "ProductivityTracker.SocialMediaPause";

    private readonly Action<bool> stateChanged;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task listenerTask;
    private readonly object stateLock = new();
    private readonly HashSet<int> activeConnections = [];
    private int nextConnectionId;
    private bool lastReportedState;
    private bool disposed;

    public SocialMediaPauseBridge(Action<bool> stateChanged)
    {
        this.stateChanged = stateChanged;
        listenerTask = ListenAsync();
    }

    public void Dispose()
    {
        cancellation.Cancel();
        listenerTask.GetAwaiter().GetResult();
        bool shouldReport;
        lock (stateLock)
        {
            disposed = true;
            activeConnections.Clear();
            shouldReport = lastReportedState;
            lastReportedState = false;
        }

        if (shouldReport)
        {
            stateChanged(false);
        }

        cancellation.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!cancellation.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(
                PipeName,
                PipeDirection.In,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await server
                    .WaitForConnectionAsync(cancellation.Token)
                    .ConfigureAwait(false);
                var connectionId = Interlocked.Increment(ref nextConnectionId);
                _ = HandleConnectionAsync(server, connectionId);
            }
            catch (OperationCanceledException)
            {
                server.Dispose();
                break;
            }
        }
    }

    private async Task HandleConnectionAsync(
        NamedPipeServerStream server,
        int connectionId)
    {
        try
        {
            using (server)
            using (var reader = new StreamReader(server))
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var message = await reader
                        .ReadLineAsync(cancellation.Token)
                        .ConfigureAwait(false);
                    if (message == null)
                    {
                        break;
                    }

                    SetConnectionState(connectionId, message == "1");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            SetConnectionState(connectionId, active: false);
        }
    }

    private void SetConnectionState(int connectionId, bool active)
    {
        bool aggregateState;
        bool shouldReport;
        lock (stateLock)
        {
            if (disposed)
            {
                return;
            }

            if (active)
            {
                activeConnections.Add(connectionId);
            }
            else
            {
                activeConnections.Remove(connectionId);
            }

            aggregateState = activeConnections.Count > 0;
            shouldReport = lastReportedState != aggregateState;
            lastReportedState = aggregateState;
        }

        if (shouldReport)
        {
            stateChanged(aggregateState);
        }
    }
}
