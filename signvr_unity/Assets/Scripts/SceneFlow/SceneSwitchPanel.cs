using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SignVR.SceneFlow
{
    /// <summary>
    /// Controls a small side-mounted scene menu. It only switches local Unity
    /// scenes and does not create or depend on a multiplayer session.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(WorldSpacePokeCanvas))]
    public sealed class SceneSwitchPanel : MonoBehaviour
    {
        [SerializeField]
        private Button sortingSceneButton;

        [SerializeField]
        private Button coopSceneButton;

        [SerializeField]
        private Button resetSceneButton;

        [SerializeField]
        private string sortingSceneName = "VRSortingGame";

        [SerializeField]
        private string coopSceneName = "CoopLiftWorkshop";

        private bool listenersAttached;

        public Button SortingSceneButton => sortingSceneButton;
        public Button CoopSceneButton => coopSceneButton;
        public Button ResetSceneButton => resetSceneButton;
        public string SortingSceneName => sortingSceneName;
        public string CoopSceneName => coopSceneName;
        public bool IsLoading { get; private set; }

        private void Awake()
        {
            GetComponent<WorldSpacePokeCanvas>()?.EnsurePokeInteraction();
            AttachListeners();
        }

        public void Configure(
            Button sortingButton,
            Button coopButton,
            Button resetButton = null
        )
        {
            Configure(
                sortingButton,
                coopButton,
                resetButton,
                "VRSortingGame",
                "CoopLiftWorkshop"
            );
        }

        public void Configure(
            Button sortingButton,
            Button coopButton,
            Button resetButton,
            string sortingTargetScene,
            string coopTargetScene
        )
        {
            DetachListeners();

            sortingSceneButton = sortingButton;
            coopSceneButton = coopButton;
            resetSceneButton = resetButton;
            sortingSceneName = string.IsNullOrWhiteSpace(sortingTargetScene)
                ? "VRSortingGame"
                : sortingTargetScene;
            coopSceneName = string.IsNullOrWhiteSpace(coopTargetScene)
                ? "CoopLiftWorkshop"
                : coopTargetScene;

            if (Application.isPlaying)
            {
                AttachListeners();
            }
        }

        public void LoadSortingScene()
        {
            LoadScene(sortingSceneName);
        }

        public void LoadCoopScene()
        {
            LoadScene(coopSceneName);
        }

        public void ReloadCurrentScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            LoadScene(activeScene.name);
        }

        public bool LoadScene(string sceneName)
        {
            if (IsLoading || string.IsNullOrWhiteSpace(sceneName))
            {
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError(
                    $"[SceneSwitchPanel] Scene '{sceneName}' is not enabled " +
                    "in Build Settings.",
                    this
                );
                return false;
            }

            IsLoading = true;
            SetButtonsInteractable(false);

            AsyncOperation operation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Single
            );

            if (operation == null)
            {
                IsLoading = false;
                SetButtonsInteractable(true);
                return false;
            }

            return true;
        }

        private void AttachListeners()
        {
            if (listenersAttached)
            {
                return;
            }

            sortingSceneButton?.onClick.AddListener(LoadSortingScene);
            coopSceneButton?.onClick.AddListener(LoadCoopScene);
            resetSceneButton?.onClick.AddListener(ReloadCurrentScene);
            listenersAttached = true;
        }

        private void DetachListeners()
        {
            if (!listenersAttached)
            {
                return;
            }

            sortingSceneButton?.onClick.RemoveListener(LoadSortingScene);
            coopSceneButton?.onClick.RemoveListener(LoadCoopScene);
            resetSceneButton?.onClick.RemoveListener(ReloadCurrentScene);
            listenersAttached = false;
        }

        private void SetButtonsInteractable(bool value)
        {
            if (sortingSceneButton != null)
            {
                sortingSceneButton.interactable = value;
            }

            if (coopSceneButton != null)
            {
                coopSceneButton.interactable = value;
            }

            if (resetSceneButton != null)
            {
                resetSceneButton.interactable = value;
            }
        }

        private void OnDestroy()
        {
            DetachListeners();
        }
    }
}
