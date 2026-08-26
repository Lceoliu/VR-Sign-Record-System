using System;

namespace SignVR.Interaction.CaptureHost
{
    /// <summary>
    /// Maximum sealed-file sizes enforced by Quest-local Experiment Capture.
    /// The manifest has no streaming budget; transport and webcam artifacts
    /// are outside this model.
    /// </summary>
    public static class InteractionLocalArtifactSizeLimits
    {
        public static long MaximumBytesFor(string artifactType)
        {
            switch (artifactType)
            {
                case InteractionLocalArtifactTypes.Events:
                    return 512L * 1024L * 1024L;
                case InteractionLocalArtifactTypes.Poses:
                    return 2L * 1024L * 1024L * 1024L;
                case InteractionLocalArtifactTypes.Objects:
                    return 1024L * 1024L * 1024L;
                case InteractionLocalArtifactTypes.Summary:
                    return 16L * 1024L * 1024L;
                default:
                    throw new ArgumentException(
                        "Standalone local artifact has no capture byte limit.",
                        nameof(artifactType)
                    );
            }
        }
    }
}
