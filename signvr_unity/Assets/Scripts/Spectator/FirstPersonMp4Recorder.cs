using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Unity.Collections;

namespace SignVR.Streaming
{
    /// <summary>
    /// Queues GPU-converted NV12 frames and encodes them to H.264 MP4. Stop is
    /// deliberately non-blocking; the writer thread owns codec finalization.
    /// </summary>
    public sealed class FirstPersonMp4Recorder : IDisposable
    {
        private const int MaxQueuedFrames = 4;
        private const int DefaultBitrate = 16_000_000;

        private readonly object queueLock = new object();
        private readonly Queue<PendingFrame> frames = new Queue<PendingFrame>();
        private readonly Queue<byte[]> availableFrameBuffers =
            new Queue<byte[]>();
        private readonly AutoResetEvent frameReady = new AutoResetEvent(false);
        private readonly string outputPath;
        private readonly int width;
        private readonly int height;
        private readonly int frameRate;
        private readonly int bitrate;
        private readonly int nv12ByteCount;
        private readonly IFirstPersonMp4Backend backend;

        private Thread writerThread;
        private volatile bool accepting;
        private volatile bool finalized = true;
        private int disposeRequested;
        private volatile string lastError = string.Empty;
        private int signalDisposed;
        private int backendDisposed;
        private long framesWritten;
        private long framesDropped;
        private long framesSubmitted;

        public FirstPersonMp4Recorder(
            string outputDirectory,
            string outputFileStem,
            int frameWidth,
            int frameHeight,
            int framesPerSecond,
            int videoBitrate = DefaultBitrate)
            : this(
                outputDirectory,
                outputFileStem,
                frameWidth,
                frameHeight,
                framesPerSecond,
                videoBitrate,
                FirstPersonMp4BackendFactory.Create())
        {
        }

        internal FirstPersonMp4Recorder(
            string outputDirectory,
            string outputFileStem,
            int frameWidth,
            int frameHeight,
            int framesPerSecond,
            int videoBitrate,
            IFirstPersonMp4Backend configuredBackend)
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
            if ((frameWidth & 3) != 0 || (frameHeight & 1) != 0)
            {
                throw new ArgumentException(
                    "NV12 recording requires width divisible by four and even height.",
                    nameof(frameWidth));
            }

            string directory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(directory);
            outputPath = Path.Combine(directory, outputFileStem + ".mp4");
            width = frameWidth;
            height = frameHeight;
            frameRate = framesPerSecond;
            bitrate = Math.Max(1_000_000, videoBitrate);
            nv12ByteCount = ExpectedNv12ByteCount(width, height);
            backend = configuredBackend ?? throw new ArgumentNullException(
                nameof(configuredBackend));
        }

        public string OutputDirectory => Path.GetDirectoryName(outputPath);
        public string CurrentPath => outputPath;
        public string LastError => lastError;
        public string EncoderName => backend.Name;
        public long FramesWritten => Interlocked.Read(ref framesWritten);
        public long FramesDropped => Interlocked.Read(ref framesDropped);
        public bool IsRecording => accepting;
        public bool IsFinalized => finalized;

        internal static int ExpectedNv12ByteCount(int frameWidth, int frameHeight)
        {
            return checked(frameWidth * frameHeight * 3 / 2);
        }

