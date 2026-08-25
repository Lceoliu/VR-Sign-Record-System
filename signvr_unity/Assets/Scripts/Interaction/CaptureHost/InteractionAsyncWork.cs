using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace SignVR.Interaction.CaptureHost
{
    internal interface IInteractionBackgroundWorkQueue
    {
        bool TryQueue(Action work);
    }

    internal sealed class InteractionThreadPoolBackgroundWorkQueue :
        IInteractionBackgroundWorkQueue
    {
        public static InteractionThreadPoolBackgroundWorkQueue Shared { get; } =
            new InteractionThreadPoolBackgroundWorkQueue();

        private InteractionThreadPoolBackgroundWorkQueue()
        {
        }

        public bool TryQueue(Action work)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }
            return ThreadPool.QueueUserWorkItem(_ => work());
        }
    }

    /// <summary>
    /// Pollable background result used by Unity coroutines without blocking the
    /// Unity thread. Wait is provided for deterministic non-Unity tests only.
    /// </summary>
    public sealed class InteractionBackgroundOperation<T>
    {
        private readonly object gate = new object();
        private readonly ManualResetEventSlim completed =
            new ManualResetEventSlim(false);
        private T result;
        private Exception error;
        private bool isCompleted;
        private List<Action<T, Exception>> completionObservers;

        private InteractionBackgroundOperation()
        {
            CallingThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public int CallingThreadId { get; }
        public int WorkerThreadId { get; private set; }

        public bool IsCompleted
        {
            get
            {
                lock (gate)
                {
                    return isCompleted;
                }
            }
        }

        public bool Succeeded
        {
            get
            {
                lock (gate)
                {
                    return isCompleted && error == null;
                }
            }
        }

        public Exception Error
        {
            get
            {
                lock (gate)
                {
                    return error;
                }
            }
        }

        public static InteractionBackgroundOperation<T> Start(Func<T> work)
        {
            return Start(
                work,
                InteractionThreadPoolBackgroundWorkQueue.Shared
            );
        }

        internal static InteractionBackgroundOperation<T> Start(
            Func<T> work,
            IInteractionBackgroundWorkQueue workQueue)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }
            if (workQueue == null)
            {
                throw new ArgumentNullException(nameof(workQueue));
            }
            var operation = new InteractionBackgroundOperation<T>();
            try
            {
                if (!workQueue.TryQueue(() => operation.Execute(work)))
                {
                    operation.CompleteFailure(new InvalidOperationException(
                        "Background work could not be queued."
                    ));
                }
            }
            catch (Exception exception)
            {
                operation.CompleteFailure(new InvalidOperationException(
                    "Background work queue rejected the operation.",
                    exception
                ));
            }
            return operation;
        }

        public bool Wait(TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }
            return completed.Wait(timeout);
        }

        public T GetResult()
        {
            lock (gate)
            {
                if (!isCompleted)
                {
                    throw new InvalidOperationException(
                        "Background operation has not completed."
                    );
                }
                if (error != null)
                {
                    throw new InvalidOperationException(
                        "Background operation failed.",
                        error
                    );
                }
                return result;
            }
        }

        internal static InteractionBackgroundOperation<T> CreatePending()
        {
            return new InteractionBackgroundOperation<T>();
        }

        internal static InteractionBackgroundOperation<T> CreateCompleted(
            T value,
            Exception failure = null)
        {
            var operation = new InteractionBackgroundOperation<T>();
            operation.Complete(value, failure);
            return operation;
        }

        internal void Execute(Func<T> work)
        {
            T value = default(T);
            Exception failure = null;
            try
            {
                WorkerThreadId = Thread.CurrentThread.ManagedThreadId;
                value = work();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            Complete(value, failure);
        }

        internal void SetWorkerThreadId()
        {
            WorkerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        internal void CompleteFailure(Exception failure)
        {
            Complete(default(T), failure ?? throw new ArgumentNullException(
                nameof(failure)
            ));
        }

        internal void Complete(T value, Exception failure)
        {
            Action<T, Exception>[] observers = null;
            lock (gate)
            {
                if (isCompleted)
                {
                    return;
                }
                result = value;
                error = failure;
                isCompleted = true;
                if (completionObservers != null)
                {
                    observers = completionObservers.ToArray();
                    completionObservers = null;
                }
            }
            completed.Set();
            if (observers == null)
            {
                return;
            }
            for (int index = 0; index < observers.Length; index++)
            {
                InvokeObserverSafely(observers[index], value, failure);
            }
        }

        internal void ObserveCompletion(Action<T, Exception> observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }
            T completedResult = default(T);
            Exception completedError = null;
            bool invokeNow;
            lock (gate)
            {
                invokeNow = isCompleted;
                if (!invokeNow)
                {
                    if (completionObservers == null)
                    {
                        completionObservers = new List<Action<T, Exception>>();
                    }
                    completionObservers.Add(observer);
                    return;
                }
                completedResult = result;
                completedError = error;
            }
            InvokeObserverSafely(observer, completedResult, completedError);
        }

        private static void InvokeObserverSafely(
            Action<T, Exception> observer,
            T value,
            Exception failure)
        {
            try
            {
                observer(value, failure);
            }
            catch
            {
                // Completion is already immutable. One diagnostic observer must
                // not block later ownership observers or escape a worker thread.
            }
        }
    }

    /// <summary>
    /// Publishes one complete background result through a single-reference
    /// handoff. Callers cannot observe fields while the result is being built.
    /// </summary>
    public sealed class InteractionBackgroundHandoff<T>
    {
        private readonly InteractionBackgroundOperation<T> operation;
        private int consumed;

        private InteractionBackgroundHandoff(
            InteractionBackgroundOperation<T> operation)
        {
            this.operation = operation ??
                throw new ArgumentNullException(nameof(operation));
        }

        public bool IsCompleted => operation.IsCompleted;
        public int CallingThreadId => operation.CallingThreadId;
        public int WorkerThreadId => operation.WorkerThreadId;
        public Exception Error => operation.Error;

        public static InteractionBackgroundHandoff<T> Start(Func<T> work)
        {
            return Start(
                work,
                InteractionThreadPoolBackgroundWorkQueue.Shared
            );
        }

        internal static InteractionBackgroundHandoff<T> Start(
            Func<T> work,
            IInteractionBackgroundWorkQueue workQueue)
        {
            return new InteractionBackgroundHandoff<T>(
                InteractionBackgroundOperation<T>.Start(work, workQueue)
            );
        }

        internal static InteractionBackgroundHandoff<T> FromCompletion(
            T value,
            Exception failure = null)
        {
            return new InteractionBackgroundHandoff<T>(
                InteractionBackgroundOperation<T>.CreateCompleted(
                    value,
                    failure
                )
            );
        }

        public bool Wait(TimeSpan timeout)
        {
            return operation.Wait(timeout);
        }

        public bool TryConsume(out T result)
        {
            if (!TryConsumeCompletion(out result, out Exception failure))
            {
                return false;
            }
            if (failure != null)
            {
                throw new InvalidOperationException(
                    "Background handoff completed with a failure.",
                    failure
                );
            }
            return true;
        }

        public bool TryConsumeCompletion(
            out T result,
            out Exception failure)
        {
            result = default(T);
            failure = null;
            if (!operation.IsCompleted ||
                Interlocked.CompareExchange(ref consumed, 1, 0) != 0)
            {
                return false;
            }
            failure = operation.Error;
            if (failure == null)
            {
                result = operation.GetResult();
            }
            return true;
        }
    }

    /// <summary>
    /// Pure managed ownership continuation used only when lifecycle work could
    /// not be queued. It attaches directly to capture initialization completion,
    /// so it needs no second queue and never references a Controller or Unity API.
    /// </summary>
    internal sealed class InteractionDetachedInitializationOwner
    {
        private int completionAccepted;
        private int isCompleted;
        private int writerOwned;
        private Exception error;

        private InteractionDetachedInitializationOwner()
        {
        }

        public bool IsCompleted => Volatile.Read(ref isCompleted) != 0;
        public bool WriterOwned => Volatile.Read(ref writerOwned) != 0;
        public Exception Error => error;

        public static InteractionDetachedInitializationOwner Adopt(
            InteractionBackgroundOperation<InteractionCaptureWriter>
                initialization)
        {
            if (initialization == null)
            {
                throw new ArgumentNullException(nameof(initialization));
            }
            var owner = new InteractionDetachedInitializationOwner();
            initialization.ObserveCompletion(owner.AcceptCompletion);
            return owner;
        }

        private void AcceptCompletion(
            InteractionCaptureWriter writer,
            Exception initializationFailure)
        {
            if (Interlocked.Exchange(ref completionAccepted, 1) != 0)
            {
                return;
            }
            Exception ownershipFailure = initializationFailure;
            if (ownershipFailure == null && writer == null)
            {
                ownershipFailure = new IOException(
                    "Capture initialization completed without a writer."
                );
            }
            if (writer != null)
            {
                Volatile.Write(ref writerOwned, 1);
                try
                {
                    writer.Dispose();
                }
                catch (Exception exception)
                {
                    ownershipFailure = ownershipFailure == null
                        ? exception
                        : new AggregateException(
                            ownershipFailure,
                            exception
                        );
                }
            }
            error = ownershipFailure;
            Volatile.Write(ref isCompleted, 1);
        }
    }

    public sealed class InteractionLifecycleTerminalizationResult
    {
        internal InteractionLifecycleTerminalizationResult(
            InteractionCaptureWriter writer,
            InteractionCaptureSealResult sealResult,
            string abortReason,
            DateTimeOffset terminalUtc,
            bool initializationDelayObserved,
            Exception error,
            InteractionDetachedInitializationOwner detachedInitializationOwner)
        {
            Writer = writer;
            SealResult = sealResult;
            AbortReason = abortReason;
            TerminalUtc = terminalUtc.ToUniversalTime();
            InitializationDelayObserved = initializationDelayObserved;
            Error = error;
            DetachedInitializationOwner = detachedInitializationOwner;
        }

        public bool Succeeded => Error == null && Writer != null &&
            SealResult != null;
        public InteractionCaptureWriter Writer { get; }
        public InteractionCaptureSealResult SealResult { get; }
        public InteractionCaptureTerminalKind TerminalKind =>
            InteractionCaptureTerminalKind.Aborted;
        public string AbortReason { get; }
        public DateTimeOffset TerminalUtc { get; }
        public bool InitializationDelayObserved { get; }
        public Exception Error { get; }
        internal InteractionDetachedInitializationOwner
            DetachedInitializationOwner { get; }
    }

    /// <summary>
    /// Owns lifecycle terminalization after the Controller has stopped. The
    /// worker only touches its detached inputs and publishes one immutable
    /// result; it never reaches back into a MonoBehaviour or W1.
    /// </summary>
    public sealed class InteractionLifecycleTerminalizationJob
    {
        public const int InitializationPollMilliseconds = 50;
        public const int InitializationDelayDiagnosticMilliseconds = 5000;
        public const int SealPollMilliseconds = 50;

        private readonly InteractionBackgroundHandoff<
            InteractionLifecycleTerminalizationResult> handoff;
        private readonly InteractionLifecycleTerminalizationProgress progress;
        private readonly InteractionCaptureWriter fallbackWriter;
        private readonly InteractionBackgroundOperation<InteractionCaptureWriter>
            fallbackInitialization;
        private readonly string fallbackAbortReason;
        private readonly DateTimeOffset fallbackUtc;

        private InteractionLifecycleTerminalizationJob(
            InteractionBackgroundHandoff<
                InteractionLifecycleTerminalizationResult> handoff,
            InteractionLifecycleTerminalizationProgress progress,
            InteractionCaptureWriter fallbackWriter,
            InteractionBackgroundOperation<InteractionCaptureWriter>
                fallbackInitialization,
            string fallbackAbortReason,
            DateTimeOffset fallbackUtc)
        {
            this.handoff = handoff ??
                throw new ArgumentNullException(nameof(handoff));
            this.progress = progress ??
                throw new ArgumentNullException(nameof(progress));
            this.fallbackWriter = fallbackWriter;
            this.fallbackInitialization = fallbackInitialization;
            this.fallbackAbortReason = fallbackAbortReason;
            this.fallbackUtc = fallbackUtc;
        }

        public bool IsCompleted => handoff.IsCompleted;
        public int CallingThreadId => handoff.CallingThreadId;
        public int WorkerThreadId => handoff.WorkerThreadId;
        public bool IsAwaitingInitialization =>
            progress.IsAwaitingInitialization;
        public bool InitializationDelayObserved =>
            progress.InitializationDelayObserved;

        public static InteractionLifecycleTerminalizationJob Start(
            InteractionCaptureWriter writer,
            InteractionBackgroundOperation<InteractionCaptureWriter>
                initialization,
            InteractionSummaryTracker detachedSummary,
            string abortReason,
            double monotonicTimeSeconds,
            DateTimeOffset utcTime,
            int frame)
        {
            return Start(
                writer,
                initialization,
                detachedSummary,
                abortReason,
                monotonicTimeSeconds,
                utcTime,
                frame,
                InteractionThreadPoolBackgroundWorkQueue.Shared
            );
        }

        internal static InteractionLifecycleTerminalizationJob Start(
            InteractionCaptureWriter writer,
            InteractionBackgroundOperation<InteractionCaptureWriter>
                initialization,
            InteractionSummaryTracker detachedSummary,
            string abortReason,
            double monotonicTimeSeconds,
            DateTimeOffset utcTime,
            int frame,
            IInteractionBackgroundWorkQueue workQueue)
        {
            if (writer == null && initialization == null)
            {
                throw new ArgumentException(
                    "Lifecycle terminalization requires a writer or its initialization."
                );
            }
            if (detachedSummary == null)
            {
                throw new ArgumentNullException(nameof(detachedSummary));
            }
            if (string.IsNullOrWhiteSpace(abortReason))
            {
                throw new ArgumentException(
                    "Lifecycle abort reason is required.",
                    nameof(abortReason)
                );
            }
            InteractionEventSequencer.ValidateFiniteNonNegative(
                monotonicTimeSeconds,
                nameof(monotonicTimeSeconds)
            );
            if (frame < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frame));
            }
            if (workQueue == null)
            {
                throw new ArgumentNullException(nameof(workQueue));
            }
            string safeReason = abortReason.Trim();
            DateTimeOffset safeUtc = utcTime.ToUniversalTime();
            var progress = new InteractionLifecycleTerminalizationProgress(
                writer == null
            );
            InteractionBackgroundHandoff<InteractionLifecycleTerminalizationResult>
                ownedHandoff = InteractionBackgroundHandoff<
                    InteractionLifecycleTerminalizationResult>.Start(() =>
                        Execute(
                            writer,
                            initialization,
                            detachedSummary,
                            safeReason,
                            monotonicTimeSeconds,
                            safeUtc,
                            frame,
                            progress
                        ),
                    workQueue
                );
            if (ownedHandoff.IsCompleted && ownedHandoff.Error != null)
            {
                Exception queueFailure = ownedHandoff.Error;
                InteractionCaptureWriter immediatelyOwned = writer;
                InteractionDetachedInitializationOwner detachedOwner =
                    AdoptInitializationAfterQueueFailure(
                        immediatelyOwned,
                        initialization,
                        progress
                    );
                if (immediatelyOwned != null)
                {
                    immediatelyOwned.Dispose();
                }
                ownedHandoff = InteractionBackgroundHandoff<
                    InteractionLifecycleTerminalizationResult>.FromCompletion(
                        Failure(
                            immediatelyOwned,
                            safeReason,
                            safeUtc,
                            progress.InitializationDelayObserved,
                            new IOException(
                                "Lifecycle terminalization queue rejected ownership; " +
                                "partial capture was retained.",
                                queueFailure
                            ),
                            detachedOwner
                        )
                    );
            }
            return new InteractionLifecycleTerminalizationJob(
                ownedHandoff,
                progress,
                writer,
                initialization,
                safeReason,
                safeUtc
            );
        }

        public bool Wait(TimeSpan timeout)
        {
            return handoff.Wait(timeout);
        }

        public bool TryConsume(
            out InteractionLifecycleTerminalizationResult result)
        {
            result = null;
            if (!IsCompleted || !handoff.TryConsumeCompletion(
                    out result,
                    out Exception handoffFailure))
            {
                return false;
            }
            if (handoffFailure == null)
            {
                return true;
            }

            InteractionCaptureWriter ownedWriter = fallbackWriter;
            InteractionDetachedInitializationOwner detachedOwner =
                AdoptInitializationAfterQueueFailure(
                    ownedWriter,
                    fallbackInitialization,
                    progress
                );
            ownedWriter?.Dispose();
            result = Failure(
                ownedWriter,
                fallbackAbortReason,
                fallbackUtc,
                progress.InitializationDelayObserved,
                new IOException(
                    "Lifecycle terminalization ownership could not be queued; " +
                    "partial capture was retained.",
                    handoffFailure
                ),
                detachedOwner
            );
            return true;
        }

        private static InteractionDetachedInitializationOwner
            AdoptInitializationAfterQueueFailure(
                InteractionCaptureWriter alreadyOwnedWriter,
                InteractionBackgroundOperation<InteractionCaptureWriter>
                    initialization,
                InteractionLifecycleTerminalizationProgress progress)
        {
            if (alreadyOwnedWriter != null || initialization == null)
            {
                return null;
            }
            InteractionDetachedInitializationOwner detachedOwner =
                InteractionDetachedInitializationOwner.Adopt(initialization);
            progress.CompleteInitializationWait();
            return detachedOwner;
        }

        private static InteractionLifecycleTerminalizationResult Execute(
            InteractionCaptureWriter writer,
            InteractionBackgroundOperation<InteractionCaptureWriter>
                initialization,
            InteractionSummaryTracker detachedSummary,
            string abortReason,
            double monotonicTimeSeconds,
            DateTimeOffset utcTime,
            int frame,
            InteractionLifecycleTerminalizationProgress progress)
        {
            InteractionCaptureWriter ownedWriter = writer;
            bool initializationDelayObserved = false;
            try
            {
                if (ownedWriter == null)
                {
                    while (!initialization.IsCompleted)
                    {
                        initialization.Wait(TimeSpan.FromMilliseconds(
                            InitializationPollMilliseconds
                        ));
                    }
                    initializationDelayObserved =
                        progress.CompleteInitializationWait();
                    if (!initialization.Succeeded)
                    {
                        return Failure(
                            null,
                            abortReason,
                            utcTime,
                            initializationDelayObserved,
                            new IOException(
                                "Capture initialization failed during lifecycle terminalization.",
                                initialization.Error
                            )
                        );
                    }
                    ownedWriter = initialization.GetResult();
                }

                ownedWriter.RecordEvent(
                    InteractionEventNames.RunAborted,
                    null,
                    monotonicTimeSeconds,
                    utcTime,
                    frame,
                    null,
                    null,
                    BuildReasonPayload(abortReason)
                );
                InteractionBackgroundOperation<InteractionCaptureSealResult>
                    seal = ownedWriter.BeginSeal(
                        InteractionCaptureTerminalKind.Aborted,
                        completeness => detachedSummary.SealAborted(
                            monotonicTimeSeconds,
                            utcTime,
                            abortReason,
                            completeness
                        )
                    );
                while (!seal.IsCompleted)
                {
                    seal.Wait(TimeSpan.FromMilliseconds(
                        SealPollMilliseconds
                    ));
                }
                if (!seal.Succeeded)
                {
                    return Failure(
                        ownedWriter,
                        abortReason,
                        utcTime,
                        initializationDelayObserved,
                        new IOException(
                            "Lifecycle Abort seal failed; local data remains retained.",
                            seal.Error
                        )
                    );
                }
                return new InteractionLifecycleTerminalizationResult(
                    ownedWriter,
                    seal.GetResult(),
                    abortReason,
                    utcTime,
                    initializationDelayObserved,
                    null,
                    null
                );
            }
            catch (Exception exception)
            {
                ownedWriter?.Dispose();
                return Failure(
                    ownedWriter,
                    abortReason,
                    utcTime,
                    initializationDelayObserved ||
                        progress.InitializationDelayObserved,
                    exception
                );
            }
        }

        private static InteractionLifecycleTerminalizationResult Failure(
            InteractionCaptureWriter writer,
            string abortReason,
            DateTimeOffset utcTime,
            bool initializationDelayObserved,
            Exception error,
            InteractionDetachedInitializationOwner detachedInitializationOwner =
                null)
        {
            return new InteractionLifecycleTerminalizationResult(
                writer,
                null,
                abortReason,
                utcTime,
                initializationDelayObserved,
                error ?? new IOException(
                    "Lifecycle terminalization failed without an error."
                ),
                detachedInitializationOwner
            );
        }

        private sealed class InteractionLifecycleTerminalizationProgress
        {
            private readonly Stopwatch ownershipClock = Stopwatch.StartNew();
            private int awaitingInitialization;
            private int initializationDelayObserved;

            public InteractionLifecycleTerminalizationProgress(
                bool awaitingInitialization)
            {
                this.awaitingInitialization = awaitingInitialization ? 1 : 0;
            }

            public bool IsAwaitingInitialization =>
                Volatile.Read(ref awaitingInitialization) != 0;

            public bool InitializationDelayObserved
            {
                get
                {
                    if (Volatile.Read(ref initializationDelayObserved) != 0)
                    {
                        return true;
                    }
                    if (!IsAwaitingInitialization ||
                        ownershipClock.ElapsedMilliseconds <
                            InitializationDelayDiagnosticMilliseconds)
                    {
                        return false;
                    }
                    Interlocked.Exchange(
                        ref initializationDelayObserved,
                        1
                    );
                    return true;
                }
            }

            public bool CompleteInitializationWait()
            {
                bool delayed = InitializationDelayObserved;
                Volatile.Write(ref awaitingInitialization, 0);
                return delayed;
            }
        }

        private static string BuildReasonPayload(string reason)
        {
            var builder = new System.Text.StringBuilder();
            builder.Append('{');
            InteractionRunManifestContractV1.AppendName(builder, "reason");
            InteractionJson.AppendQuoted(builder, reason);
            builder.Append('}');
            return builder.ToString();
        }
    }

    internal sealed class InteractionSerialBackgroundScheduler
    {
        private readonly object gate = new object();
        private readonly Queue<Action> pending = new Queue<Action>();
        private readonly Thread worker;
        private bool accepting = true;
        private bool stopRequested;

        public InteractionSerialBackgroundScheduler(string workerName)
        {
            if (string.IsNullOrWhiteSpace(workerName))
            {
                throw new ArgumentException(
                    "Background worker name is required.",
                    nameof(workerName)
                );
            }
            worker = new Thread(WorkLoop)
            {
                IsBackground = true,
                Name = workerName
            };
            worker.Start();
        }

        public InteractionBackgroundOperation<T> Enqueue<T>(
            Func<T> work,
            bool terminal = false)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }
            var operation = InteractionBackgroundOperation<T>.CreatePending();
            lock (gate)
            {
                if (!accepting)
                {
                    throw new InvalidOperationException(
                        "The serial background scheduler is closing."
                    );
                }
                pending.Enqueue(() => operation.Execute(work));
                if (terminal)
                {
                    accepting = false;
                    stopRequested = true;
                }
                Monitor.PulseAll(gate);
            }
            return operation;
        }

        public void StopAcceptingWithoutJoin()
        {
            lock (gate)
            {
                accepting = false;
                stopRequested = true;
                Monitor.PulseAll(gate);
            }
        }

        private void WorkLoop()
        {
            while (true)
            {
                Action work;
                lock (gate)
                {
                    while (pending.Count == 0 && !stopRequested)
                    {
                        Monitor.Wait(gate, 50);
                    }
                    if (pending.Count == 0 && stopRequested)
                    {
                        return;
                    }
                    work = pending.Dequeue();
                }
                work();
            }
        }
    }

    public interface IInteractionCancelableRequest
    {
        void Abort();
    }

    /// <summary>
    /// Owns active boundary requests so lifecycle shutdown can abort them even
    /// when the coroutine that created the request is stopped.
    /// </summary>
    public sealed class InteractionRequestCancellationRegistry
    {
        private readonly object gate = new object();
        private readonly HashSet<IInteractionCancelableRequest> active =
            new HashSet<IInteractionCancelableRequest>();

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

        public void Register(IInteractionCancelableRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            lock (gate)
            {
                active.Add(request);
            }
        }

        public bool Unregister(IInteractionCancelableRequest request)
        {
            if (request == null)
            {
                return false;
            }
            lock (gate)
            {
                return active.Remove(request);
            }
        }

        public int CancelAll()
        {
            IInteractionCancelableRequest[] snapshot;
            lock (gate)
            {
                snapshot = new IInteractionCancelableRequest[active.Count];
                active.CopyTo(snapshot);
                active.Clear();
            }
            for (int index = 0; index < snapshot.Length; index++)
            {
                try
                {
                    snapshot[index].Abort();
                }
                catch
                {
                    // Continue cancelling the remaining boundary requests.
                }
            }
            return snapshot.Length;
        }
    }

    public sealed class InteractionLifecycleShutdownGate
    {
        private readonly object gate = new object();

        public bool IsShutdownInitiated { get; private set; }
        public string Reason { get; private set; }

        public bool TryBegin(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException(
                    "Lifecycle shutdown reason is required.",
                    nameof(reason)
                );
            }
            lock (gate)
            {
                if (IsShutdownInitiated)
                {
                    return false;
                }
                IsShutdownInitiated = true;
                Reason = reason.Trim();
                return true;
            }
        }

        public void Reset()
        {
            lock (gate)
            {
                IsShutdownInitiated = false;
                Reason = null;
            }
        }
    }
}
