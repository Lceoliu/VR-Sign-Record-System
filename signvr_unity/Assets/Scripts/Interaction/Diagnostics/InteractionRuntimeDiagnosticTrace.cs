using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace SignVR.Interaction.Diagnostics
{
    /// <summary>
    /// Temporary, development-build-only trace for diagnosing standalone
    /// startup failures whose managed exceptions are not visible in logcat.
    /// </summary>
    internal static class InteractionRuntimeDiagnosticTrace
    {
        private const string DirectoryName = "interaction-diagnostics";
        private const string FileName = "latest.jsonl";
        private static readonly object Sync = new object();
        private static string tracePath;
        private static bool enabled;

        internal static void ResetSession(string detail)
        {
            enabled = Debug.isDebugBuild;
            if (!enabled)
            {
                return;
            }

            lock (Sync)
            {
                try
                {
                    string directory = Path.Combine(
                        Application.persistentDataPath,
                        DirectoryName
                    );
                    Directory.CreateDirectory(directory);
                    tracePath = Path.Combine(directory, FileName);
                    File.WriteAllText(tracePath, string.Empty, Encoding.UTF8);
                    WriteUnlocked("session_reset", detail, null);
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        "[InteractionDiagnosticTrace] Reset failed: " + exception
                    );
                }
            }
        }

        internal static void Write(string eventName, string detail = null)
        {
            Write(eventName, detail, null);
        }

        internal static void Write(
            string eventName,
            string detail,
            Exception exception)
        {
            if (!enabled)
            {
                return;
            }

            lock (Sync)
            {
                try
                {
                    if (string.IsNullOrEmpty(tracePath))
                    {
                        string directory = Path.Combine(
                            Application.persistentDataPath,
                            DirectoryName
                        );
                        Directory.CreateDirectory(directory);
                        tracePath = Path.Combine(directory, FileName);
                    }
                    WriteUnlocked(eventName, detail, exception);
                }
                catch (Exception writeFailure)
                {
                    Debug.LogError(
                        "[InteractionDiagnosticTrace] Write failed: " +
                        writeFailure
                    );
                }
            }
        }

        internal static void WriteBackground(
            string eventName,
            string detail = null,
            Exception exception = null)
        {
            if (!enabled)
            {
                return;
            }

            lock (Sync)
            {
                try
                {
                    if (string.IsNullOrEmpty(tracePath))
                    {
                        return;
                    }
                    WriteUnlocked(
                        eventName,
                        detail,
                        exception,
                        -1d,
                        -1
                    );
                }
                catch
                {
                    // Diagnostics must never fault a capture worker.
                }
            }
        }

        private static void WriteUnlocked(
            string eventName,
            string detail,
            Exception exception,
            double? realtime = null,
            int? frame = null)
        {
            string line =
                "{\"utc\":\"" + Escape(DateTimeOffset.UtcNow.ToString("O")) +
                "\",\"realtime\":" +
                (realtime ?? Time.realtimeSinceStartupAsDouble).ToString(
                    "R",
                    CultureInfo.InvariantCulture
                ) +
                ",\"frame\":" + (frame ?? Time.frameCount).ToString(
                    CultureInfo.InvariantCulture
                ) +
                ",\"event\":\"" + Escape(eventName) +
                "\",\"detail\":\"" + Escape(detail) +
                "\",\"exception\":\"" + Escape(exception?.ToString()) +
                "\"}" + Environment.NewLine;
            File.AppendAllText(tracePath, line, Encoding.UTF8);
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length + 16);
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                switch (character)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < ' ')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }
            return builder.ToString();
        }
    }
}
