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

        [Serializable]
        private sealed class StoredMetadata
        {
            public string session_id;
            public string sentence_id;
            public string take_id;
            public string pose_file;
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
            if (recorder == null)
            {
                Debug.LogError("[QuestTakeUploader] Recorder is not assigned.");
                enabled = false;
                return;
            }

            recorder.RecordingFinalized += HandleRecordingFinalized;
        }

        public void ConfigureHost(
            string hostBaseUrl,
            string questDeviceId)
        {
            baseUrl = hostBaseUrl.TrimEnd('/');
            deviceId = questDeviceId;
            QueueStoredRecordings();
            TryStartNextUpload();
        }

        private void HandleRecordingFinalized(
            MetaBodyMotionRecorder.RecordingArtifact artifact)
        {
            Enqueue(
                new UploadJob(
                    artifact.Take.SessionId,
                    artifact.Take.SentenceId,
                    artifact.Take.TakeId,
                    artifact.PosePath,
                    artifact.MetadataPath
                )
            );
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

            string[] metadataPaths = Directory.GetFiles(
                root,
                "*.meta.json",
                SearchOption.AllDirectories
            );

            foreach (string metadataPath in metadataPaths)
            {
                if (File.Exists(metadataPath + ".uploaded"))
                {
                    continue;
                }

                StoredMetadata metadata = JsonUtility.FromJson<StoredMetadata>(
                    File.ReadAllText(metadataPath)
                );

                string posePath = Path.Combine(
                    Path.GetDirectoryName(metadataPath),
                    metadata.pose_file
                );

                if (File.Exists(posePath))
                {
                    Enqueue(
                        new UploadJob(
                            metadata.session_id,
                            metadata.sentence_id,
                            metadata.take_id,
                            posePath,
                            metadataPath
                        )
                    );
                }
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

            for (int attempt = 1; attempt <= uploadAttempts; attempt++)
            {
                var sections = new List<IMultipartFormSection>
                {
                    new MultipartFormDataSection("session_id", job.SessionId),
                    new MultipartFormDataSection("sentence_id", job.SentenceId),
                    new MultipartFormDataSection("take_id", job.TakeId),
                    new MultipartFormFileSection(
                        "pose_file",
                        File.ReadAllBytes(job.PosePath),
                        Path.GetFileName(job.PosePath),
                        "application/x-ndjson"
                    ),
                    new MultipartFormFileSection(
                        "meta_file",
                        File.ReadAllBytes(job.MetadataPath),
                        Path.GetFileName(job.MetadataPath),
                        "application/json"
                    )
                };

                string url =
                    $"{baseUrl}/api/devices/{UnityWebRequest.EscapeURL(deviceId)}/takes/upload";

                using (UnityWebRequest request = UnityWebRequest.Post(url, sections))
                {
                    yield return request.SendWebRequest();

                    uploaded = request.result == UnityWebRequest.Result.Success;

                    if (!uploaded)
                    {
                        Debug.LogWarning(
                            $"[QuestTakeUploader] Attempt {attempt} failed: " +
                            request.error
                        );
                    }
                }

                if (uploaded)
                {
                    break;
                }

                yield return new WaitForSecondsRealtime(retryDelaySeconds);
            }

            if (uploaded)
            {
                File.WriteAllText(
                    job.MetadataPath + ".uploaded",
                    DateTime.UtcNow.ToString("O")
                );
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

            queuedMetadataPaths.Remove(job.MetadataPath);
            uploading = false;
            TryStartNextUpload();
        }

        private void OnDisable()
        {
            if (recorder != null)
            {
                recorder.RecordingFinalized -= HandleRecordingFinalized;
            }
        }
    }
}
