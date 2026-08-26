#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace SignVR.Interaction.CaptureHost
{
    public static class StandaloneLocalArtifactModelTestDriver
    {
        public static void LocalArtifactSetContainsExactlyFiveArtifacts()
        {
            IReadOnlyList<string> actual = InteractionLocalArtifactTypes.All;
            var expected = new HashSet<string>(StringComparer.Ordinal)
            {
                "manifest",
                "events",
                "poses",
                "objects",
                "summary"
            };

            Require(
                actual.Count == expected.Count &&
                expected.SetEquals(actual),
                "Standalone capture must expose exactly manifest, events, " +
                    "poses, objects, and summary."
            );
        }

        public static void LocalArtifactFileNamesAreStable()
        {
            RequireFileName("manifest", "run.manifest.json");
            RequireFileName("events", "events.jsonl");
            RequireFileName("poses", "poses.jsonl");
            RequireFileName("objects", "objects.jsonl");
            RequireFileName("summary", "summary.json");
        }

        public static void UnknownLocalArtifactTypeFailsClosed()
        {
            RequireRejected(null);
            RequireRejected(string.Empty);
            RequireRejected("unknown");
        }

        public static void
            LocalArtifactSetExcludesWebcamUploadAndAcknowledgementState()
        {
            RequireRejected("webcam");
            RequireRejected("upload");
            RequireRejected("upload_state");
            RequireRejected(".upload-state.json");
            RequireRejected("ack");
        }

        public static void LocalArtifactSizeLimitsRemainStable()
        {
            RequireSizeLimit("events", 536870912L);
            RequireSizeLimit("poses", 2147483648L);
            RequireSizeLimit("objects", 1073741824L);
            RequireSizeLimit("summary", 16777216L);
        }

        public static void DefaultCaptureBudgetsRetainLocalArtifactSizeLimits()
        {
            InteractionCaptureBudgetPolicy policy =
                InteractionCaptureBudgetPolicy.CreateDefault();

            Require(
                policy.Events.MaxFileBytes == 536870912L &&
                policy.Poses.MaxFileBytes == 2147483648L &&
                policy.Objects.MaxFileBytes == 1073741824L &&
                policy.SummaryMaxBytes == 16777216L,
                "Default capture budgets changed while moving to the local " +
                    "artifact limit seam."
            );
        }

        private static void RequireFileName(
            string artifactType,
            string expectedFileName)
        {
            Require(
                string.Equals(
                    InteractionLocalArtifactTypes.FileNameFor(artifactType),
                    expectedFileName,
                    StringComparison.Ordinal
                ),
                artifactType + " did not retain its stable local file name."
            );
        }

        private static void RequireRejected(string artifactType)
        {
            try
            {
                InteractionLocalArtifactTypes.FileNameFor(artifactType);
            }
            catch (ArgumentException)
            {
                return;
            }

            throw new InvalidOperationException(
                "Standalone capture accepted a non-local artifact type."
            );
        }

        private static void RequireSizeLimit(
            string artifactType,
            long expectedBytes)
        {
            Require(
                InteractionLocalArtifactSizeLimits.MaximumBytesFor(
                    artifactType
                ) == expectedBytes,
                artifactType + " did not retain its established byte limit."
            );
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
#endif
