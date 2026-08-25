using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SignVR.Interaction.CaptureHost
{
    /// <summary>
    /// Cooperative cancellation observed only by background artifact workers.
    /// It deliberately contains no Unity object or API reference.
    /// </summary>
    internal sealed class InteractionArtifactCancellation
    {
        private readonly InteractionArtifactCancellationSource source;

        internal InteractionArtifactCancellation(
            InteractionArtifactCancellationSource source)
        {
            this.source = source;
        }

        public static InteractionArtifactCancellation None { get; } =
            new InteractionArtifactCancellation(null);

        public bool IsCancellationRequested =>
            source != null && source.IsCancellationRequested;

        public bool WasObserved => source != null && source.WasObserved;

        public void ThrowIfCancellationRequested()
        {
            if (source == null || !source.IsCancellationRequested)
            {
                return;
            }
            source.MarkObserved();
            throw new OperationCanceledException(
                "Interaction artifact operation was cancelled."
            );
        }
    }

    internal sealed class InteractionArtifactCancellationSource
    {
        private readonly object gate = new object();
        private bool cancellationRequested;
        private bool cancellationObserved;
        private bool completionPublished;

        public InteractionArtifactCancellationSource()
        {
            Token = new InteractionArtifactCancellation(this);
        }

        public InteractionArtifactCancellation Token { get; }
        public bool IsCancellationRequested
        {
            get
            {
                lock (gate)
                {
                    return cancellationRequested;
                }
            }
        }
        public bool WasObserved
        {
            get
            {
                lock (gate)
                {
                    return cancellationObserved;
                }
            }
        }

        public bool Cancel()
        {
            lock (gate)
            {
                if (completionPublished || cancellationRequested)
                {
                    return false;
                }
                cancellationRequested = true;
                return true;
            }
        }

        public void MarkObserved()
        {
            lock (gate)
            {
                cancellationObserved = true;
            }
        }

        public void PublishCompletion<T>(
            InteractionBackgroundOperation<T> operation,
            T value,
            Exception failure)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            lock (gate)
            {
                if (completionPublished)
                {
                    return;
                }
                if (failure == null && cancellationRequested)
                {
                    cancellationObserved = true;
                    failure = new OperationCanceledException(
                        "Interaction artifact operation was cancelled before publication."
                    );
                }
                operation.Complete(value, failure);
                completionPublished = true;
            }
        }
    }

    internal interface IInteractionArtifactReadObserver
    {
        void OnChunkRead(
            string path,
            long totalBytesRead,
            InteractionArtifactCancellation cancellation);
    }

    internal static class InteractionArtifactReadObserver
    {
        public static readonly IInteractionArtifactReadObserver None =
            new NullInteractionArtifactReadObserver();

        private sealed class NullInteractionArtifactReadObserver :
            IInteractionArtifactReadObserver
        {
            public void OnChunkRead(
                string path,
                long totalBytesRead,
                InteractionArtifactCancellation cancellation)
            {
            }
        }
    }

    internal interface IInteractionArtifactCompletionObserver
    {
        void BeforePublish(
            string key,
            InteractionArtifactCancellation cancellation);
    }

    internal static class InteractionArtifactCompletionObserver
    {
        public static readonly IInteractionArtifactCompletionObserver None =
            new NullInteractionArtifactCompletionObserver();

        private sealed class NullInteractionArtifactCompletionObserver :
            IInteractionArtifactCompletionObserver
        {
            public void BeforePublish(
                string key,
                InteractionArtifactCancellation cancellation)
            {
            }
        }
    }

    internal static class InteractionArtifactHasher
    {
        public const int ChunkBytes = 65536;

        public static string ComputeSha256(
            string path,
            InteractionArtifactCancellation cancellation,
            IInteractionArtifactReadObserver observer)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Artifact path is required.",
                    nameof(path)
                );
            }
            cancellation = cancellation ??
                throw new ArgumentNullException(nameof(cancellation));
            observer = observer ??
                throw new ArgumentNullException(nameof(observer));
            cancellation.ThrowIfCancellationRequested();
            byte[] buffer = new byte[ChunkBytes];
            long totalBytesRead = 0L;
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                ChunkBytes,
                FileOptions.SequentialScan))
            using (SHA256 sha = SHA256.Create())
            {
                while (true)
                {
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        break;
                    }
                    sha.TransformBlock(buffer, 0, read, buffer, 0);
                    totalBytesRead = checked(totalBytesRead + read);
                    observer.OnChunkRead(
                        path,
                        totalBytesRead,
                        cancellation
                    );
                    cancellation.ThrowIfCancellationRequested();
                }
                cancellation.ThrowIfCancellationRequested();
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                byte[] digest = sha.Hash ?? throw new CryptographicException(
                    "SHA-256 did not produce an artifact digest."
                );
                var builder = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                {
                    builder.Append(digest[index].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }

    internal static class InteractionArtifactOperationKeys
    {
        public static string ForFreeze(string runDirectory)
        {
            if (string.IsNullOrWhiteSpace(runDirectory))
            {
                throw new ArgumentException(
                    "Run directory is required.",
                    nameof(runDirectory)
                );
            }
            return "freeze|" + Path.GetFullPath(runDirectory);
        }

        public static string ForVerify(InteractionFrozenArtifact artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }
            return "verify|" + artifact.ArtifactType + "|" +
                Path.GetFullPath(artifact.Path);
        }
    }

    internal interface IInteractionArtifactOperation
    {
        string Key { get; }
        bool IsCompleted { get; }
        bool RequestCancellation();
        void MarkReaped();
    }

    internal sealed class InteractionArtifactOperation<T> :
        IInteractionArtifactOperation
    {
        private readonly InteractionBackgroundOperation<T> operation;
        private readonly InteractionArtifactCancellationSource cancellation;
        private int reaped;

        internal InteractionArtifactOperation(
            string key,
            InteractionBackgroundOperation<T> operation,
            InteractionArtifactCancellationSource cancellation)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            this.operation = operation ??
                throw new ArgumentNullException(nameof(operation));
            this.cancellation = cancellation ??
                throw new ArgumentNullException(nameof(cancellation));
        }

        public string Key { get; }
        public bool IsCompleted => operation.IsCompleted;
        public bool Succeeded => operation.Succeeded;
        public Exception Error => operation.Error;
        public bool CancellationRequested =>
            cancellation.IsCancellationRequested;
        public bool CancellationObserved => cancellation.WasObserved;
        public bool IsReaped => Volatile.Read(ref reaped) != 0;
        public int CallingThreadId => operation.CallingThreadId;
        public int WorkerThreadId => operation.WorkerThreadId;

        public bool Wait(TimeSpan timeout)
        {
            return operation.Wait(timeout);
        }

        public T GetResult()
        {
            return operation.GetResult();
        }

        public bool RequestCancellation()
        {
            return cancellation.Cancel();
        }

        public void MarkReaped()
        {
            Volatile.Write(ref reaped, 1);
        }

        internal void Execute(
            Func<InteractionArtifactCancellation, T> work,
            IInteractionArtifactCompletionObserver completionObserver)
        {
            T value = default(T);
            Exception failure = null;
            try
            {
                operation.SetWorkerThreadId();
                value = work(cancellation.Token);
                completionObserver.BeforePublish(Key, cancellation.Token);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            cancellation.PublishCompletion(operation, value, failure);
        }

        internal void CompleteFailure(Exception failure)
        {
            cancellation.PublishCompletion(
                operation,
                default(T),
                failure ?? throw new ArgumentNullException(nameof(failure))
            );
        }
    }

    /// <summary>
    /// Component-owned registry for long file reads. It keeps a lease even when
    /// a coroutine disappears, rejects overlapping keys, requests cancellation
    /// without waiting, and reaps itself from the worker completion boundary.
    /// </summary>
    internal sealed class InteractionArtifactOperationRegistry
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, IInteractionArtifactOperation>
            active = new Dictionary<string, IInteractionArtifactOperation>(
                Path.DirectorySeparatorChar == '\\'
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal
            );
        private IInteractionBackgroundWorkQueue workQueue =
            InteractionThreadPoolBackgroundWorkQueue.Shared;
        private IInteractionArtifactCompletionObserver completionObserver =
            InteractionArtifactCompletionObserver.None;

        public int ActiveCount
        {
            get
            {
                lock (gate)
                {
                    return active.Count;
                }
            }
        }

        public bool TryStart<T>(
            string key,
            Func<InteractionArtifactCancellation, T> work,
            out InteractionArtifactOperation<T> operation)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException(
                    "Artifact operation key is required.",
                    nameof(key)
                );
            }
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            string safeKey = key.Trim();
            var cancellation = new InteractionArtifactCancellationSource();
            InteractionBackgroundOperation<T> background =
                InteractionBackgroundOperation<T>.CreatePending();
            var candidate = new InteractionArtifactOperation<T>(
                safeKey,
                background,
                cancellation
            );
            IInteractionBackgroundWorkQueue queueSnapshot;
            IInteractionArtifactCompletionObserver completionObserverSnapshot;
            lock (gate)
            {
                if (active.TryGetValue(
                        safeKey,
                        out IInteractionArtifactOperation existing))
                {
                    if (!existing.IsCompleted)
                    {
                        operation = null;
                        return false;
                    }
                    active.Remove(safeKey);
                    existing.MarkReaped();
                }
                active.Add(safeKey, candidate);
                queueSnapshot = workQueue;
                completionObserverSnapshot = completionObserver;
            }

            Exception queueFailure = null;
            try
            {
                bool queued = queueSnapshot.TryQueue(() =>
                {
                    try
                    {
                        candidate.Execute(work, completionObserverSnapshot);
                    }
                    finally
                    {
                        Reap(safeKey, candidate);
                    }
                });
                if (!queued)
                {
                    queueFailure = new InvalidOperationException(
                        "Artifact background operation could not be queued."
                    );
                }
            }
            catch (Exception exception)
            {
                queueFailure = new InvalidOperationException(
                    "Artifact background work queue rejected the operation.",
                    exception
                );
            }
            if (queueFailure != null)
            {
                candidate.CompleteFailure(queueFailure);
                Reap(safeKey, candidate);
                operation = candidate;
                return true;
            }
            operation = candidate;
            return true;
        }

        internal void ConfigureWorkQueue(
            IInteractionBackgroundWorkQueue value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            lock (gate)
            {
                if (active.Count != 0)
                {
                    throw new InvalidOperationException(
                        "Artifact work queue cannot change while an operation is active."
                    );
                }
                workQueue = value;
            }
        }

        internal void ConfigureCompletionObserver(
            IInteractionArtifactCompletionObserver value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            lock (gate)
            {
                if (active.Count != 0)
                {
                    throw new InvalidOperationException(
                        "Artifact completion observer cannot change while an operation is active."
                    );
                }
                completionObserver = value;
            }
        }

        public bool TryGet<T>(
            string key,
            out InteractionArtifactOperation<T> operation)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                operation = null;
                return false;
            }
            lock (gate)
            {
                if (active.TryGetValue(
                        key.Trim(),
                        out IInteractionArtifactOperation value) &&
                    value is InteractionArtifactOperation<T> typed)
                {
                    operation = typed;
                    return true;
                }
            }
            operation = null;
            return false;
        }

        public int CancelAll()
        {
            IInteractionArtifactOperation[] snapshot;
            lock (gate)
            {
                snapshot = new IInteractionArtifactOperation[active.Count];
                active.Values.CopyTo(snapshot, 0);
            }
            int requested = 0;
            for (int index = 0; index < snapshot.Length; index++)
            {
                if (snapshot[index].RequestCancellation())
                {
                    requested++;
                }
            }
            return requested;
        }

        private void Reap(
            string key,
            IInteractionArtifactOperation operation)
        {
            lock (gate)
            {
                if (active.TryGetValue(
                        key,
                        out IInteractionArtifactOperation current) &&
                    ReferenceEquals(current, operation))
                {
                    active.Remove(key);
                }
            }
            operation.MarkReaped();
        }
    }
}
