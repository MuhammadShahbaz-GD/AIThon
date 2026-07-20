#if UNITY_EDITOR
using System;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Focused runtime smoke for the authored Level 05 press cycle.</summary>
    [InitializeOnLoad]
    public static class HydraulicPressPlayModeSmokeEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string ActiveKey = "KickTheBuddy.HydraulicPressSmoke.Active";
        private const string StartedKey = "KickTheBuddy.HydraulicPressSmoke.Started";
        private const string SuccessKey = "KickTheBuddy.HydraulicPressSmoke.Success";
        private const string StartTimeKey = "KickTheBuddy.HydraulicPressSmoke.StartTime";
        private static HydroicPress press;
        private static float startY;
        private static float startX;
        private static float minimumY;
        private static bool sawReturn;

        static HydraulicPressPlayModeSmokeEditor()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        [MenuItem("Tools/Gameplay/Levels/Validate Level 05 Hydraulic Press In Play Mode")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before validating the hydraulic press.");
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(StartedKey, false);
            SessionState.SetBool(SuccessKey, false);
            SessionState.SetString(StartTimeKey, EditorApplication.timeSinceStartup.ToString("R"));
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Hook();
            EditorApplication.isPlaying = true;
        }

        private static void Hook()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false))
            {
                EditorApplication.update -= Tick;
                return;
            }
            if (!EditorApplication.isPlaying)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(StartedKey, false))
                {
                    bool succeeded = SessionState.GetBool(SuccessKey, false);
                    SessionState.SetBool(ActiveKey, false);
                    SessionState.SetBool(StartedKey, false);
                    EditorApplication.update -= Tick;
                    if (!succeeded)
                        Debug.LogError("HYDRAULIC_PRESS_LEVEL_05_SMOKE_FAILED: Play Mode stopped before the hydraulic press completed its loop check.");
                }
                return;
            }

            try
            {
                if (!SessionState.GetBool(StartedKey, false))
                {
                    press = FindScenePress();
                    if (press == null) return;
                    Transform levelRoot = press.transform;
                    while (levelRoot.parent != null && levelRoot.name != "Level 05 New -hydrolic press")
                        levelRoot = levelRoot.parent;
                    if (levelRoot.name != "Level 05 New -hydrolic press")
                        throw new InvalidOperationException("The press is not under the authored Level 05 root.");
                    levelRoot.gameObject.SetActive(true);
                    press.SetInputEnabled(true);
                    Vector2 authoredPosition = press.MachinePosition;
                    press.SetMachineWorldPosition(authoredPosition + Vector2.right * 1.25f);
                    startX = press.transform.position.x;
                    startY = press.transform.position.y;
                    minimumY = startY;
                    sawReturn = false;
                    press.Activate();
                    if (!press.IsActivated || !press.IsDescending)
                        throw new InvalidOperationException("Tap activation did not begin the press descent.");
                    RagdollAttackManager2D attack = press.GetComponentInChildren<RagdollAttackManager2D>(true);
                    if (attack == null || !Mathf.Approximately(attack.CalculateDamage(0f), 220f))
                        throw new InvalidOperationException("The press hit plate does not have its 220 damage profile.");
                    SessionState.SetBool(StartedKey, true);
                    SessionState.SetString(StartTimeKey, EditorApplication.timeSinceStartup.ToString("R"));
                    return;
                }

                if (press == null) throw new InvalidOperationException("The press was destroyed during its runtime smoke.");
                if (Mathf.Abs(press.transform.position.x - startX) > .05f)
                    throw new InvalidOperationException("The freely positioned press snapped back toward screen center.");
                float y = press.transform.position.y;
                if (y < minimumY) minimumY = y;
                if (minimumY < startY - .5f && y > minimumY + .15f) sawReturn = true;
                double elapsed = Elapsed();
                if (sawReturn && press.IsDescending && elapsed > .8d)
                {
                    Finish(true, string.Empty);
                    return;
                }
                if (elapsed > 6d)
                    throw new InvalidOperationException("The press did not descend, return, and begin its next crush within six seconds.");
            }
            catch (Exception exception)
            {
                Finish(false, exception.Message);
            }
        }

        private static HydroicPress FindScenePress()
        {
            HydroicPress[] candidates = Resources.FindObjectsOfTypeAll<HydroicPress>();
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i] != null && candidates[i].gameObject.scene.IsValid()) return candidates[i];
            return null;
        }

        private static double Elapsed()
        {
            double start;
            return double.TryParse(SessionState.GetString(StartTimeKey, "0"), out start)
                ? EditorApplication.timeSinceStartup - start
                : 0d;
        }

        private static void Finish(bool success, string failure)
        {
            SessionState.SetBool(ActiveKey, false);
            SessionState.SetBool(StartedKey, false);
            SessionState.SetBool(SuccessKey, success);
            EditorApplication.update -= Tick;
            if (success)
                Debug.Log("HYDRAULIC_PRESS_LEVEL_05_SMOKE_OK: press descended, returned, looped, and retained its authored damage profile.");
            else
                Debug.LogError("HYDRAULIC_PRESS_LEVEL_05_SMOKE_FAILED: " + failure);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }
    }
}
#endif
