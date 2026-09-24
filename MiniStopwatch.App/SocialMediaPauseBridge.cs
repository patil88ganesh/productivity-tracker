using System.IO;
using System.IO.Pipes;

using MiniStopwatch.Core;

namespace MiniStopwatch.App;

internal sealed class SocialMediaPauseBridge : IDisposable
{
    public const string PipeName = "ProductivityTracker.SocialMediaPause";

    private readonly Action<BrowserActivityKind> stateChanged;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task listenerTask;
    private readonly object stateLock = new();
    private readonly Dictionary<int, BrowserActivityKind> connectionStates = [];
    private int nextConnectionId;
    private BrowserActivityKind lastReportedState;
    private bool disposed;

    public SocialMediaPauseBridge(Action<BrowserActivityKind> stateChanged)
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
            connectionStates.Clear();
            shouldReport = lastReportedState != BrowserActivityKind.None;
            lastReportedState = BrowserActivityKind.None;
        }

        if (shouldReport)
        {
            stateChanged(BrowserActivityKind.None);
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

                    SetConnectionState(connectionId, ParseState(message));
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
            SetConnectionState(connectionId, BrowserActivityKind.None);
        }
    }

    private static BrowserActivityKind ParseState(string message)
    {
        return message switch
        {
            "y" => BrowserActivityKind.YouTube,
            "1" => BrowserActivityKind.OtherDistracting,
            "u" => BrowserActivityKind.UnknownDistracting,
            _ => BrowserActivityKind.None,
        };
    }

    private void SetConnectionState(
        int connectionId,
        BrowserActivityKind state)
    {
        BrowserActivityKind aggregateState;
        bool shouldReport;
        lock (stateLock)
        {
            if (disposed)
            {
                return;
            }

            if (state != BrowserActivityKind.None)
            {
                connectionStates[connectionId] = state;
            }
            else
            {
                connectionStates.Remove(connectionId);
            }

            aggregateState = connectionStates.Values.Contains(
                BrowserActivityKind.OtherDistracting)
                    ? BrowserActivityKind.OtherDistracting
                    : connectionStates.Values.Contains(
                        BrowserActivityKind.UnknownDistracting)
                        ? BrowserActivityKind.UnknownDistracting
                        : connectionStates.Values.Contains(
                            BrowserActivityKind.YouTube)
                            ? BrowserActivityKind.YouTube
                            : BrowserActivityKind.None;
            shouldReport = lastReportedState != aggregateState;
            lastReportedState = aggregateState;
        }

        if (shouldReport)
        {
            stateChanged(aggregateState);
        }
    }
}
