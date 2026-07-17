using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using UnityEngine;

namespace KickTheBuddy.Haptics
{
    /// <summary>Event adapter only; gameplay, ragdoll, persistence, and platform drivers remain independent.</summary>
    [DisallowMultipleComponent]
    public sealed class GameplayHapticsAdapter : MonoBehaviour
    {
        [SerializeField] private HapticsManager haptics;
        [Min(0f)] [SerializeField] private float heavyDamageThreshold = 15f;
        private GameplayManager gameplay; private GameSaveManager saves; private GameFlowController flow; private RagdollController ragdoll;
        private void Start()
        {
            if (haptics == null) haptics = GetComponent<HapticsManager>(); GameBootstrapper root = GameBootstrapper.Instance; if (root == null || haptics == null) { enabled = false; return; }
            gameplay = root.Gameplay; saves = root.Saves; flow = root.Flow; haptics.Configure(saves.Data.vibrationEnabled);
            gameplay.LevelCompleted += HandleComplete; gameplay.LevelFailed += HandleFailed; if (flow != null) flow.GameplayStarted += HandleGameplayStarted; BindRagdoll();
        }
        public void SetEnabled(bool value) { if (saves == null || haptics == null) return; saves.Data.vibrationEnabled = value; saves.Save(); haptics.Configure(value); if (value) haptics.Play(GameHaptic.Selection); }
        public void Selection() { haptics?.Play(GameHaptic.Selection); }
        private void HandleGameplayStarted(LevelDefinition level) => BindRagdoll();
        private void BindRagdoll() { UnbindRagdoll(); ragdoll = FindObjectOfType<RagdollController>(); if (ragdoll == null) return; ragdoll.OnDamageTaken += HandleDamage; ragdoll.OnLimbBroken += HandleLimbBreak; }
        private void UnbindRagdoll() { if (ragdoll == null) return; ragdoll.OnDamageTaken -= HandleDamage; ragdoll.OnLimbBroken -= HandleLimbBreak; ragdoll = null; }
        private void HandleDamage(float damage, Vector2 point) { haptics.Play(damage >= heavyDamageThreshold ? GameHaptic.HeavyHit : GameHaptic.LightHit, Mathf.Clamp01(damage / 25f)); }
        private void HandleLimbBreak(Rigidbody2D body, Vector2 point) { haptics.Play(GameHaptic.LimbBreak); }
        private void HandleComplete(int score, int stars) { haptics.Notification(HapticNotification.Success); }
        private void HandleFailed() { haptics.Notification(HapticNotification.Error); }
        private void OnDestroy() { if (gameplay != null) { gameplay.LevelCompleted -= HandleComplete; gameplay.LevelFailed -= HandleFailed; } if (flow != null) flow.GameplayStarted -= HandleGameplayStarted; UnbindRagdoll(); }
    }
}
