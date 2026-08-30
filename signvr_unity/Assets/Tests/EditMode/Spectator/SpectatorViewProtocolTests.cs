using NUnit.Framework;
using UnityEngine;

namespace SignVR.Streaming.Tests
{
    public sealed class SpectatorViewProtocolTests
    {
        [Test]
        public void WriteHeader_UsesDocumentedLittleEndianLayout()
        {
            var packet = new byte[SpectatorViewProtocol.HeaderSize];

            SpectatorViewProtocol.WriteHeader(
                packet,
                SpectatorViewProtocol.HeadsetPovFlag,
                0x12345678,
                2,
                5,
                640,
                360,
                0x00010203
            );

            CollectionAssert.AreEqual(
                new byte[] { (byte)'S', (byte)'V', (byte)'R', (byte)'1' },
                new[] { packet[0], packet[1], packet[2], packet[3] }
            );
            Assert.That(packet[4], Is.EqualTo(SpectatorViewProtocol.Version));
            Assert.That(packet[5], Is.EqualTo(1));
            Assert.That(packet[6], Is.EqualTo(24));
            Assert.That(packet[7], Is.EqualTo(0));
            CollectionAssert.AreEqual(
                new byte[] { 0x78, 0x56, 0x34, 0x12 },
                new[] { packet[8], packet[9], packet[10], packet[11] }
            );
            CollectionAssert.AreEqual(
                new byte[] { 2, 0, 5, 0 },
                new[] { packet[12], packet[13], packet[14], packet[15] }
            );
            CollectionAssert.AreEqual(
                new byte[] { 0x80, 0x02, 0x68, 0x01 },
                new[] { packet[16], packet[17], packet[18], packet[19] }
            );
            CollectionAssert.AreEqual(
                new byte[] { 3, 2, 1, 0 },
                new[] { packet[20], packet[21], packet[22], packet[23] }
            );
        }

        [TestCase(1176, 1200, 1)]
        [TestCase(1177, 1200, 2)]
        [TestCase(2352, 1200, 2)]
        public void CalculateChunkCount_UsesPayloadAfterHeader(
            int frameSize,
            int datagramSize,
            int expected
        )
        {
            Assert.That(
                SpectatorViewProtocol.CalculateChunkCount(
                    frameSize,
                    datagramSize
                ),
                Is.EqualTo(expected)
            );
        }

        [Test]
        public void TransportLimits_MatchDesktopReceiver()
        {
            Assert.That(SpectatorViewProtocol.MaxChunkCount, Is.EqualTo(8192));
            Assert.That(
                SpectatorViewProtocol.MaxFrameBytes,
                Is.EqualTo(8 * 1024 * 1024)
            );
        }

        [Test]
        public void Configure_ExposesGeneratedSceneDefaultsAndFixedAnchor()
        {
            GameObject root = new GameObject("Spectator Test");

            try
            {
                Transform anchor = new GameObject("Fixed View Anchor").transform;
                anchor.SetParent(root.transform, false);
                SpectatorViewStreamer streamer =
                    root.AddComponent<SpectatorViewStreamer>();

                streamer.Configure(
                    SpectatorViewMode.HeadsetPov,
                    "255.255.255.255",
                    SpectatorViewStreamer.DefaultPort,
                    fixedAnchor: anchor
                );

                Assert.That(
                    streamer.ViewMode,
                    Is.EqualTo(SpectatorViewMode.HeadsetPov)
                );
                Assert.That(
                    streamer.ConfiguredRemoteHost,
                    Is.EqualTo("255.255.255.255")
                );
                Assert.That(
                    streamer.ConfiguredRemotePort,
                    Is.EqualTo(SpectatorViewStreamer.DefaultPort)
                );
                Assert.That(streamer.FixedViewAnchor, Is.SameAs(anchor));
                Assert.That(
                    streamer.CaptureWidth,
                    Is.EqualTo(SpectatorViewStreamer.DefaultWidth)
                );
                Assert.That(
                    streamer.CaptureHeight,
                    Is.EqualTo(SpectatorViewStreamer.DefaultHeight)
                );
                Assert.That(
                    streamer.CaptureFrameRate,
                    Is.EqualTo(SpectatorViewStreamer.DefaultFrameRate)
                );
                Assert.That(
                    streamer.JpegQuality,
                    Is.EqualTo(SpectatorViewStreamer.DefaultJpegQuality)
                );
                Assert.That(streamer.StreamsAutomatically, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CalculateHorizonLevelRotation_PreservesViewAndRemovesRoll()
        {
            Quaternion headset = Quaternion.Euler(24f, 68f, 37f);

            Quaternion leveled = SpectatorViewStreamer
                .CalculateHorizonLevelRotation(headset);

            Assert.That(
                Vector3.Angle(
                    headset * Vector3.forward,
                    leveled * Vector3.forward
                ),
                Is.LessThan(0.001f)
            );
            Assert.That(
                Mathf.Abs(Vector3.Dot(
                    leveled * Vector3.right,
                    Vector3.up
                )),
                Is.LessThan(0.0001f)
            );
        }

        [Test]
        public void ConfigureRecordingStability_ExposesHorizonLock()
        {
            GameObject root = new GameObject("Spectator Stability Test");
            try
            {
                SpectatorViewStreamer streamer =
                    root.AddComponent<SpectatorViewStreamer>();
                streamer.ConfigureRecordingStability(
                    smoothPose: true,
                    keepHorizonLevel: false
                );

                Assert.That(streamer.KeepsCaptureHorizonLevel, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
