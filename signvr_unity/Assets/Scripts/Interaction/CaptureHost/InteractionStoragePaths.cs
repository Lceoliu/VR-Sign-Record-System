using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SignVR.Interaction.CaptureHost
{
    public static class InteractionStoragePaths
    {
        public const string RootDirectoryName = "interaction-tests";
        public const string ManifestFileName = "run.manifest.json";
        public const string EventsFileName = "events.jsonl";
        public const string PosesFileName = "poses.jsonl";
        public const string ObjectsFileName = "objects.jsonl";
        public const string SummaryFileName = "summary.json";

        private static readonly HashSet<string> WindowsReservedNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5",
                "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5",
                "LPT6", "LPT7", "LPT8", "LPT9"
            };

        public static string ValidateSegment(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty storage identifier is required.",
                    parameterName
                );
            }

            string cleaned = value.Trim();
            if (cleaned == "." || cleaned == ".." || cleaned.Length > 80)
            {
                throw new ArgumentException(
                    "Storage identifiers must be 1-80 characters and cannot be dot segments.",
                    parameterName
                );
            }

            if (!IsAsciiLetterOrDigit(cleaned[0]) ||
                cleaned.EndsWith(".", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Storage identifiers must start with an ASCII letter or digit " +
                    "and cannot end in a dot.",
                    parameterName
                );
            }

            for (int index = 0; index < cleaned.Length; index++)
            {
                char character = cleaned[index];
                if (!IsAsciiLetterOrDigit(character) && character != '.' &&
                    character != '_' && character != '-')
                {
                    throw new ArgumentException(
                        "Storage identifiers may contain only ASCII letters, " +
                        "digits, dot, underscore, or hyphen.",
                        parameterName
                    );
                }
            }

            string baseName = cleaned.Split('.')[0];
            if (WindowsReservedNames.Contains(baseName))
            {
                throw new ArgumentException(
                    "Storage identifier is reserved by Windows.",
                    parameterName
                );
            }

            return cleaned;
        }

        public static string ValidateRelativeArtifactPath(
            string value,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512 ||
                Path.IsPathRooted(value) || value.IndexOf('\\') >= 0 ||
                value.IndexOf(':') >= 0 ||
                !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Artifact paths must be normalized relative POSIX paths of " +
                    "at most 512 characters.",
                    parameterName
                );
            }

            string[] segments = value.Split('/');
            if (segments.Length < 2)
            {
                throw new ArgumentException(
                    "Artifact paths must contain at least two safe segments.",
                    parameterName
                );
            }

            for (int index = 0; index < segments.Length; index++)
            {
                ValidateArtifactPathSegment(segments[index], parameterName);
            }

            return value;
        }

        private static void ValidateArtifactPathSegment(
            string value,
            string parameterName)
        {
            if (string.IsNullOrEmpty(value) || value == "." || value == ".." ||
                value.Length > 128 || !IsAsciiLetterOrDigit(value[0]) ||
                value.EndsWith(".", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Artifact paths contain an unsafe segment.",
                    parameterName
                );
            }
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!IsAsciiLetterOrDigit(character) && character != '.' &&
                    character != '_' && character != '-')
                {
                    throw new ArgumentException(
                        "Artifact path segments may contain only ASCII letters, " +
                        "digits, dot, underscore, or hyphen.",
                        parameterName
                    );
                }
            }
            if (WindowsReservedNames.Contains(value.Split('.')[0]))
            {
                throw new ArgumentException(
                    "Artifact path contains a Windows-reserved segment.",
                    parameterName
                );
            }
        }

        private static bool IsAsciiLetterOrDigit(char value)
        {
            return (value >= 'A' && value <= 'Z') ||
                (value >= 'a' && value <= 'z') ||
                (value >= '0' && value <= '9');
        }

        public static string GetInteractionRoot(string persistentDataPath)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath))
            {
                throw new ArgumentException(
                    "A persistent data root is required.",
                    nameof(persistentDataPath)
                );
            }

            return Path.GetFullPath(Path.Combine(
                persistentDataPath,
                RootDirectoryName
            ));
        }

        public static string GetRunDirectory(
            string persistentDataPath,
            string batchId,
            string participantId,
            string runId)
        {
            string root = GetInteractionRoot(persistentDataPath);
            string candidate = Path.GetFullPath(Path.Combine(
                root,
                ValidateSegment(batchId, nameof(batchId)),
                ValidateSegment(participantId, nameof(participantId)),
                ValidateSegment(runId, nameof(runId))
            ));

            EnsureChildPath(root, candidate);
            return candidate;
        }

        public static void EnsureChildPath(string parent, string child)
        {
            string canonicalParent = Path.GetFullPath(parent).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            ) + Path.DirectorySeparatorChar;
            string canonicalChild = Path.GetFullPath(child);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!canonicalChild.StartsWith(canonicalParent, comparison))
            {
                throw new InvalidOperationException(
                    "Resolved path escapes the interaction storage root."
                );
            }
        }
    }

    internal static class InteractionAtomicFile
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private const string ReplaceBackupMarker = ".replace-backup";

        public static void WriteNew(string destination, byte[] bytes)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (bytes == null || bytes.Length == 0)
            {
                throw new ArgumentException(
                    "Atomic files cannot be empty.",
                    nameof(bytes)
                );
            }

            if (File.Exists(destination))
            {
                throw new IOException(
                    "Refusing to overwrite existing file " + destination + "."
                );
            }

            string temporary = CreateTemporaryPath(destination);
            try
            {
                WriteAndSync(temporary, bytes);
                File.Move(temporary, destination);
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        public static void WriteTextReplace(string destination, string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            WriteReplace(destination, Utf8.GetBytes(text));
        }

        public static void WriteReplace(string destination, byte[] bytes)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (bytes == null || bytes.Length == 0)
            {
                throw new ArgumentException(
                    "Atomic files cannot be empty.",
                    nameof(bytes)
                );
            }

            RecoverDestination(destination);
            string temporary = CreateTemporaryPath(destination);
            try
            {
                WriteAndSync(temporary, bytes);
                if (!File.Exists(destination))
                {
                    File.Move(temporary, destination);
                    return;
                }

                try
                {
                    File.Replace(temporary, destination, null);
                }
                catch (PlatformNotSupportedException)
                {
                    ReplaceWithRecoverableRenames(temporary, destination);
                }
                catch (IOException)
                {
                    ReplaceWithRecoverableRenames(temporary, destination);
                }
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        public static string ReadUtf8(string path)
        {
            RecoverDestination(path);
            return Utf8.GetString(File.ReadAllBytes(path));
        }

        public static void RecoverDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
            {
                return;
            }
            string canonicalDirectory = Path.GetFullPath(directory);
            var destinations = new HashSet<string>(
                Path.DirectorySeparatorChar == '\\'
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal
            );
            foreach (string backup in Directory.GetFiles(
                canonicalDirectory,
                "*" + ReplaceBackupMarker + "*",
                SearchOption.TopDirectoryOnly))
            {
                int marker = backup.LastIndexOf(
                    ReplaceBackupMarker,
                    StringComparison.Ordinal
                );
                if (marker <= canonicalDirectory.Length)
                {
                    continue;
                }
                string destination = backup.Substring(0, marker);
                if (string.Equals(
                        Path.GetDirectoryName(Path.GetFullPath(destination)),
                        canonicalDirectory,
                        Path.DirectorySeparatorChar == '\\'
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal))
                {
                    destinations.Add(destination);
                }
            }
            foreach (string destination in destinations)
            {
                RecoverDestination(destination);
            }
        }

        public static void RecoverDestination(string destination)
        {
            if (string.IsNullOrWhiteSpace(destination))
            {
                throw new ArgumentException(
                    "Recovery destination is required.",
                    nameof(destination)
                );
            }
            string canonical = Path.GetFullPath(destination);
            string directory = Path.GetDirectoryName(canonical);
            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
            {
                return;
            }
            string fileName = Path.GetFileName(canonical);
            string[] backups = Directory.GetFiles(
                directory,
                fileName + ReplaceBackupMarker + "*",
                SearchOption.TopDirectoryOnly
            );
            if (backups.Length == 0)
            {
                return;
            }
            if (File.Exists(canonical))
            {
                for (int index = 0; index < backups.Length; index++)
                {
                    TryDelete(backups[index]);
                }
                return;
            }
            var valid = new List<string>();
            for (int index = 0; index < backups.Length; index++)
            {
                var info = new FileInfo(backups[index]);
                if (info.Exists && info.Length > 0L)
                {
                    valid.Add(backups[index]);
                }
            }
            if (valid.Count != 1)
            {
                throw new IOException(
                    "Atomic replace recovery requires exactly one non-empty backup for " +
                    canonical + "."
                );
            }
            File.Move(valid[0], canonical);
            for (int index = 0; index < backups.Length; index++)
            {
                if (!string.Equals(
                        backups[index],
                        valid[0],
                        Path.DirectorySeparatorChar == '\\'
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal))
                {
                    TryDelete(backups[index]);
                }
            }
        }

        public static string CreateSiblingTemporaryPath(
            string destination,
            string purpose)
        {
            if (string.IsNullOrWhiteSpace(purpose) ||
                purpose.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException(
                    "Temporary-file purpose is invalid.",
                    nameof(purpose)
                );
            }
            string directory = Path.GetDirectoryName(
                Path.GetFullPath(destination)
            );
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException(
                    "Destination has no parent directory.",
                    nameof(destination)
                );
            }
            Directory.CreateDirectory(directory);
            return Path.Combine(
                directory,
                "." + Path.GetFileName(destination) + "." + purpose + "." +
                Guid.NewGuid().ToString("N") + ".tmp"
            );
        }

        public static void PublishPreparedFile(
            string temporary,
            string destination,
            bool replace,
            bool allowEmpty)
        {
            string source = Path.GetFullPath(
                temporary ?? throw new ArgumentNullException(nameof(temporary))
            );
            string target = Path.GetFullPath(
                destination ?? throw new ArgumentNullException(nameof(destination))
            );
            if (!string.Equals(
                    Path.GetDirectoryName(source),
                    Path.GetDirectoryName(target),
                    Path.DirectorySeparatorChar == '\\'
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Prepared atomic file must be a destination sibling."
                );
            }
            var info = new FileInfo(source);
            if (!info.Exists || (!allowEmpty && info.Length == 0L))
            {
                throw new IOException(
                    "Prepared atomic file is missing or unexpectedly empty."
                );
            }
            RecoverDestination(target);
            if (!File.Exists(target))
            {
                File.Move(source, target);
                return;
            }
            if (!replace)
            {
                throw new IOException(
                    "Refusing to overwrite existing file " + target + "."
                );
            }
            try
            {
                File.Replace(source, target, null);
            }
            catch (PlatformNotSupportedException)
            {
                ReplaceWithRecoverableRenames(source, target);
            }
            catch (IOException)
            {
                ReplaceWithRecoverableRenames(source, target);
            }
        }

        private static void ReplaceWithRecoverableRenames(
            string temporary,
            string destination)
        {
            RecoverDestination(destination);
            string backup = destination + ReplaceBackupMarker;
            File.Move(destination, backup);
            try
            {
                File.Move(temporary, destination);
            }
            catch
            {
                if (!File.Exists(destination) && File.Exists(backup))
                {
                    File.Move(backup, destination);
                }
                throw;
            }

            TryDelete(backup);
        }

        private static string CreateTemporaryPath(string destination)
        {
            string directory = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException(
                    "Destination has no parent directory.",
                    nameof(destination)
                );
            }

            return CreateSiblingTemporaryPath(destination, "atomic");
        }

        private static void WriteAndSync(string path, byte[] bytes)
        {
            using (var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                65536,
                FileOptions.SequentialScan))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
                stream.Flush(true);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // The primary operation has already succeeded or is already
                // throwing. A uniquely named recovery file is safer than
                // masking that result with cleanup noise.
            }
        }
    }
}
