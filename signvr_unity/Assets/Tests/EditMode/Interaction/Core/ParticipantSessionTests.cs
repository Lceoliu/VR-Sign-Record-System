using System;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace SignVR.Interaction.Core.Tests
{
    public sealed class ParticipantSessionTests
    {
        private static readonly DateTimeOffset FixedLocalTime =
            new DateTimeOffset(
                2026,
                8,
                26,
                20,
                34,
                56,
                TimeSpan.FromHours(8)
            );

        [Test]
        public void Constructor_UsesInjectedClockAndGuidForStableFormat()
        {
            ParticipantSession session = CreateFixedSession();

            Assert.That(
                session.ParticipantId,
                Is.EqualTo(
                    "P-20260826-123456-" +
                    "00112233445566778899aabbccddeeff"
                )
            );
        }

        [Test]
        public void ParticipantId_IsSafeForCaptureDirectorySegment()
        {
            ParticipantSession session = CreateFixedSession();

            Assert.That(
                Regex.IsMatch(
                    session.ParticipantId,
                    "^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$"
                ),
                Is.True
            );
        }

        [Test]
        public void Constructor_RejectsEmptyGuidSourceResult()
        {
            Assert.Throws<InvalidOperationException>(() =>
                new ParticipantSession(
                    () => FixedLocalTime,
                    () => Guid.Empty
                )
            );
        }

        [Test]
        public void ParticipantId_OneSessionSharesIdentityAcrossRuns()
        {
            int guidRequestCount = 0;
            var session = new ParticipantSession(
                () => FixedLocalTime,
                () =>
                {
                    guidRequestCount++;
                    return new Guid(
                        guidRequestCount == 1
                            ? "11111111-2222-3333-4444-555555555555"
                            : "99999999-8888-7777-6666-555555555555"
                    );
                }
            );

            string firstRunParticipantId = session.ParticipantId;
            string secondRunParticipantId = session.ParticipantId;
            string thirdRunParticipantId = session.ParticipantId;

            Assert.That(secondRunParticipantId, Is.EqualTo(firstRunParticipantId));
            Assert.That(thirdRunParticipantId, Is.EqualTo(firstRunParticipantId));
        }

        [Test]
        public void ParticipantId_DifferentSessionsUseDifferentGuidEntropy()
        {
            var first = new ParticipantSession(
                () => FixedLocalTime,
                () => new Guid(
                    "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
                )
            );
            var second = new ParticipantSession(
                () => FixedLocalTime,
                () => new Guid(
                    "ffffffff-1111-2222-3333-444444444444"
                )
            );

            Assert.That(second.ParticipantId, Is.Not.EqualTo(first.ParticipantId));
        }

        private static ParticipantSession CreateFixedSession()
        {
            return new ParticipantSession(
                () => FixedLocalTime,
                () => new Guid(
                    "00112233-4455-6677-8899-aabbccddeeff"
                )
            );
        }
    }
}
