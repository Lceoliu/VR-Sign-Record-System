using System;
using System.Linq;
using SignVR.Interaction.Core;
using SignVR.Interaction.Orchestration;
using SignVR.Interaction.PhaseAdapters;
using SignVR.Interaction.Presentation;
using UnityEditor;
using UnityEngine;

namespace SignVR.Editor.Interaction.Qa
{
    public sealed class InteractionQaConsoleWindow : EditorWindow
    {
        public const string MenuPath =
            "Tools/SignVR/Interaction/Open QA Console";

        private InteractionQaActionService actions;
        private Vector2 scroll;
        private string lastActionMessage = "No QA action dispatched.";
        private bool lastActionSucceeded = true;

        [MenuItem(MenuPath)]
        public static void Open()
        {
            InteractionQaConsoleWindow window =
                GetWindow<InteractionQaConsoleWindow>();
            window.titleContent = new GUIContent("Interaction QA");
            window.minSize = new Vector2(430f, 620f);
            window.RefreshDependencies();
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
            RefreshDependencies();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        }

        private void Update()
        {
            if (EditorApplication.isPlaying)
            {
                Repaint();
            }
        }

        private void HandlePlayModeChanged(PlayModeStateChange _)
        {
            RefreshDependencies();
            Repaint();
        }

        private void RefreshDependencies()
        {
            InteractionStudyFlowController flow = FindInLoadedScenes<
                InteractionStudyFlowController>();
            InteractionPhaseCoordinator coordinator =
                flow?.PhaseCoordinator ??
                FindInLoadedScenes<InteractionPhaseCoordinator>();
            InteractionDeterministicPresentation presentation =
                FindInLoadedScenes<InteractionDeterministicPresentation>();
            GhostPointingDetector pointing =
                FindInLoadedScenes<GhostPointingDetector>();

            var authority = new UnityInteractionQaAuthorityPort(
                flow,
                coordinator,
                presentation,
                pointingDetector: pointing
            );
            actions = new InteractionQaActionService(
                authority,
                new UnityInteractionQaScreenshotPort()
            );
        }

        private static T FindInLoadedScenes<T>() where T : UnityEngine.Object
        {
            return UnityEngine.Object.FindFirstObjectByType<T>(
                FindObjectsInactive.Include
            );
        }

        private void OnGUI()
        {
            if (actions == null)
            {
                RefreshDependencies();
            }

            EditorGUILayout.HelpBox(
                "QA 注入只用于验证公开 authority 与确定性展示。" +
                "常规手操作仍是最终真相；通过控制台成功不替代 Quest 裸手验收。",
                MessageType.Warning
            );

            if (GUILayout.Button("Refresh Scene Dependencies"))
            {
                RefreshDependencies();
            }

            InteractionQaSnapshot snapshot = actions.ReadSnapshot();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawRunSnapshot(snapshot);
            EditorGUILayout.Space(8f);
            DrawPointingDiagnostics(snapshot.Pointing);
            EditorGUILayout.Space(8f);
            DrawRunActions(snapshot);
            EditorGUILayout.Space(8f);
            DrawInputActions(snapshot);
            EditorGUILayout.Space(8f);
            DrawPresentationActions();
            EditorGUILayout.Space(8f);
            DrawLastAction();
            EditorGUILayout.EndScrollView();
        }

        private static void DrawRunSnapshot(InteractionQaSnapshot snapshot)
        {
            EditorGUILayout.LabelField("Run", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Play Mode",
                snapshot.IsPlayMode ? "Yes" : "No"
            );
            EditorGUILayout.LabelField(
                "RunState",
                snapshot.RunState?.ToString() ?? "Unavailable"
            );
            EditorGUILayout.LabelField(
                "Current phase",
                snapshot.CurrentPhaseId?.ToString() ?? "None"
            );
            EditorGUILayout.LabelField(
                "Progress",
                $"{snapshot.Progress}/{snapshot.RequiredProgress}"
            );
            EditorGUILayout.LabelField(
                "Plan correct targets",
                Join(snapshot.PlannedTargetIds)
            );
            EditorGUILayout.LabelField(
                "Already accepted targets",
                Join(snapshot.AcceptedTargetIds)
            );
            EditorGUILayout.LabelField("Gates");
            EditorGUILayout.SelectableLabel(
                $"Start={snapshot.CanStart}; Replay={snapshot.CanReplay}; " +
                $"GiveUp={snapshot.CanGiveUp}; " +
                $"AbortInProgress={snapshot.AbortInProgress}",
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight)
            );
            EditorGUILayout.LabelField("Flow status");
            EditorGUILayout.HelpBox(snapshot.Status, MessageType.Info);
            EditorGUILayout.LabelField("Most recent task result");
            EditorGUILayout.HelpBox(snapshot.LastResult, MessageType.None);
        }

