using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SignVR.Editor.Interaction.Qa
{
    public static class InteractionQaScreenshotPath
    {
        public const string DirectoryName = "Screenshots";

        public static bool TryResolve(
            string assetsDirectory,
            string fileName,
            out string fullPath,
            out string error)
        {
            fullPath = string.Empty;
            if (string.IsNullOrWhiteSpace(assetsDirectory))
            {
                error = "The project Assets directory is required.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "A screenshot filename is required.";
                return false;
            }

            string trimmed = fileName.Trim();
            if (Path.IsPathRooted(trimmed) ||
                !string.Equals(
                    Path.GetFileName(trimmed),
                    trimmed,
                    StringComparison.Ordinal))
            {
                error = "Screenshot filenames cannot contain a path.";
                return false;
            }
            if (!string.Equals(
                    Path.GetExtension(trimmed),
                    ".png",
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "QA screenshots must use the .png extension.";
                return false;
            }

            try
            {
                string root = Path.GetFullPath(Path.Combine(
                    assetsDirectory,
                    DirectoryName
                ));
                string candidate = Path.GetFullPath(Path.Combine(
                    root,
                    trimmed
                ));
                string rootPrefix = root.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                ) + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(
                        rootPrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "Screenshot path escaped Assets/Screenshots.";
                    return false;
                }

                fullPath = candidate;
                error = string.Empty;
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException)
            {
                error = "Screenshot path is invalid: " + exception.Message;
                return false;
            }
        }
    }

    public sealed class UnityInteractionQaScreenshotPort :
        IInteractionQaScreenshotPort
    {
        private readonly Func<bool> isPlaying;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly Action<string> capture;
        private readonly string assetsDirectory;

        public UnityInteractionQaScreenshotPort(
            string assetsDirectory = null,
            Func<bool> isPlaying = null,
            Func<DateTimeOffset> utcNow = null,
            Action<string> capture = null)
        {
            this.assetsDirectory = string.IsNullOrWhiteSpace(assetsDirectory)
                ? Application.dataPath
                : assetsDirectory;
            this.isPlaying = isPlaying ?? (() => EditorApplication.isPlaying);
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            this.capture = capture ?? ScreenCapture.CaptureScreenshot;
        }

        public InteractionQaActionResult CaptureGameView()
        {
            if (!isPlaying())
            {
                return InteractionQaActionResult.Failure(
                    "Enter Play Mode before capturing the Game View."
                );
            }

            string fileName = "interaction-qa-" +
                utcNow().ToUniversalTime().ToString(
                    "yyyyMMdd-HHmmss-fff"
                ) + ".png";
            if (!InteractionQaScreenshotPath.TryResolve(
                    assetsDirectory,
                    fileName,
                    out string fullPath,
                    out string error))
            {
                return InteractionQaActionResult.Failure(error);
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                capture(fullPath);
                return InteractionQaActionResult.Success(
                    "Game View screenshot requested at " + fullPath
                );
            }
            catch (Exception exception)
            {
                return InteractionQaActionResult.Failure(
                    "Game View screenshot failed safely: " +
                    exception.Message
                );
            }
        }
    }
}
