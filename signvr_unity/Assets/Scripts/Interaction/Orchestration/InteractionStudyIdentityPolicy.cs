using System;
using SignVR.Interaction.CaptureHost;

namespace SignVR.Interaction.Orchestration
{
    public static class InteractionStudyIdentityPolicy
    {
        public static void Validate(
            string participantId,
            string integratedBuildIdentity,
            out string safeParticipantId,
            out string safeBuildIdentity)
        {
            safeParticipantId = InteractionStoragePaths.ValidateSegment(
                participantId,
                nameof(participantId)
            );
            safeBuildIdentity = InteractionStoragePaths.ValidateSegment(
                integratedBuildIdentity,
                nameof(integratedBuildIdentity)
            );
            if (string.Equals(
                    safeParticipantId,
                    "UNCONFIGURED",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Participant ID must be explicitly selected.",
                    nameof(participantId)
                );
            }
            if (safeBuildIdentity.Length < 7 ||
                string.Equals(
                    safeBuildIdentity,
                    "unintegrated",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    safeBuildIdentity,
                    "unknown",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "An integrated commit/build identity of at least seven " +
                    "characters is required.",
                    nameof(integratedBuildIdentity)
                );
            }
        }
    }
}
