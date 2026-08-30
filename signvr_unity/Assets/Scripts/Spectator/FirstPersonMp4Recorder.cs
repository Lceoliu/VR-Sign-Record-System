using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SignVR.Streaming
{
    /// <summary>
    /// Queues RGB24 capture frames and encodes them to a single H.264 MP4 on
    /// Android through MediaCodec/MediaMuxer. The editor fallback only keeps
    /// counters so capture tests do not require an Android codec.
    /// </summary>
    public sealed class FirstPersonMp4Recorder : IDisposable
    {
        private const int MaxQueuedFrames = 4;
        private const int DefaultBitrate = 16_000_000;

        private readonly object queueLock = new object();
        private readonly Queue<PendingFrame> frames = new Queue<PendingFrame>();
        private readonly AutoResetEvent frameReady = new AutoResetEvent(false);
        private readonly string outputPath;
        private readonly int width;
        private readonly int height;
        private readonly int frameRate;
        private readonly int bitrate;

        private Thread writerThread;
        private volatile bool accepting;
        private volatile string lastError = string.Empty;
        private long framesWritten;
        private long framesDropped;
        private long framesSubmitted;

#if UNITY_ANDROID && !UNITY_EDITOR
        private UnityEngine.AndroidJavaClass nativeRecorder;
#endif

        public FirstPersonMp4Recorder(
            string outputDirectory,
            string outputFileStem,
            int frameWidth,
            int frameHeight,
            int framesPerSecond,
            int videoBitrate = DefaultBitrate)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException(
                    "An output directory is required.",
                    nameof(outputDirectory));
            }
            if (string.IsNullOrWhiteSpace(outputFileStem))
            {
                throw new ArgumentException(
                    "An output file stem is required.",
                    nameof(outputFileStem));
            }
            if (frameWidth <= 0 || frameHeight <= 0 || framesPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frameWidth),
                    "Frame dimensions and frame rate must be positive.");
            }

            string directory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(directory);
            outputPath = Path.Combine(directory, outputFileStem + ".mp4");
            width = frameWidth;
            height = frameHeight;
            frameRate = framesPerSecond;
            bitrate = Math.Max(1_000_000, videoBitrate);
        }

        public string OutputDirectory => Path.GetDirectoryName(outputPath);
        public string CurrentPath => outputPath;
        public string LastError => lastError;
        public long FramesWritten => Interlocked.Read(ref framesWritten);
        public long FramesDropped => Interlocked.Read(ref framesDropped);
        public bool IsRecording => accepting;

        public void Start()
        {
            if (accepting || writerThread != null)
            {
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                nativeRecorder = new UnityEngine.AndroidJavaClass(
                    "com.signvr.recording.SignVRMp4Recorder");
                bool started = nativeRecorder.CallStatic<bool>(
                    "start",
                    outputPath,
                    width,
                    height,
                    frameRate,
                    bitrate);
                if (!started)
                {
                    lastError = nativeRecorder.CallStatic<string>("getLastError") ??
                        "Android MediaCodec could not start.";
                    nativeRecorder.Dispose();
                    nativeRecorder = null;
                    return;
                }
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
                nativeRecorder?.Dispose();
                nativeRecorder = null;
                return;
            }
#endif

            accepting = true;
            lastError = string.Empty;
            writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "SignVR First-Person MP4 Writer"
            };
            writerThread.Start();
        }

        public bool Enqueue(byte[] rgb24)
        {
            if (!accepting || rgb24 == null || rgb24.Length != width * height * 3)
            {
                if (accepting)
                {
                    Interlocked.Increment(ref framesDropped);
                }
                return false;
            }

            long submittedIndex = Interlocked.Increment(ref framesSubmitted) - 1L;
            long timestampUs = checked(submittedIndex * 1_000_000L / frameRate);
            lock (queueLock)
            {
                if (!accepting || frames.Count >= MaxQueuedFrames)
                {
                    Interlocked.Increment(ref framesDropped);
                    return false;
                }

                frames.Enqueue(new PendingFrame(rgb24, timestampUs));
            }

            frameReady.Set();
            return true;
        }

        public void Stop()
        {
            accepting = false;
            frameReady.Set();

            Thread thread = writerThread;
            if (thread != null && thread.IsAlive && !thread.Join(15_000))
            {
                lastError = "MP4 writer did not stop within 15 seconds.";
                return;
            }

            writerThread = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            nativeRecorder?.Dispose();
            nativeRecorder = null;
#endif
        }

        public void Dispose()
        {
            Stop();
            frameReady.Dispose();
        }

        private void WriterLoop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            UnityEngine.AndroidJNI.AttachCurrentThread();
#endif
            try
            {
                while (accepting || HasQueuedFrames())
                {
                    if (!TryDequeue(out PendingFrame frame))
                    {
                        frameReady.WaitOne(250);
                        continue;
                    }

#if UNITY_ANDROID && !UNITY_EDITOR
                    bool encoded = nativeRecorder != null &&
                        nativeRecorder.CallStatic<bool>(
                            "encodeRgb",
                            frame.Rgb24,
                            frame.TimestampUs);
                    if (!encoded)
                    {
                        lastError = nativeRecorder == null
                            ? "Android MediaCodec is unavailable."
                            : nativeRecorder.CallStatic<string>("getLastError") ??
                                "Android MediaCodec rejected a frame.";
                        accepting = false;
                        ClearQueueAsDropped();
                        break;
                    }
#endif
                    Interlocked.Increment(ref framesWritten);
                }
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
                accepting = false;
                ClearQueueAsDropped();
            }
            finally
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    nativeRecorder?.CallStatic("stop");
                }
                catch (Exception exception)
                {
                    if (string.IsNullOrEmpty(lastError))
                    {
                        lastError = exception.Message;
                    }
                }
                UnityEngine.AndroidJNI.DetachCurrentThread();
#endif
            }
        }

        private bool HasQueuedFrames()
        {
            lock (queueLock)
            {
                return frames.Count > 0;
            }
        }

        private bool TryDequeue(out PendingFrame frame)
        {
            lock (queueLock)
            {
                if (frames.Count == 0)
                {
                    frame = default;
                    return false;
                }

                frame = frames.Dequeue();
                return true;
            }
        }

        private void ClearQueueAsDropped()
        {
            lock (queueLock)
            {
                Interlocked.Add(ref framesDropped, frames.Count);
                frames.Clear();
            }
        }

        private readonly struct PendingFrame
        {
            public PendingFrame(byte[] rgb24, long timestampUs)
            {
                Rgb24 = rgb24;
                TimestampUs = timestampUs;
            }

            public byte[] Rgb24 { get; }
            public long TimestampUs { get; }
        }
    }
}