        public void Start()
        {
            if (
                accepting ||
                writerThread != null ||
                Volatile.Read(ref disposeRequested) != 0
            )
            {
                return;
            }

            try
            {
                if (!backend.Start(
                        outputPath,
                        width,
                        height,
                        frameRate,
                        bitrate))
                {
                    lastError = string.IsNullOrWhiteSpace(backend.LastError)
                        ? "Android MediaCodec could not start."
                        : backend.LastError;
                    return;
                }
                for (int index = 0; index < MaxQueuedFrames; index++)
                {
                    availableFrameBuffers.Enqueue(new byte[nv12ByteCount]);
                }
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
                return;
            }

            accepting = true;
            finalized = false;
            lastError = string.Empty;
            writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "SignVR First-Person MP4 Writer"
            };
            writerThread.Start();
        }

        public bool Enqueue(byte[] nv12, int repeatedFrameCount = 1)
        {
            if (
                nv12 == null ||
                nv12.Length != nv12ByteCount ||
                repeatedFrameCount <= 0
            )
            {
                if (accepting && repeatedFrameCount > 0)
                {
                    Interlocked.Add(ref framesDropped, repeatedFrameCount);
                }
                return false;
            }
            if (!TryReserveFrameBatch(repeatedFrameCount, out long firstFrameIndex))
            {
                return false;
            }

            lock (queueLock)
            {
                if (
                    !accepting ||
                    availableFrameBuffers.Count == 0
                )
                {
                    Interlocked.Add(ref framesDropped, repeatedFrameCount);
                    return false;
                }

                byte[] buffer = availableFrameBuffers.Dequeue();
                Buffer.BlockCopy(nv12, 0, buffer, 0, nv12ByteCount);
                frames.Enqueue(new PendingFrame(
                    buffer,
                    firstFrameIndex,
                    repeatedFrameCount
                ));
            }

            SignalWriter();
            return true;
        }

        internal bool Enqueue(
            NativeArray<byte> nv12,
            int repeatedFrameCount = 1)
        {
            if (
                !nv12.IsCreated ||
                nv12.Length != nv12ByteCount ||
                repeatedFrameCount <= 0
            )
            {
                if (accepting && repeatedFrameCount > 0)
                {
                    Interlocked.Add(ref framesDropped, repeatedFrameCount);
                }
                return false;
            }
            if (!TryReserveFrameBatch(repeatedFrameCount, out long firstFrameIndex))
            {
                return false;
            }

            lock (queueLock)
            {
                if (
                    !accepting ||
                    availableFrameBuffers.Count == 0
                )
                {
                    Interlocked.Add(ref framesDropped, repeatedFrameCount);
                    return false;
                }

                byte[] buffer = availableFrameBuffers.Dequeue();
                nv12.CopyTo(buffer);
                frames.Enqueue(new PendingFrame(
                    buffer,
                    firstFrameIndex,
                    repeatedFrameCount
                ));
            }

            SignalWriter();
            return true;
        }

        internal bool Enqueue(uint[] nv12Words, int repeatedFrameCount = 1)
        {
            if (
                nv12Words == null ||
                nv12Words.Length * sizeof(uint) != nv12ByteCount ||
                repeatedFrameCount <= 0
            )
            {
                if (accepting && repeatedFrameCount > 0)
                {
                    Interlocked.Add(ref framesDropped, repeatedFrameCount);
                }
                return false;
            }
            if (!TryReserveFrameBatch(repeatedFrameCount, out long firstFrameIndex))
            {
                return false;
            }

            lock (queueLock)
            {
                if (
                    !accepting ||
                    availableFrameBuffers.Count == 0
                )
                {
                    Interlocked.Add(ref framesDropped, repeatedFrameCount);
                    return false;
                }

                byte[] buffer = availableFrameBuffers.Dequeue();
                Buffer.BlockCopy(
                    nv12Words,
                    0,
                    buffer,
                    0,
                    nv12ByteCount
                );
                frames.Enqueue(new PendingFrame(
                    buffer,
                    firstFrameIndex,
                    repeatedFrameCount
                ));
            }

            SignalWriter();
            return true;
        }

        /// <summary>
        /// Requests codec finalization and returns without waiting for the
        /// writer thread. Poll IsFinalized before consuming the completed file.
        /// </summary>
        public void Stop()
        {
            accepting = false;
            SignalWriter();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposeRequested, 1) != 0)
            {
                return;
            }

            Stop();
            if (finalized || writerThread == null)
            {
                DisposeSignal();
                DisposeBackendOnce();
            }
        }

        private void WriterLoop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            bool jniAttached = false;
#endif
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                UnityEngine.AndroidJNI.AttachCurrentThread();
                jniAttached = true;
