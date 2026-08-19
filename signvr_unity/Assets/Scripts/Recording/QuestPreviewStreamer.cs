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
            "向网页监控端上传主角侧后方摄像机的压缩 JPEG 画面。" +
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
        private Transform characterRoot;

        [SerializeField]
        private Transform deskScreenTarget;

        [SerializeField]
        private Vector3 fixedCameraPosition = new Vector3(0f, 1.35f, -0.65f);

        [SerializeField]
        [Range(30f, 70f)]
        private float fieldOfView = 60f;

        [Header("Preview")]
        [SerializeField]
        [Min(160)]
        private int width = 480;

        [SerializeField]
        [Min(90)]
        private int height = 270;

        [SerializeField]
        [Range(1, 15)]
        private int framesPerSecond = 3;

        [SerializeField]
        [Range(1, 100)]
        private int jpegQuality = 35;

        private Camera previewCamera;
        private RenderTexture renderTexture;
        private Texture2D synchronousReadbackTexture;
        private Coroutine captureRoutine;
        private byte[] pendingJpeg;
        private bool readbackPending;
        private bool uploadInFlight;
        private string previewUrl;

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

        /// <summary>
        /// Keeps the preview dependency available to the recording gateway in
        /// scenes that do not author a preview character/camera. Pose capture
        /// and take upload remain enabled; a scene with a camera can opt in via
        /// ConfigureView.
        /// </summary>
        public void ConfigureDisabled()
        {
            streamJpegPreview = false;
            if (captureRoutine != null)
            {
                StopCoroutine(captureRoutine);
                captureRoutine = null;
            }
        }

        private void Awake()
        {
            width = 480;
            height = 270;
            framesPerSecond = 3;
            jpegQuality = 35;
            fieldOfView = 60f;

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

            characterRoot = GameObject.Find("Objects/StylizedCharacter")?.transform;
            deskScreenTarget = GameObject.Find(
                "Environment/TouchScreenDevice_03/ScreenArea"
            )?.transform;

            if (characterRoot == null || deskScreenTarget == null)
            {
                Debug.LogError(
                    "[QuestPreviewStreamer] Character or desk screen target is missing."
                );
                enabled = false;
                return;
            }

            Transform[] characterTransforms =
                characterRoot.GetComponentsInChildren<Transform>(true);
            upperBodyHead = System.Array.Find(
                characterTransforms,
                item => item.name == "Head"
            );
            upperBodyHips = System.Array.Find(
                characterTransforms,
                item => item.name == "Hips"
            );

            if (upperBodyHead == null || upperBodyHips == null)
            {
                Debug.LogError(
                    "[QuestPreviewStreamer] Character Head or Hips is missing."
                );
                enabled = false;
                return;
            }

            Vector3 forward = Vector3.ProjectOnPlane(
                deskScreenTarget.position - characterRoot.position,
                Vector3.up
            ).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
            fixedCameraPosition = characterRoot.position - forward * 1.45f +
                                  side * 0.9f + Vector3.up * 1.4f;
        }

        public void ConfigureHost(
            string hostBaseUrl,
            string deviceId)
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
                if (!readbackPending && !uploadInFlight && pendingJpeg == null)
                {
                    previewCamera.CopyFrom(sourceCamera);
                    PositionPreviewCamera();
                    previewCamera.aspect = (float)width / height;
                    previewCamera.fieldOfView = fieldOfView;
                    previewCamera.targetTexture = renderTexture;

                    previewCamera.enabled = true;
                    yield return endOfFrame;
                    previewCamera.enabled = false;

                    if (SystemInfo.supportsAsyncGPUReadback)
                    {
                        readbackPending = true;
                        AsyncGPUReadback.Request(
                            renderTexture,
                            0,
                            TextureFormat.RGBA32,
                            HandleReadback
                        );
                    }
                    else
                    {
                        ReadbackSynchronously();
                    }
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
            Vector3 characterCenter =
                (upperBodyHead.position + upperBodyHips.position) * 0.5f +
                Vector3.up * 0.12f;
            Vector3 framingCenter = Vector3.Lerp(
                characterCenter,
                deskScreenTarget.position,
                0.42f
            );
            Vector3 viewDirection =
                framingCenter - fixedCameraPosition;

            previewCamera.transform.SetPositionAndRotation(
                fixedCameraPosition,
                Quaternion.LookRotation(viewDirection, Vector3.up)
            );
        }

        private void ReadbackSynchronously()
        {
            if (synchronousReadbackTexture == null)
            {
                synchronousReadbackTexture = new Texture2D(
                    width,
                    height,
                    TextureFormat.RGBA32,
                    false
                );
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            synchronousReadbackTexture.ReadPixels(
                new Rect(0, 0, width, height),
                0,
                0,
                false
            );
            synchronousReadbackTexture.Apply(false, false);
            RenderTexture.active = previous;
            pendingJpeg = synchronousReadbackTexture.EncodeToJPG(jpegQuality);
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

            if (synchronousReadbackTexture != null)
            {
                Destroy(synchronousReadbackTexture);
                synchronousReadbackTexture = null;
            }
        }
    }
}
