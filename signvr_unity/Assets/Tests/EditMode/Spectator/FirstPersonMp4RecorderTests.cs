using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace SignVR.Streaming.Tests
{
    public sealed class FirstPersonMp4RecorderTests
    {
        private string testDirectory;

        [SetUp]
        public void SetUp()
        {
            testDirectory = Path.Combine(
                Path.GetTempPath(),
                "SignVR-Mp4RecorderTests-" + Guid.NewGuid().ToString("N")
            );
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, true);
            }
        }

        [TestCase(1920, 1080, 3_110_400)]
        [TestCase(640, 360, 345_600)]
        public void ExpectedNv12ByteCount_IsOneAndAHalfBytesPerPixel(
            int width,
            int height,
            int expected)
        {
            Assert.That(
                FirstPersonMp4Recorder.ExpectedNv12ByteCount(width, height),
                Is.EqualTo(expected)
            );
        }

        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        public void PreviewEncoding_RequiresAnActiveUdpTransport(
            bool hasTransport,
            bool senderRunning,
            bool expected)
        {
            Assert.That(
                SpectatorViewStreamer.ShouldEncodePreview(
                    hasTransport,
                    senderRunning
                ),
                Is.EqualTo(expected)
            );
        }

        [Test]
        public void Stop_ReturnsImmediatelyWhileBackendFinalizationIsBlocked()
        {
            var backend = new BlockingBackend();
            var recorder = CreateRecorder(backend);
            recorder.Start();
            Assert.That(recorder.IsRecording, Is.True);

            var stopwatch = Stopwatch.StartNew();
            recorder.Stop();
            stopwatch.Stop();

            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(50));
            Assert.That(
                backend.StopEntered.Wait(2000),
                Is.True,
                "The writer thread never began codec finalization."
            );
            Assert.That(recorder.IsFinalized, Is.False);

            backend.AllowStop.Set();
            Assert.That(
                SpinWait.SpinUntil(() => recorder.IsFinalized, 2000),
                Is.True
            );
            recorder.Dispose();
            recorder.Dispose();
            Assert.That(backend.DisposeCalls, Is.EqualTo(1));
            Assert.That(backend.DisposedAfterStop, Is.True);
        }

        [Test]
        public void Writer_UsesThirtyFpsPresentationTimestampsForNv12Frames()
        {
            var backend = new RecordingBackend();
            var recorder = CreateRecorder(backend);
            recorder.Start();
            var frame = new byte[
                FirstPersonMp4Recorder.ExpectedNv12ByteCount(4, 2)
            ];

            Assert.That(recorder.Enqueue(frame), Is.True);
            Assert.That(recorder.Enqueue(frame), Is.True);
            Assert.That(recorder.Enqueue(frame), Is.True);
            Assert.That(
                SpinWait.SpinUntil(() => recorder.FramesWritten == 3, 2000),
                Is.True
            );
            recorder.Stop();
            Assert.That(
                SpinWait.SpinUntil(() => recorder.IsFinalized, 2000),
                Is.True
            );

            CollectionAssert.AreEqual(
                new long[] { 0L, 33_333L, 66_666L },
                backend.Timestamps
            );
            recorder.Dispose();
        }

        [Test]
        public void RepeatedSlots_UseOneQueueBufferAndPreserveTimestamps()
        {
            var backend = new RecordingBackend();
            var recorder = CreateRecorder(backend);
            recorder.Start();
            var frame = new byte[
                FirstPersonMp4Recorder.ExpectedNv12ByteCount(4, 2)
            ];

            Assert.That(recorder.Enqueue(frame, 6), Is.True);
            Assert.That(
                SpinWait.SpinUntil(() => recorder.FramesWritten == 6, 2000),
                Is.True
            );
            recorder.Stop();
            Assert.That(
                SpinWait.SpinUntil(() => recorder.IsFinalized, 2000),
                Is.True
            );

            CollectionAssert.AreEqual(
                new long[]
                {
                    0L,
                    33_333L,
                    66_666L,
                    100_000L,
                    133_333L,
                    166_666L
                },
                backend.Timestamps
            );
            Assert.That(recorder.FramesDropped, Is.Zero);
            recorder.Dispose();
        }

        [Test]
        public void FullQueue_DropsWholeRepeatedBatchButKeepsNextTimestamp()
        {
            var backend = new GateEncodingBackend();
            var recorder = CreateRecorder(backend);
            recorder.Start();
            var frame = new byte[
                FirstPersonMp4Recorder.ExpectedNv12ByteCount(4, 2)
            ];

            Assert.That(recorder.Enqueue(frame), Is.True);
            Assert.That(backend.EncodeEntered.Wait(2000), Is.True);
            Assert.That(recorder.Enqueue(frame), Is.True);
            Assert.That(recorder.Enqueue(frame), Is.True);
            Assert.That(recorder.Enqueue(frame), Is.True);
            Assert.That(recorder.Enqueue(frame, 5), Is.False);
            Assert.That(recorder.FramesDropped, Is.EqualTo(5));

            backend.AllowEncode.Set();
            Assert.That(
                SpinWait.SpinUntil(() => recorder.FramesWritten == 4, 2000),
                Is.True
            );
            Assert.That(recorder.Enqueue(frame), Is.True);
            Assert.That(
                SpinWait.SpinUntil(() => recorder.FramesWritten == 5, 2000),
                Is.True
            );
            recorder.Stop();
            Assert.That(
                SpinWait.SpinUntil(() => recorder.IsFinalized, 2000),
                Is.True
            );

            Assert.That(backend.Timestamps[4], Is.EqualTo(300_000L));
            recorder.Dispose();
        }

        [Test]
        public void NativeReadback_IsCopiedIntoRecorderOwnedBuffer()
        {
            var backend = new GateEncodingBackend();
            var recorder = CreateRecorder(backend);
            recorder.Start();
            var frame = new NativeArray<byte>(
                FirstPersonMp4Recorder.ExpectedNv12ByteCount(4, 2),
                Allocator.Temp
            );
            try
            {
                frame[0] = 42;
                Assert.That(recorder.Enqueue(frame), Is.True);
                Assert.That(backend.EncodeEntered.Wait(2000), Is.True);
                frame[0] = 7;
                backend.AllowEncode.Set();
                Assert.That(
                    SpinWait.SpinUntil(() => recorder.FramesWritten == 1, 2000),
                    Is.True
                );
                recorder.Stop();
                Assert.That(
                    SpinWait.SpinUntil(() => recorder.IsFinalized, 2000),
                    Is.True
                );

                Assert.That(backend.FirstBytes[0], Is.EqualTo(42));
            }
            finally
            {
                backend.AllowEncode.Set();
                frame.Dispose();
                recorder.Dispose();
            }
        }

        [Test]
        public void Enqueue_RejectsRgbSizedFrame()
        {
            var backend = new RecordingBackend();
            var recorder = CreateRecorder(backend);
            recorder.Start();

            Assert.That(recorder.Enqueue(new byte[4 * 2 * 3]), Is.False);
            Assert.That(recorder.FramesDropped, Is.EqualTo(1));

            recorder.Stop();
            Assert.That(
                SpinWait.SpinUntil(() => recorder.IsFinalized, 2000),
                Is.True
            );
            recorder.Dispose();
        }

        [Test]
        public void RgbToNv12Compute_PacksLimitedRangeNv12()
        {
            ComputeShader compute = Resources.Load<ComputeShader>(
                "SpectatorRgbToNv12"
            );
            Assert.That(compute, Is.Not.Null);
            var source = new RenderTexture(
                4,
                2,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear
            );
            var output = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                3,
                sizeof(uint)
            );
            RenderTexture previous = RenderTexture.active;
            try
            {
                source.Create();
                RenderTexture.active = source;
                GL.Clear(false, true, Color.red);

                int yKernel = compute.FindKernel("ConvertY");
                int uvKernel = compute.FindKernel("ConvertUv");
                compute.SetInts("_Dimensions", 4, 2);
                compute.SetInt("_UvWordOffset", 2);
                compute.SetInt("_SourceIsLinear", 0);
                compute.SetTexture(yKernel, "_Source", source);
                compute.SetBuffer(yKernel, "_Nv12Words", output);
                compute.Dispatch(yKernel, 1, 1, 1);
                compute.SetTexture(uvKernel, "_Source", source);
                compute.SetBuffer(uvKernel, "_Nv12Words", output);
                compute.Dispatch(uvKernel, 1, 1, 1);

                var words = new uint[3];
                output.GetData(words);
                Assert.That(words[0], Is.EqualTo(0x52525252u));
                Assert.That(words[1], Is.EqualTo(0x52525252u));
                Assert.That(words[2], Is.EqualTo(0xF05AF05Au));
            }
            finally
            {
                RenderTexture.active = previous;
                output.Dispose();
                source.Release();
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private FirstPersonMp4Recorder CreateRecorder(
            IFirstPersonMp4Backend backend)
        {
            return new FirstPersonMp4Recorder(
                testDirectory,
                "test",
                4,
                2,
                30,
                1_000_000,
                backend
            );
        }

        private class RecordingBackend : IFirstPersonMp4Backend
        {
            private readonly object sync = new object();
            private readonly List<long> timestamps = new List<long>();
            private readonly List<byte> firstBytes = new List<byte>();
            private int disposeCalls;
            private int stopCompleted;
            private int disposedAfterStop;

            public string LastError => string.Empty;
            public string Name => "Test backend";
            public int DisposeCalls => Volatile.Read(ref disposeCalls);
            public bool DisposedAfterStop =>
                Volatile.Read(ref disposedAfterStop) != 0;
            public IReadOnlyList<long> Timestamps
            {
                get
                {
                    lock (sync)
                    {
                        return timestamps.ToArray();
                    }
                }
            }
            public IReadOnlyList<byte> FirstBytes
            {
                get
                {
                    lock (sync)
                    {
                        return firstBytes.ToArray();
                    }
                }
            }

            public bool Start(
                string outputPath,
                int width,
                int height,
                int frameRate,
                int bitrate)
            {
                return true;
            }

            public virtual bool EncodeNv12(
                byte[] nv12,
                long presentationTimeUs)
            {
                lock (sync)
                {
                    timestamps.Add(presentationTimeUs);
                    firstBytes.Add(nv12[0]);
                }
                return true;
            }

            public virtual void Stop()
            {
                Interlocked.Exchange(ref stopCompleted, 1);
            }

            public virtual void Dispose()
            {
                if (Volatile.Read(ref stopCompleted) != 0)
                {
                    Interlocked.Exchange(ref disposedAfterStop, 1);
                }
                Interlocked.Increment(ref disposeCalls);
            }
        }

        private sealed class BlockingBackend : RecordingBackend
        {
            public readonly ManualResetEventSlim StopEntered =
                new ManualResetEventSlim(false);
            public readonly ManualResetEventSlim AllowStop =
                new ManualResetEventSlim(false);

            public override void Stop()
            {
                StopEntered.Set();
                AllowStop.Wait(2000);
                base.Stop();
            }
        }

        private sealed class GateEncodingBackend : RecordingBackend
        {
            public readonly ManualResetEventSlim EncodeEntered =
                new ManualResetEventSlim(false);
            public readonly ManualResetEventSlim AllowEncode =
                new ManualResetEventSlim(false);

            public override bool EncodeNv12(
                byte[] nv12,
                long presentationTimeUs)
            {
                EncodeEntered.Set();
                AllowEncode.Wait(2000);
                return base.EncodeNv12(nv12, presentationTimeUs);
            }
        }
    }
}
