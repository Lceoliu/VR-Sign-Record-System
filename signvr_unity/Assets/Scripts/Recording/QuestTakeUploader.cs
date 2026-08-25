using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace SignVR.Recording
{
    public sealed class QuestTakeUploader : MonoBehaviour
    {
        [SerializeField]
        private MetaBodyMotionRecorder recorder;

        [SerializeField]
        [Min(1)]
        private int uploadAttempts = 3;

        [SerializeField]
        [Min(0.25f)]
        private float retryDelaySeconds = 2f;

        private readonly Queue<UploadJob> pendingJobs = new Queue<UploadJob>();
        private readonly HashSet<string> queuedMetadataPaths = new HashSet<string>();
        private bool uploading;
        private string baseUrl;
        private string deviceId;
        private bool subscribed;
        private float nextStoredRecordingScanTime;

        private const float StoredRecordingScanIntervalSeconds = 2f;

        [Serializable]
        private sealed class StoredMetadata
        {
            public string session_id;
            public string sentence_id;
            public string take_id;
            public string pose_file;
            public string capture_status;
            public long pose_frame_count;
            public long valid_pose_frame_count;
            public float valid_pose_ratio;
            public bool editor_simulation;
            public bool pose_source_simulated;
        }

        private readonly struct UploadJob
        {
            public UploadJob(
                string sessionId,
                string sentenceId,
                string takeId,
                string posePath,
                string metadataPath)
            {
                SessionId = sessionId;
                SentenceId = sentenceId;
                TakeId = takeId;
                PosePath = posePath;
                MetadataPath = metadataPath;
            }

            public string SessionId { get; }
            public string SentenceId { get; }
            public string TakeId { get; }
            public string PosePath { get; }
            public string MetadataPath { get; }
        }

        private void OnEnable()
        {
            TryBindRecorder();
        }

        private void Update()
        {
            if (string.IsNullOrWhiteSpace(baseUrl) ||
                Time.unscaledTime < nextStoredRecordingScanTime)
            {
                return;
            }

            nextStoredRecordingScanTime =
                Time.unscaledTime + StoredRecordingScanIntervalSeconds;
            QueueStoredRecordings();
            TryStartNextUpload();
        }

        public void Configure(MetaBodyMotionRecorder recordingRecorder)
        {
            if (recordingRecorder == null)
            {
                throw new ArgumentNullException(nameof(recordingRecorder));
            }

            if (subscribed && recorder != recordingRecorder)
            {
                recorder.RecordingFinalized -= HandleRecordingFinalized;
                subscribed = false;
            }

            recorder = recordingRecorder;
            TryBindRecorder();
        }

        private void TryBindRecorder()
        {
            if (subscribed || recorder == null)
            {
                return;
            }

            recorder.RecordingFinalized += HandleRecordingFinalized;
            subscribed = true;
        }

        public void ConfigureHost(
            string hostBaseUrl,
            string questDeviceId)
        {
            baseUrl = hostBaseUrl.TrimEnd('/');
            deviceId = questDeviceId;
            Debug.Log(
                $"[QuestTakeUploader] Host configured: {baseUrl}; " +
                $"device={deviceId}."
            );
            nextStoredRecordingScanTime = Time.unscaledTime;
            QueueStoredRecordings();
            TryStartNextUpload();
        }

        private void HandleRecordingFinalized(
            MetaBodyMotionRecorder.RecordingArtifact artifact)
        {
            Debug.Log(
                $"[QuestTakeUploader] Finalized {artifact.Take.TakeId}; " +
                "queueing its local files for upload."
            );
            if (TryCreateStoredUploadJob(
                    artifact.MetadataPath,
                    out UploadJob job,
                    out string error))
            {
                Enqueue(job);
            }
            else
            {
                LogSkippedArtifact(artifact.Take.TakeId, error);
            }
            TryStartNextUpload();
        }

        private void QueueStoredRecordings()
        {
            string root = Path.Combine(
                Application.persistentDataPath,
                "Recordings"
            );

            if (!Directory.Exists(root))
            {
                return;
            }

            string[] metadataPaths;
            try
            {
                metadataPaths = Directory.GetFiles(
                    root,
                    "*.meta.json",
                    SearchOption.AllDirectories
                );
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[QuestTakeUploader] Could not scan stored recordings: " +
                    exception.Message
                );
                return;
            }

            foreach (string metadataPath in metadataPaths)
            {
                if (File.Exists(metadataPath + ".uploaded"))
                {
                    continue;
                }

                if (TryCreateStoredUploadJob(
                        metadataPath,
                        out UploadJob job,
                        out string error))
                {
                    Enqueue(job);
                }
                else
                {
                    LogSkippedArtifact(
                        Path.GetFileName(metadataPath),
                        error
                    );
                }
            }
        }

        private static bool TryCreateStoredUploadJob(
            string metadataPath,
            out UploadJob job,
            out string error)
        {
            job = default;
            error = null;

            try
            {
                string json = File.ReadAllText(metadataPath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    error = "metadata is empty";
                    return false;
                }

                StoredMetadata metadata =
                    JsonUtility.FromJson<StoredMetadata>(json);
                if (metadata == null ||
                    string.IsNullOrWhiteSpace(metadata.session_id) ||
                    string.IsNullOrWhiteSpace(metadata.sentence_id) ||
                    string.IsNullOrWhiteSpace(metadata.take_id) ||
                    string.IsNullOrWhiteSpace(metadata.pose_file))
                {
                    error = "metadata is incomplete";
                    return false;
                }

                if (metadata.editor_simulation ||
                    metadata.pose_source_simulated)
                {
                    error = "Editor simulation artifacts are local-only";
                    return false;
                }

                if (!string.Equals(
                        metadata.capture_status,
                        "completed",
                        StringComparison.Ordinal))
                {
                    error = "Take did not pass the completion quality gate";
                    return false;
                }

                if (metadata.pose_frame_count <= 0 ||
                    metadata.valid_pose_frame_count <= 0 ||
                    metadata.valid_pose_frame_count > metadata.pose_frame_count ||
                    metadata.valid_pose_ratio <= 0f ||
                    metadata.valid_pose_ratio > 1f)
                {
                    error = "Take pose quality metadata is invalid";
                    return false;
                }

                string metadataDirectory = Path.GetDirectoryName(metadataPath);
                if (string.IsNullOrWhiteSpace(metadataDirectory))
                {
                    error = "metadata directory is invalid";
                    return false;
                }

                string directoryPath = Path.GetFullPath(metadataDirectory);
                string posePath = Path.GetFullPath(
                    Path.Combine(directoryPath, metadata.pose_file)
                );
                string directoryPrefix = directoryPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                ) + Path.DirectorySeparatorChar;
                StringComparison pathComparison =
                    Path.DirectorySeparatorChar == '\\'
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal;

                if (!posePath.StartsWith(
                        directoryPrefix,
                        pathComparison))
                {
                    error = "pose file points outside its take directory";
                    return false;
                }

                if (!File.Exists(posePath))
                {
                    error = "pose file is missing";
                    return false;
                }

                if (new FileInfo(posePath).Length == 0)
                {
                    error = "pose file is empty";
                    return false;
                }

                job = new UploadJob(
                    metadata.session_id,
                    metadata.sentence_id,
                    metadata.take_id,
                    posePath,
                    metadataPath
                );
                return true;
            }
            catch (Exception exception)
            {
                error = $"metadata could not be read ({exception.Message})";
                return false;
            }
        }

        private void Enqueue(UploadJob job)
        {
            if (File.Exists(job.MetadataPath + ".uploaded") ||
                !queuedMetadataPaths.Add(job.MetadataPath))
            {
                return;
            }

            pendingJobs.Enqueue(job);
        }

        private void TryStartNextUpload()
        {
            if (uploading || pendingJobs.Count == 0 ||
                string.IsNullOrWhiteSpace(baseUrl))
            {
                return;
            }

            uploading = true;
            StartCoroutine(UploadNext());
        }

        private IEnumerator UploadNext()
        {
            UploadJob job = pendingJobs.Dequeue();
            bool uploaded = false;
            if (!TryReadUploadFiles(
                    job,
                    out byte[] poseBytes,
                    out byte[] metadataBytes,
                    out string readError))
            {
                Debug.LogWarning(
                    $"[QuestTakeUploader] Skipping {job.TakeId}: {readError}."
                );
                CompleteJob(job);
                yield break;
            }

            for (int attempt = 1; attempt <= uploadAttempts; attempt++)
            {
                string url =
                    $"{baseUrl}/api/devices/{UnityWebRequest.EscapeURL(deviceId)}/takes/upload";
                if (!TryCreateUploadRequest(
                        url,
                        job,
                        poseBytes,
                        metadataBytes,
                        out UnityWebRequest request,
                        out string requestError))
                {
                    Debug.LogWarning(
                        $"[QuestTakeUploader] Skipping {job.TakeId}: " +
                        $"{requestError}."
                    );
                    break;
                }

                using (request)
                {
                    int poseMegabytes = Mathf.CeilToInt(
                        poseBytes.Length / (1024f * 1024f)
                    );
                    request.timeout = Mathf.Clamp(
                        60 + poseMegabytes * 3,
                        60,
                        300
                    );
                    Debug.Log(
                        $"[QuestTakeUploader] Uploading {job.TakeId} " +
                        $"({poseBytes.Length} pose bytes, " +
                        $"{metadataBytes.Length} metadata bytes) to {url}; " +
                        $"timeout={request.timeout}s."
                    );
                    if (!TryBeginRequest(
                            request,
                            out UnityWebRequestAsyncOperation operation,
                            out requestError))
                    {
                        Debug.LogWarning(
                            $"[QuestTakeUploader] Skipping {job.TakeId}: " +
                            $"{requestError}."
                        );
                        break;
                    }

                    yield return operation;

                    uploaded = request.result == UnityWebRequest.Result.Success;

                    if (!uploaded)
                    {
                        Debug.LogWarning(
                            $"[QuestTakeUploader] Attempt {attempt} failed " +
                            $"for {job.TakeId}: result={request.result}, " +
                            $"response={request.responseCode}, error={request.error}."
                        );
                    }
                }

                if (uploaded)
                {
                    break;
                }

                if (attempt < uploadAttempts)
                {
                    yield return new WaitForSecondsRealtime(retryDelaySeconds);
                }
            }

            if (uploaded)
            {
                try
                {
                    File.WriteAllText(
                        job.MetadataPath + ".uploaded",
                        DateTime.UtcNow.ToString("O")
                    );
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"[QuestTakeUploader] Uploaded {job.TakeId}, but the " +
                        $"receipt marker could not be saved: {exception.Message}"
                    );
                }

                Debug.Log(
                    $"[QuestTakeUploader] Uploaded {job.TakeId} to {baseUrl}."
                );
            }
            else
            {
                Debug.LogError(
                    $"[QuestTakeUploader] Upload deferred for {job.TakeId}. " +
                    "The local files remain available for the next pairing."
                );
            }

            CompleteJob(job);
        }

        private static bool TryCreateUploadRequest(
            string url,
            UploadJob job,
            byte[] poseBytes,
            byte[] metadataBytes,
            out UnityWebRequest request,
            out string error)
        {
            request = null;
            error = null;

            try
            {
                var sections = new List<IMultipartFormSection>
                {
                    new MultipartFormDataSection("session_id", job.SessionId),
                    new MultipartFormDataSection("sentence_id", job.SentenceId),
                    new MultipartFormDataSection("take_id", job.TakeId),
                    new MultipartFormFileSection(
                        "pose_file",
                        poseBytes,
                        Path.GetFileName(job.PosePath),
                        "application/x-ndjson"
                    ),
                    new MultipartFormFileSection(
                        "meta_file",
                        metadataBytes,
                        Path.GetFileName(job.MetadataPath),
                        "application/json"
                    )
                };

                request = UnityWebRequest.Post(url, sections);
                return true;
            }
            catch (Exception exception)
            {
                request?.Dispose();
                request = null;
                error = $"upload request could not be created ({exception.Message})";
                return false;
            }
        }

        private static bool TryBeginRequest(
            UnityWebRequest request,
            out UnityWebRequestAsyncOperation operation,
            out string error)
        {
            operation = null;
            error = null;

            try
            {
                operation = request.SendWebRequest();
                return true;
            }
            catch (Exception exception)
            {
                error = $"upload request could not be sent ({exception.Message})";
                return false;
            }
        }

        private static bool TryReadUploadFiles(
            UploadJob job,
            out byte[] poseBytes,
            out byte[] metadataBytes,
            out string error)
        {
            poseBytes = null;
            metadataBytes = null;
            error = null;

            try
            {
                poseBytes = File.ReadAllBytes(job.PosePath);
                metadataBytes = File.ReadAllBytes(job.MetadataPath);
                if (poseBytes.Length == 0 || metadataBytes.Length == 0)
                {
                    error = "an upload file is empty";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = $"upload files could not be read ({exception.Message})";
                return false;
            }
        }

        private void CompleteJob(UploadJob job)
        {
            queuedMetadataPaths.Remove(job.MetadataPath);
            uploading = false;
            TryStartNextUpload();
        }

        private static void LogSkippedArtifact(string takeName, string error)
        {
            string message =
                $"[QuestTakeUploader] Skipping {takeName}: {error}.";
            if (string.Equals(
                    error,
                    "Editor simulation artifacts are local-only",
                    StringComparison.Ordinal))
            {
                Debug.Log(message);
            }
            else
            {
                Debug.LogWarning(message);
            }
        }

        private void OnDisable()
        {
            if (subscribed && recorder != null)
            {
                recorder.RecordingFinalized -= HandleRecordingFinalized;
                subscribed = false;
            }
        }
    }
}
