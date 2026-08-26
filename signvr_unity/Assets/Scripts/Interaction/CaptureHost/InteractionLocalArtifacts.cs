using System;
using System.Collections.Generic;

namespace SignVR.Interaction.CaptureHost
{
    /// <summary>
    /// The Quest-authoritative files that make up a sealed Standalone Study
    /// Mode Run. No files outside this set contribute to local completeness.
    /// </summary>
    public static class InteractionLocalArtifactTypes
    {
        public const string Manifest = "manifest";
        public const string Events = "events";
        public const string Poses = "poses";
        public const string Objects = "objects";
        public const string Summary = "summary";

        private static readonly IReadOnlyList<string> all = Array.AsReadOnly(
            new[]
            {
                Manifest,
                Events,
                Poses,
                Objects,
                Summary
            }
        );

        public static IReadOnlyList<string> All => all;

        public static string FileNameFor(string artifactType)
        {
            switch (artifactType)
            {
                case Manifest:
                    return InteractionStoragePaths.ManifestFileName;
                case Events:
                    return InteractionStoragePaths.EventsFileName;
                case Poses:
                    return InteractionStoragePaths.PosesFileName;
                case Objects:
                    return InteractionStoragePaths.ObjectsFileName;
                case Summary:
                    return InteractionStoragePaths.SummaryFileName;
                default:
                    throw new ArgumentException(
                        "Unknown standalone local artifact type.",
                        nameof(artifactType)
                    );
            }
        }
    }
}