        private static void DrawPointingDiagnostics(
            InteractionQaPointingDiagnostics pointing)
        {
            EditorGUILayout.LabelField(
                "GhostPointingDetector",
                EditorStyles.boldLabel
            );
            EditorGUILayout.LabelField(
                "Component present",
                YesNo(pointing.ComponentPresent)
            );
            EditorGUILayout.LabelField(
                "PhaseConfigured",
                YesNo(pointing.PhaseConfigured)
            );
            EditorGUILayout.LabelField(
                "Finger rig complete",
                YesNo(pointing.FingerRigComplete)
            );
            EditorGUILayout.LabelField(
                "Pointing allowed",
                YesNo(pointing.PointingAllowed)
            );
            EditorGUILayout.LabelField(
                "Ray visible",
                YesNo(pointing.RayVisible)
            );
            EditorGUILayout.LabelField(
                "Current ray hit",
                string.IsNullOrEmpty(pointing.CurrentHitTargetId)
                    ? "None"
                    : pointing.CurrentHitTargetId
            );
            EditorGUILayout.LabelField(
                "Exposure seconds",
                pointing.ExposureSeconds.ToString("F3")
            );
        }

        private void DrawRunActions(InteractionQaSnapshot snapshot)
        {
            EditorGUILayout.LabelField("Run actions", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Start"))
            {
                Dispatch(actions.Start);
            }
            if (GUILayout.Button("Replay"))
            {
                Dispatch(actions.Replay);
            }
            if (GUILayout.Button("Give Up"))
            {
                Dispatch(actions.GiveUp);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Abort Run"))
            {
                Dispatch(() => actions.Abort());
            }
            if (GUILayout.Button("Confirm Result (API hook)"))
            {
                Dispatch(actions.ConfirmResult);
            }
            EditorGUILayout.EndHorizontal();
            if (snapshot.RunState.HasValue &&
                (snapshot.RunState.Value == RunState.Completed ||
                 snapshot.RunState.Value == RunState.Aborted))
            {
                EditorGUILayout.HelpBox(
                    "Result confirmation is shown above. If the current " +
                    "runtime has no public Confirm API, the button returns a " +
                    "clear failure and leaves terminal ownership unchanged.",
                    MessageType.Info
                );
            }
        }

        private void DrawInputActions(InteractionQaSnapshot snapshot)
        {
            EditorGUILayout.LabelField(
                "Current-phase input",
                EditorStyles.boldLabel
            );
            bool phaseTwo = snapshot.CurrentPhaseId == 2;
            string correctLabel = phaseTwo
                ? "Inject Correct Coin-Plate Pair"
                : "Inject Correct Target";
            string wrongLabel = phaseTwo
                ? "Inject Wrong Coin-Plate Pair"
                : "Inject Wrong Target";
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(correctLabel))
            {
                Dispatch(actions.InjectCorrectPhaseInput);
            }
            if (GUILayout.Button(wrongLabel))
            {
                Dispatch(actions.InjectWrongPhaseInput);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPresentationActions()
        {
            EditorGUILayout.LabelField(
                "Deterministic presentation",
                EditorStyles.boldLabel
            );
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Rebuild From Authority"))
            {
                Dispatch(actions.RebuildPresentation);
            }
            if (GUILayout.Button("Reset Presentation"))
            {
                Dispatch(actions.ResetPresentation);
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Capture Game View PNG"))
            {
                Dispatch(actions.CaptureGameView);
            }
            EditorGUILayout.LabelField(
                "Destination",
                "Assets/Screenshots/interaction-qa-*.png"
            );
        }

        private void DrawLastAction()
        {
            EditorGUILayout.LabelField("Last QA action", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                lastActionMessage,
                lastActionSucceeded ? MessageType.Info : MessageType.Error
            );
        }

        private void Dispatch(Func<InteractionQaActionResult> action)
        {
            try
            {
                InteractionQaActionResult result = action();
                lastActionSucceeded = result.Succeeded;
                lastActionMessage = result.Message;
            }
            catch (Exception exception)
            {
                lastActionSucceeded = false;
                lastActionMessage = "QA action failed safely: " +
                    exception.Message;
            }
        }

        private static string Join(
            System.Collections.Generic.IReadOnlyList<string> values)
        {
            return values == null || values.Count == 0
                ? "None"
                : string.Join(", ", values.Where(value => value != null));
        }

        private static string YesNo(bool value)
        {
            return value ? "Yes" : "No";
        }
    }
}
