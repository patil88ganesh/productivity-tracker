using System.IO;
using System.IO.Pipes;

namespace MiniStopwatch.App;

internal sealed class SocialMediaPauseBridge : IDisposable
{
    private sealed record ConnectionState(
        bool Active,
        string? VisitToken,
        long Sequence);

    public const string PipeName = "ProductivityTracker.SocialMediaPause";

    private readonly Action<bool, string?> stateChanged;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task listenerTask;
    private readonly object stateLock = new();
    private readonly Dictionary<int, ConnectionState> connectionStates = [];
    private int nextConnectionId;
    private long nextSequence;
    private bool lastReportedState;
    private string? lastReportedVisitToken;
    private bool disposed;

    public SocialMediaPauseBridge(Action<bool, string?> stateChanged)
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
            shouldReport = lastReportedState || lastReportedVisitToken != null;
            lastReportedState = false;
            lastReportedVisitToken = null;
        }

        if (shouldReport)
        {
            stateChanged(false, null);
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

                    var separator = message.IndexOf('\t');
                    var active = (separator >= 0 ? message[..separator] : message) == "1";
                    var visitToken = separator >= 0 && separator < message.Length - 1
                        ? message[(separator + 1)..]
                        : null;
                    SetConnectionState(connectionId, active, visitToken);
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
            RemoveConnection(connectionId);
        }
    }

    private void SetConnectionState(
        int connectionId,
        bool active,
        string? visitToken)
    {
        bool aggregateState;
        string? aggregateVisitToken;
        bool shouldReport;
        lock (stateLock)
        {
            if (disposed)
            {
                return;
            }

            connectionStates[connectionId] = new ConnectionState(
                active,
                visitToken,
                Interlocked.Increment(ref nextSequence));
            (aggregateState, aggregateVisitToken) = GetAggregateState();
            shouldReport =
                lastReportedState != aggregateState ||
                lastReportedVisitToken != aggregateVisitToken;
            lastReportedState = aggregateState;
            lastReportedVisitToken = aggregateVisitToken;
        }

        if (shouldReport)
        {
            stateChanged(aggregateState, aggregateVisitToken);
        }
    }

    private void RemoveConnection(int connectionId)
    {
        bool aggregateState;
        string? aggregateVisitToken;
        bool shouldReport;
        lock (stateLock)
        {
            if (disposed)
            {
                return;
            }

            connectionStates.Remove(connectionId);
            (aggregateState, aggregateVisitToken) = GetAggregateState();
            shouldReport =
                lastReportedState != aggregateState ||
                lastReportedVisitToken != aggregateVisitToken;
            lastReportedState = aggregateState;
            lastReportedVisitToken = aggregateVisitToken;
        }

        if (shouldReport)
        {
            stateChanged(aggregateState, aggregateVisitToken);
        }
    }

    private (bool Active, string? VisitToken) GetAggregateState()
    {
        var selectedState = connectionStates.Values
            .Where(state => state.Active)
            .OrderByDescending(state => state.Sequence)
            .FirstOrDefault() ??
            connectionStates.Values
                .Where(state => state.VisitToken != null)
                .OrderByDescending(state => state.Sequence)
                .FirstOrDefault();
        return selectedState == null
            ? (false, null)
            : (selectedState.Active, selectedState.VisitToken);
    }
}
