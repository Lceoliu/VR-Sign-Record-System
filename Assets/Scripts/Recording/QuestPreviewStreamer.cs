using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SignVR.Recording
{
    public sealed class QuestPreviewStreamer : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField]
        [Tooltip(
            "向网页监控端上传独立正面摄像机的压缩 JPEG 画面。" +
            "关闭可节省 GPU 回读、JPEG 编码与 HTTP 上传开销。"
        )]
        private bool streamJpegPreview = true;

        [SerializeField]
        private Camera sourceCamera;

        [SerializeField]
        private Transform upperBodyHead;

        [SerializeField]
        private Transform upperBodyHips;

        [SerializeField]
        private Vector3 fixedCameraPosition = new Vector3(0f, 1.35f, -0.65f);

        [SerializeField]
        [Range(30f, 70f)]
        private float fieldOfView = 50f;

        [Header("Preview")]
        [SerializeField]
        [Min(160)]
        private int width = 640;

        [SerializeField]
        [Min(90)]
        private int height = 360;

        [SerializeField]
        [Range(1, 15)]
        private int framesPerSecond = 8;

        [SerializeField]
        [Range(1, 100)]
        private int jpegQuality = 60;

        private Camera previewCamera;
        private RenderTexture renderTexture;
        private Coroutine captureRoutine;
        private byte[] pendingJpeg;
        private bool readbackPending;
        private bool uploadInFlight;
        private string previewUrl;
        private string sessionToken;

        public void ConfigureView(
            Transform head,
            Transform hips,
            Vector3 cameraPosition,
            float verticalFieldOfView,
            bool enableJpegStreaming)
        {
            upperBodyHead = head;
            upperBodyHips = hips;
            fixedCameraPosition = cameraPosition;
            fieldOfView = verticalFieldOfView;
            streamJpegPreview = enableJpegStreaming;
        }

        private void Awake()
        {
            if (!streamJpegPreview)
            {
                enabled = false;
                return;
            }

            if (sourceCamera == null)
            {
                sourceCamera = Camera.main;
            }

            if (sourceCamera == null)
            {
                Debug.LogError("[QuestPreviewStreamer] Source camera is not assigned.");
                enabled = false;
                return;
            }

            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                Debug.LogError("[QuestPreviewStreamer] Async GPU readback is not supported.");
                enabled = false;
            }
        }

        public void ConfigureHost(
            string hostBaseUrl,
            string deviceId,
            string token)
        {
            // The gateway calls this on every pair, so the switch is re-checked
            // here rather than relying on the component being enabled.
            if (!streamJpegPreview)
            {
                return;
            }

            previewUrl =
                $"{hostBaseUrl.TrimEnd('/')}/api/devices/" +
                $"{UnityWebRequest.EscapeURL(deviceId)}/preview-frame";
            sessionToken = token;

            EnsureCaptureResources();

            if (captureRoutine == null)
            {
                captureRoutine = StartCoroutine(CaptureLoop());
            }
        }

        private void EnsureCaptureResources()
        {
            if (renderTexture == null)
            {
                renderTexture = new RenderTexture(
                    width,
                    height,
                    16,
                    RenderTextureFormat.ARGB32
                )
                {
                    name = "SignVR Quest Preview",
                    useMipMap = false,
                    autoGenerateMips = false
                };
                renderTexture.Create();
            }

            if (previewCamera == null)
            {
                var cameraObject = new GameObject("SignVR Preview Camera")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                cameraObject.transform.SetParent(transform, false);
                previewCamera = cameraObject.AddComponent<Camera>();
                previewCamera.enabled = false;
                UniversalAdditionalCameraData cameraData =
                    cameraObject.AddComponent<UniversalAdditionalCameraData>();
                cameraData.allowXRRendering = false;
                cameraData.renderPostProcessing = false;
            }
        }

        private IEnumerator CaptureLoop()
        {
            var interval = new WaitForSecondsRealtime(1f / framesPerSecond);
            var endOfFrame = new WaitForEndOfFrame();

            while (true)
            {
                if (!readbackPending)
                {
                    previewCamera.CopyFrom(sourceCamera);
                    PositionPreviewCamera();
                    previewCamera.aspect = (float)width / height;
                    previewCamera.fieldOfView = fieldOfView;
                    previewCamera.targetTexture = renderTexture;

                    int mirroredCharacterLayer =
                        LayerMask.NameToLayer("MirroredCharacter");
                    if (mirroredCharacterLayer >= 0)
                    {
                        previewCamera.cullingMask = 1 << mirroredCharacterLayer;
                    }
                    else
                    {
                        int overlayUiLayer = LayerMask.NameToLayer("Overlay UI");
                        if (overlayUiLayer >= 0)
                        {
                            previewCamera.cullingMask &= ~(1 << overlayUiLayer);
                        }
                    }

                    previewCamera.enabled = true;

                    yield return endOfFrame;

                    previewCamera.enabled = false;
                    readbackPending = true;
                    AsyncGPUReadback.Request(
                        renderTexture,
                        0,
                        TextureFormat.RGBA32,
                        HandleReadback
                    );
                }

                if (!uploadInFlight && pendingJpeg != null)
                {
                    byte[] jpeg = pendingJpeg;
                    pendingJpeg = null;
                    StartCoroutine(UploadPreview(jpeg));
                }

                yield return interval;
            }
        }

        private void PositionPreviewCamera()
        {
            Vector3 framingCenter =
                (upperBodyHead.position + upperBodyHips.position) * 0.5f;
            Vector3 viewDirection =
                framingCenter + Vector3.up * 0.12f - fixedCameraPosition;

            previewCamera.transform.SetPositionAndRotation(
                fixedCameraPosition,
                Quaternion.LookRotation(viewDirection, Vector3.up)
            );
        }

        private void HandleReadback(AsyncGPUReadbackRequest request)
        {
            readbackPending = false;

            if (request.hasError)
            {
                Debug.LogWarning("[QuestPreviewStreamer] GPU readback failed.");
                return;
            }

            NativeArray<byte> pixels = request.GetData<byte>();

            using (NativeArray<byte> encoded =
                ImageConversion.EncodeNativeArrayToJPG(
                    pixels,
                    GraphicsFormat.R8G8B8A8_UNorm,
                    (uint)width,
                    (uint)height,
                    0,
                    jpegQuality
                ))
            {
                pendingJpeg = encoded.ToArray();
            }
        }

        private IEnumerator UploadPreview(byte[] jpeg)
        {
            uploadInFlight = true;

            using (var request = new UnityWebRequest(previewUrl, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(jpeg)
                {
                    contentType = "image/jpeg"
                };
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("x-signvr-token", sessionToken);
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning(
                        "[QuestPreviewStreamer] Preview upload failed: " +
                        request.error
                    );
                }
            }

            uploadInFlight = false;
        }

        private void OnDisable()
        {
            if (captureRoutine != null)
            {
                StopCoroutine(captureRoutine);
                captureRoutine = null;
            }

            if (previewCamera != null)
            {
                Destroy(previewCamera.gameObject);
                previewCamera = null;
            }

            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }
        }
    }
}
