using UnityEngine;

namespace SignVR.Demo
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class AvatarPreviewHumanoidPoseBroadcaster : MonoBehaviour
    {
        [SerializeField] private Animator _sourceAnimator;
        [SerializeField] private Animator[] _targetAnimators;

        private HumanPoseHandler _sourceHandler;
        private HumanPoseHandler[] _targetHandlers;
        private HumanPose _pose;
        private Quaternion[] _targetRestBodyRotations;

        public void Configure(Animator sourceAnimator, Animator[] targetAnimators)
        {
            _sourceAnimator = sourceAnimator;
            _targetAnimators = targetAnimators;
        }

        private void Awake()
        {
            _sourceHandler = new HumanPoseHandler(
                _sourceAnimator.avatar,
                _sourceAnimator.transform
            );
            _targetHandlers = new HumanPoseHandler[_targetAnimators.Length];
            _targetRestBodyRotations = new Quaternion[_targetAnimators.Length];
            var restPose = new HumanPose
            {
                muscles = new float[HumanTrait.MuscleCount],
            };

            for (int index = 0; index < _targetAnimators.Length; index++)
            {
                Animator target = _targetAnimators[index];
                _targetHandlers[index] = new HumanPoseHandler(
                    target.avatar,
                    target.transform
                );
                _targetHandlers[index].GetHumanPose(ref restPose);
                _targetRestBodyRotations[index] = restPose.bodyRotation;
            }

            _pose = new HumanPose
            {
                muscles = new float[HumanTrait.MuscleCount],
            };
        }

        private void LateUpdate()
        {
            _sourceHandler.GetHumanPose(ref _pose);

            for (int index = 0; index < _targetHandlers.Length; index++)
            {
                HumanPose targetPose = _pose;
                targetPose.bodyRotation =
                    _targetRestBodyRotations[index] * Quaternion.Euler(0f, 180f, 0f);
                _targetHandlers[index].SetHumanPose(ref targetPose);
            }
        }

        private void OnDestroy()
        {
            _sourceHandler?.Dispose();
            if (_targetHandlers == null)
            {
                return;
            }

            foreach (HumanPoseHandler targetHandler in _targetHandlers)
            {
                targetHandler.Dispose();
            }
        }
    }
}
