using System;
using System.IO;
using System.Text;

namespace SignVR.Interaction.CaptureHost
{
    public sealed class InteractionCaptureBudgetException : IOException
    {
        public InteractionCaptureBudgetException(string message)
            : base(message)
        {
        }
    }

    public sealed class InteractionCaptureCompletenessException : IOException
    {
        public InteractionCaptureCompletenessException(string message)
            : base(message)
        {
        }
    }

    public sealed class InteractionJsonlBudget
    {
        public InteractionJsonlBudget(
            long maxLineBytes,
            long maxPendingBytes,
            long maxFileBytes)
        {
            if (maxLineBytes < 2L || maxPendingBytes < maxLineBytes ||
                maxFileBytes < maxLineBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxLineBytes),
                    "JSONL byte budgets must be positive and internally consistent."
                );
            }
            MaxLineBytes = maxLineBytes;
            MaxPendingBytes = maxPendingBytes;
            MaxFileBytes = maxFileBytes;
        }

        public long MaxLineBytes { get; }
        public long MaxPendingBytes { get; }
        public long MaxFileBytes { get; }
    }

    public sealed class InteractionJsonlReservation
    {
        internal InteractionJsonlReservation(
            InteractionJsonlBudgetTracker owner,
            long byteCount)
        {
            Owner = owner;
            ByteCount = byteCount;
        }

        internal InteractionJsonlBudgetTracker Owner { get; }
        internal bool Released { get; set; }
        public long ByteCount { get; }
    }

    public sealed class InteractionJsonlBudgetTracker
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private readonly object gate = new object();
        private readonly InteractionJsonlBudget budget;

        public InteractionJsonlBudgetTracker(InteractionJsonlBudget budget)
        {
            this.budget = budget ?? throw new ArgumentNullException(nameof(budget));
        }

        public long PendingBytes { get; private set; }
        public long AcceptedFileBytes { get; private set; }

        public InteractionJsonlReservation Reserve(string line)
        {
            if (string.IsNullOrEmpty(line) || line.IndexOf('\n') >= 0 ||
                line.IndexOf('\r') >= 0)
            {
                throw new ArgumentException(
                    "JSONL records must be one non-empty line.",
                    nameof(line)
                );
            }
            long bytes = checked((long)Utf8.GetByteCount(line) + 1L);
            lock (gate)
            {
                if (bytes > budget.MaxLineBytes)
                {
                    throw new InteractionCaptureBudgetException(
                        "JSONL record exceeds the single-line UTF-8 byte limit."
                    );
                }
                if (checked(PendingBytes + bytes) > budget.MaxPendingBytes)
                {
                    throw new InteractionCaptureBudgetException(
                        "JSONL pending UTF-8 byte budget is exhausted."
                    );
                }
                if (checked(AcceptedFileBytes + bytes) > budget.MaxFileBytes)
                {
                    throw new InteractionCaptureBudgetException(
                        "JSONL artifact would exceed its local file byte limit."
                    );
                }
                PendingBytes += bytes;
                AcceptedFileBytes += bytes;
                return new InteractionJsonlReservation(this, bytes);
            }
        }

        public void MarkWritten(InteractionJsonlReservation reservation)
        {
            ReleasePending(reservation, rollbackFile: false);
        }

        public void Rollback(InteractionJsonlReservation reservation)
        {
            ReleasePending(reservation, rollbackFile: true);
        }

        private void ReleasePending(
            InteractionJsonlReservation reservation,
            bool rollbackFile)
        {
            if (reservation == null || reservation.Owner != this)
            {
                throw new ArgumentException(
                    "JSONL reservation belongs to another tracker.",
                    nameof(reservation)
                );
            }
            lock (gate)
            {
                if (reservation.Released)
                {
                    throw new InvalidOperationException(
                        "JSONL reservation was already released."
                    );
                }
                PendingBytes = checked(PendingBytes - reservation.ByteCount);
                if (rollbackFile)
                {
                    AcceptedFileBytes = checked(
                        AcceptedFileBytes - reservation.ByteCount
                    );
                }
                reservation.Released = true;
            }
        }
    }

    public interface IInteractionFreeSpaceProbe
    {
        long GetAvailableBytes(string path);
    }

    public sealed class InteractionDriveFreeSpaceProbe : IInteractionFreeSpaceProbe
    {
        public long GetAvailableBytes(string path)
        {
            string fullPath = Path.GetFullPath(
                path ?? throw new ArgumentNullException(nameof(path))
            );
#if UNITY_ANDROID && !UNITY_EDITOR
            return GetAndroidAvailableBytes(
                ResolveExistingProbePath(
                    SelectProbePath(fullPath, pathAware: true)
                )
            );
#else
            string root = SelectProbePath(fullPath, pathAware: false);
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new IOException("Capture path has no storage root.");
            }
            return new DriveInfo(root).AvailableFreeSpace;
#endif
        }

        internal static string SelectProbePath(
            string fullPath,
            bool pathAware)
        {
            if (pathAware)
            {
                return fullPath;
            }
            return Path.GetPathRoot(fullPath);
        }

        internal static string ResolveExistingProbePath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                throw new ArgumentException(
                    "Capture probe path is required.",
                    nameof(fullPath)
                );
            }

            string candidate = Path.GetFullPath(fullPath);
            while (!Directory.Exists(candidate))
            {
                DirectoryInfo parent = Directory.GetParent(candidate);
                if (parent == null || string.Equals(
                        parent.FullName,
                        candidate,
                        StringComparison.Ordinal))
                {
                    throw new IOException(
                        "Capture path has no existing storage ancestor."
                    );
                }
                candidate = parent.FullName;
            }
            return candidate;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static long GetAndroidAvailableBytes(string fullPath)
        {
            int attachResult = UnityEngine.AndroidJNI.AttachCurrentThread();
            if (attachResult < 0)
            {
                throw new IOException(
                    "Could not attach the capture worker to Android's JVM."
                );
            }

            try
            {
                using (var statFs = new UnityEngine.AndroidJavaObject(
                    "android.os.StatFs",
                    fullPath
                ))
                {
                    return statFs.Call<long>("getAvailableBytes");
                }
            }
            catch (UnityEngine.AndroidJavaException exception)
            {
                throw new IOException(
                    "Android StatFs could not inspect the capture path.",
                    exception
                );
            }
            finally
            {
                UnityEngine.AndroidJNI.DetachCurrentThread();
            }
        }
