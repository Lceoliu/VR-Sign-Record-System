using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

/// <summary>
/// Writes pose JSONL on a background thread so storage latency cannot block
/// Meta tracking or the render loop.
/// </summary>
internal sealed class AsyncPoseFileWriter
{
    private const int MaximumQueuedLines = 512;
    private const int CloseTimeoutMilliseconds = 10000;

    private readonly ConcurrentQueue<string> pendingLines = new();
    private readonly AutoResetEvent wakeSignal = new(false);
    private readonly StreamWriter streamWriter;
    private readonly Thread worker;

    private volatile bool accepting = true;
    private Exception workerException;
    private int queuedLineCount;

    public AsyncPoseFileWriter(string path, int bufferSize)
    {
        streamWriter = new StreamWriter(
            new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                bufferSize,
                FileOptions.SequentialScan
            ),
            new UTF8Encoding(false),
            bufferSize,
            false
        );

        worker = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "SignVR Pose Writer"
        };
        worker.Start();
    }

    public bool TryWriteLine(string line)
    {
        if (!accepting || workerException != null)
        {
            return false;
        }

        int queued = Interlocked.Increment(ref queuedLineCount);
        if (queued > MaximumQueuedLines)
        {
            Interlocked.Decrement(ref queuedLineCount);
            return false;
        }

        pendingLines.Enqueue(line);
        wakeSignal.Set();
        return true;
    }

    public void CloseAndWait()
    {
        accepting = false;
        wakeSignal.Set();

        if (!worker.Join(CloseTimeoutMilliseconds))
        {
            throw new IOException(
                "Timed out while closing the background pose writer."
            );
        }

        wakeSignal.Dispose();
        if (workerException != null)
        {
            throw new IOException(
                "The background pose writer failed.",
                workerException
            );
        }
    }

    private void WriteLoop()
    {
        try
        {
            while (accepting || !pendingLines.IsEmpty)
            {
                if (pendingLines.TryDequeue(out string line))
                {
                    streamWriter.WriteLine(line);
                    Interlocked.Decrement(ref queuedLineCount);
                    continue;
                }

                wakeSignal.WaitOne(20);
            }

            streamWriter.Flush();
        }
        catch (Exception exception)
        {
            workerException = exception;
        }
        finally
        {
            try
            {
                streamWriter.Dispose();
            }
            catch (Exception exception)
            {
                workerException ??= exception;
            }
        }
    }
}
