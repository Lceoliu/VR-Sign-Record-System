using System;
using System.Collections;
using SignVR.Interaction.Core;
using UnityEngine;

namespace SignVR.Interaction.PhaseAdapters
{
    [DisallowMultipleComponent]
    public sealed class InteractionFeedbackPresenter : MonoBehaviour
    {
        private static readonly int BaseColorProperty =
            Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorProperty =
            Shader.PropertyToID("_EmissionColor");

        [SerializeField]
        private InteractionPhaseCoordinator coordinator;

        [SerializeField]
        private Renderer feedbackRenderer;

        [SerializeField]
        private AudioSource audioSource;

        [SerializeField]
        private AudioClip acceptedClip;

        [SerializeField]
        private AudioClip errorClip;

        [SerializeField]
        private Color idleColor = new Color(0.2f, 0.35f, 0.55f, 1f);

        [SerializeField]
        private Color acceptedColor = new Color(0.15f, 1f, 0.35f, 1f);

        [SerializeField]
        private Color errorColor = new Color(1f, 0.12f, 0.08f, 1f);

        [SerializeField]
        private Color rejectedColor = new Color(1f, 0.65f, 0.05f, 1f);

        [SerializeField, Min(0.05f)]
        private float flashSeconds = 0.25f;

        private MaterialPropertyBlock propertyBlock;
        private Coroutine flashRoutine;
        private bool subscribed;

#if UNITY_INCLUDE_TESTS
        private readonly InteractionSubscriptionDiagnostic
            subscriptionDiagnostic =
                new InteractionSubscriptionDiagnostic();
#endif

        public Renderer FeedbackRenderer => feedbackRenderer;

        public InteractionPhaseCoordinator Coordinator => coordinator;

        public AudioSource FeedbackAudioSource => audioSource;

#if UNITY_INCLUDE_TESTS
        public InteractionSubscriptionDiagnostic SubscriptionDiagnostic =>
            subscriptionDiagnostic;
#endif

        private void Awake()
        {
            Bind();
            ApplyColor(idleColor);
        }

        private void OnEnable()
        {
            Bind();
        }

        public void Configure(
            InteractionPhaseCoordinator targetCoordinator,
            Renderer targetRenderer,
            AudioSource targetAudioSource = null)
        {
            InteractionPhaseCoordinator nextCoordinator = targetCoordinator ??
                throw new ArgumentNullException(nameof(targetCoordinator));
            Renderer nextRenderer = targetRenderer ??
                throw new ArgumentNullException(nameof(targetRenderer));
            bool manageRuntimeSubscriptions =
                Application.isPlaying && isActiveAndEnabled;
            if (manageRuntimeSubscriptions)
            {
                Unbind();
            }
            ResetFeedback();
            coordinator = nextCoordinator;
            feedbackRenderer = nextRenderer;
            audioSource = targetAudioSource;
            if (manageRuntimeSubscriptions)
            {
                Bind();
            }
            ApplyColor(idleColor);
        }

#if UNITY_INCLUDE_TESTS
        public void ConfigureTestAudioClips(
            AudioClip targetAcceptedClip,
            AudioClip targetErrorClip)
        {
            acceptedClip = targetAcceptedClip;
            errorClip = targetErrorClip;
        }
#endif

        public void ResetFeedback()
        {
            if (flashRoutine != null)
            {
                StopCoroutine(flashRoutine);
                flashRoutine = null;
            }
            if (audioSource != null)
            {
                audioSource.Stop();
            }
            ApplyColor(idleColor);
        }

        private void Bind()
        {
            if (!Application.isPlaying || subscribed || coordinator == null ||
                !isActiveAndEnabled)
            {
                return;
            }

            coordinator.RunConfigured += HandleRunConfigured;
            coordinator.ResultProduced += HandleResult;
            coordinator.RunReset += HandleRunReset;
            subscribed = true;
        }

        private void Unbind()
        {
            if (subscribed && coordinator != null)
            {
                coordinator.RunConfigured -= HandleRunConfigured;
                coordinator.ResultProduced -= HandleResult;
                coordinator.RunReset -= HandleRunReset;
            }
            subscribed = false;
        }

        private void HandleRunConfigured(RunPlan runPlan)
        {
#if UNITY_INCLUDE_TESTS
            subscriptionDiagnostic.RecordRunConfigured();
#endif
            ResetFeedback();
        }

        private void HandleRunReset()
        {
#if UNITY_INCLUDE_TESTS
            subscriptionDiagnostic.RecordRunReset();
#endif
            ResetFeedback();
        }

        private void HandleResult(ValidationResult result)
        {
#if UNITY_INCLUDE_TESTS
            subscriptionDiagnostic.RecordResultProduced();
#endif
            if (!isActiveAndEnabled)
            {
                return;
            }
            Color color = result.InteractionError
                ? errorColor
                : result.Accepted
                    ? acceptedColor
                    : rejectedColor;
            AudioClip clip = result.Accepted ? acceptedClip : errorClip;
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }

            if (flashRoutine != null)
            {
                StopCoroutine(flashRoutine);
            }
            flashRoutine = StartCoroutine(Flash(color));
        }

        private IEnumerator Flash(Color color)
        {
            ApplyColor(color);
            yield return new WaitForSecondsRealtime(flashSeconds);
            ApplyColor(idleColor);
            flashRoutine = null;
        }

        private void ApplyColor(Color color)
        {
            if (feedbackRenderer == null)
            {
                return;
            }

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }
            feedbackRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorProperty, color);
            propertyBlock.SetColor(EmissionColorProperty, color * 0.35f);
            feedbackRenderer.SetPropertyBlock(propertyBlock);
        }

        private void OnDisable()
        {
            Unbind();
            ResetFeedback();
        }

        private void OnDestroy()
        {
            Unbind();
            ResetFeedback();
        }
    }
}