#endif
    }

    public sealed class InteractionDiskBudgetGuard
    {
        private readonly object gate = new object();
        private readonly string path;
        private readonly long minimumFreeBytes;
        private readonly IInteractionFreeSpaceProbe probe;
        private long remainingReservedHeadroom = -1L;

        public InteractionDiskBudgetGuard(
            string path,
            long minimumFreeBytes,
            IInteractionFreeSpaceProbe probe)
        {
            this.path = Path.GetFullPath(
                path ?? throw new ArgumentNullException(nameof(path))
            );
            if (minimumFreeBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumFreeBytes));
            }
            this.minimumFreeBytes = minimumFreeBytes;
            this.probe = probe ?? throw new ArgumentNullException(nameof(probe));
        }

        public long MinimumFreeBytes => minimumFreeBytes;

        public void EnsureMinimumAvailable()
        {
            long available;
            try
            {
                available = probe.GetAvailableBytes(path);
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException ||
                exception is ArgumentException || exception is NotSupportedException)
            {
                throw new InteractionCaptureBudgetException(
                    "Available capture storage could not be determined: " +
                    exception.Message
                );
            }
            if (available < minimumFreeBytes)
            {
                throw new InteractionCaptureBudgetException(
                    "Available capture storage is below the minimum free-space watermark."
                );
            }
            lock (gate)
            {
                if (remainingReservedHeadroom < 0L)
                {
                    remainingReservedHeadroom = available - minimumFreeBytes;
                }
            }
        }

        public void Reserve(long bytes)
        {
            if (bytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(bytes));
            }
            lock (gate)
            {
                if (remainingReservedHeadroom < 0L)
                {
                    throw new InvalidOperationException(
                        "Disk budget must be initialized before reserving bytes."
                    );
                }
                if (bytes > remainingReservedHeadroom)
                {
                    throw new InteractionCaptureBudgetException(
                        "Capture would cross the minimum free-space watermark."
                    );
                }
                remainingReservedHeadroom -= bytes;
            }
        }
    }

    public sealed class InteractionCaptureBudgetPolicy
    {
        public const long DefaultMinimumFreeBytes = 512L * 1024L * 1024L;
        public const long DefaultMaxLineBytes = 1024L * 1024L;
        public const long DefaultPendingBytes = 8L * 1024L * 1024L;

        public InteractionCaptureBudgetPolicy(
            InteractionJsonlBudget events,
            InteractionJsonlBudget poses,
            InteractionJsonlBudget objects,
            long summaryMaxBytes,
            long minimumFreeBytes,
            IInteractionFreeSpaceProbe freeSpaceProbe)
        {
            Events = events ?? throw new ArgumentNullException(nameof(events));
            Poses = poses ?? throw new ArgumentNullException(nameof(poses));
            Objects = objects ?? throw new ArgumentNullException(nameof(objects));
            if (summaryMaxBytes <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(summaryMaxBytes));
            }
            if (minimumFreeBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumFreeBytes));
            }
            SummaryMaxBytes = summaryMaxBytes;
            MinimumFreeBytes = minimumFreeBytes;
            FreeSpaceProbe = freeSpaceProbe ??
                throw new ArgumentNullException(nameof(freeSpaceProbe));
        }

        public InteractionJsonlBudget Events { get; }
        public InteractionJsonlBudget Poses { get; }
        public InteractionJsonlBudget Objects { get; }
        public long SummaryMaxBytes { get; }
        public long MinimumFreeBytes { get; }
        public IInteractionFreeSpaceProbe FreeSpaceProbe { get; }

        public static InteractionCaptureBudgetPolicy CreateDefault()
        {
            return new InteractionCaptureBudgetPolicy(
                new InteractionJsonlBudget(
                    DefaultMaxLineBytes,
                    DefaultPendingBytes,
                    InteractionLocalArtifactSizeLimits.MaximumBytesFor(
                        InteractionLocalArtifactTypes.Events
                    )
                ),
                new InteractionJsonlBudget(
                    DefaultMaxLineBytes,
                    DefaultPendingBytes,
                    InteractionLocalArtifactSizeLimits.MaximumBytesFor(
                        InteractionLocalArtifactTypes.Poses
                    )
                ),
                new InteractionJsonlBudget(
                    DefaultMaxLineBytes,
                    DefaultPendingBytes,
                    InteractionLocalArtifactSizeLimits.MaximumBytesFor(
                        InteractionLocalArtifactTypes.Objects
                    )
                ),
                InteractionLocalArtifactSizeLimits.MaximumBytesFor(
                    InteractionLocalArtifactTypes.Summary
                ),
                DefaultMinimumFreeBytes,
                new InteractionDriveFreeSpaceProbe()
            );
        }
    }
}