#endif
                while (accepting || HasQueuedFrames())
                {
                    if (!TryDequeue(out PendingFrame frame))
                    {
                        frameReady.WaitOne(250);
                        continue;
                    }

                    bool frameFailed = false;
                    for (int index = 0; index < frame.RepeatCount; index++)
                    {
                        long timestampUs = TimestampForFrameIndex(
                            frame.FirstFrameIndex + index
                        );
                        if (!backend.EncodeNv12(frame.Nv12, timestampUs))
                        {
                            lastError = string.IsNullOrWhiteSpace(backend.LastError)
                                ? "Android MediaCodec rejected an NV12 frame."
                                : backend.LastError;
                            Interlocked.Add(
                                ref framesDropped,
                                frame.RepeatCount - index
                            );
                            accepting = false;
                            frameFailed = true;
                            break;
                        }
                        Interlocked.Increment(ref framesWritten);
                    }
                    ReturnFrameBuffer(frame.Nv12);
                    if (frameFailed)
                    {
                        ClearQueueAsDropped();
                        break;
                    }
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
                try
                {
                    backend.Stop();
                    if (
                        string.IsNullOrEmpty(lastError) &&
                        !string.IsNullOrWhiteSpace(backend.LastError)
                    )
                    {
                        lastError = backend.LastError;
                    }
                }
                catch (Exception exception)
                {
                    if (string.IsNullOrEmpty(lastError))
                    {
                        lastError = exception.Message;
                    }
                }
                try
                {
                    DisposeBackendOnce();
                }
                catch (Exception exception)
                {
                    if (string.IsNullOrEmpty(lastError))
                    {
                        lastError = exception.Message;
                    }
                }
                finally
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    if (jniAttached)
                    {
                        UnityEngine.AndroidJNI.DetachCurrentThread();
                    }
#endif
                    writerThread = null;
                    finalized = true;
                    if (Volatile.Read(ref disposeRequested) != 0)
                    {
                        DisposeSignal();
                    }
                }
            }
        }

        private void SignalWriter()
        {
            if (Volatile.Read(ref signalDisposed) != 0)
            {
                return;
            }
            try
            {
                frameReady.Set();
            }
            catch (ObjectDisposedException)
            {
                // A racing Dispose can only happen after finalization.
            }
        }

        private void DisposeSignal()
        {
            if (Interlocked.Exchange(ref signalDisposed, 1) == 0)
            {
                frameReady.Dispose();
            }
        }

        private void DisposeBackendOnce()
        {
            if (Interlocked.Exchange(ref backendDisposed, 1) == 0)
            {
                backend.Dispose();
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
                while (frames.Count > 0)
                {
                    PendingFrame frame = frames.Dequeue();
                    Interlocked.Add(ref framesDropped, frame.RepeatCount);
                    availableFrameBuffers.Enqueue(frame.Nv12);
                }
            }
        }

        private bool TryReserveFrameBatch(
            int repeatedFrameCount,
            out long firstFrameIndex)
        {
            firstFrameIndex = 0L;
            if (repeatedFrameCount <= 0 || !accepting)
            {
                return false;
            }

            firstFrameIndex = Interlocked.Add(
                ref framesSubmitted,
                repeatedFrameCount
            ) - repeatedFrameCount;
            return true;
        }

        private long TimestampForFrameIndex(long frameIndex)
        {
            return checked(frameIndex * 1_000_000L / frameRate);
        }

        private void ReturnFrameBuffer(byte[] buffer)
        {
            lock (queueLock)
            {
                availableFrameBuffers.Enqueue(buffer);
            }
        }

        private readonly struct PendingFrame
        {
            public PendingFrame(
                byte[] nv12,
                long firstFrameIndex,
                int repeatCount)
            {
                Nv12 = nv12;
                FirstFrameIndex = firstFrameIndex;
                RepeatCount = repeatCount;
            }

            public byte[] Nv12 { get; }
            public long FirstFrameIndex { get; }
            public int RepeatCount { get; }
        }
    }

    internal interface IFirstPersonMp4Backend : IDisposable
    {
        string LastError { get; }
        string Name { get; }

        bool Start(
            string outputPath,
            int width,
            int height,
            int frameRate,
            int bitrate);
        bool EncodeNv12(byte[] nv12, long presentationTimeUs);
        void Stop();
    }

    internal static class FirstPersonMp4BackendFactory
    {
        public static IFirstPersonMp4Backend Create()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidMediaCodecBackend();
#else
            return new EditorCountingBackend();
#endif
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    internal sealed class AndroidMediaCodecBackend : IFirstPersonMp4Backend
    {
        private UnityEngine.AndroidJavaClass recorder;

        public string LastError => recorder == null
            ? string.Empty
            : recorder.CallStatic<string>("getLastError") ?? string.Empty;

        public string Name => recorder == null
            ? string.Empty
            : recorder.CallStatic<string>("getEncoderName") ?? string.Empty;

        public bool Start(
            string outputPath,
            int width,
            int height,
            int frameRate,
            int bitrate)
        {
            recorder = new UnityEngine.AndroidJavaClass(
                "com.signvr.recording.SignVRMp4Recorder");
            return recorder.CallStatic<bool>(
                "start",
                outputPath,
                width,
                height,
                frameRate,
                bitrate);
        }

        public bool EncodeNv12(byte[] nv12, long presentationTimeUs)
        {
            return recorder != null && recorder.CallStatic<bool>(
                "encodeYuv",
                nv12,
                presentationTimeUs);
        }

        public void Stop()
        {
            recorder?.CallStatic("stop");
        }

        public void Dispose()
        {
            recorder?.Dispose();
            recorder = null;
        }
    }
#else
    internal sealed class EditorCountingBackend : IFirstPersonMp4Backend
    {
        public string LastError => string.Empty;
        public string Name => "Editor counting backend";

        public bool Start(
            string outputPath,
            int width,
            int height,
            int frameRate,
            int bitrate)
        {
            return true;
        }

        public bool EncodeNv12(byte[] nv12, long presentationTimeUs)
        {
            return true;
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }
#endif
}
