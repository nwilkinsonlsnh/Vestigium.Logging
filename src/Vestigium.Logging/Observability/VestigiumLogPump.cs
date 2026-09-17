using System.Threading.Channels;

namespace Vestigium.Logging;

/// <summary>
/// Drains <see cref="VestigiumLogger.EventReader"/> in batches. Runs on the caller thread
/// (typically a background task). The callback must marshal onto the UI dispatcher.
/// </summary>
public static class VestigiumLogPump
{
    public static Task RunAsync(
        ChannelReader<VestigiumLogEvent> reader,
        Action<IReadOnlyList<VestigiumLogEvent>> emitBatch,
        VestigiumLoggerOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(emitBatch);
        ArgumentNullException.ThrowIfNull(options);
        return RunAsync(reader, emitBatch, options.UiBatchSize, options.UiBatchInterval, cancellationToken);
    }

    public static async Task RunAsync(
        ChannelReader<VestigiumLogEvent> reader,
        Action<IReadOnlyList<VestigiumLogEvent>> emitBatch,
        int batchSize,
        TimeSpan batchInterval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(emitBatch);

        var size = Math.Max(1, batchSize);
        var interval = batchInterval <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(100) : batchInterval;
        var buffer = new List<VestigiumLogEvent>(size);

        while (!cancellationToken.IsCancellationRequested)
        {
            buffer.Clear();
            using var timer = new CancellationTokenSource(interval);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timer.Token);
            try
            {
                while (buffer.Count < size && await reader.WaitToReadAsync(linked.Token).ConfigureAwait(false))
                {
                    while (buffer.Count < size && reader.TryRead(out var evt))
                        buffer.Add(evt);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Batch interval elapsed.
            }

            if (buffer.Count == 0)
            {
                if (reader.Completion.IsCompleted)
                    return;
                continue;
            }

            emitBatch(buffer.ToArray());
        }
    }
}
