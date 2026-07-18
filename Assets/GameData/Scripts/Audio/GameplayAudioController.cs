using System;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using KickTheBuddy.Physics.VFX;
using UnityEngine;

namespace KickTheBuddy.Audio
{
    /// <summary>
    /// Converts authored gameplay events into semantic audio cues. Physics, damage, input, and
    /// animation remain independent of audio and never need to own AudioSources.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameplayAudioController : MonoBehaviour
    {
        [Header("Explicit Scene References")]
        [SerializeField] private SoundManager soundManager;
        [SerializeField] private GameplayManager gameplayManager;
        [SerializeField] private RagdollController ragdoll;
        [SerializeField] private RagdollInputManager ragdollInput;
        [SerializeField] private RagdollAnimationController animationController;
        [SerializeField] private SandboxTool2D[] tools = Array.Empty<SandboxTool2D>();
        [SerializeField] private CandyCannonController2D candyCannons;
        [SerializeField] private CandyGunController2D[] candyGuns = Array.Empty<CandyGunController2D>();
        [SerializeField] private LevelFourPipeController2D[] levelFourPipes = Array.Empty<LevelFourPipeController2D>();
        [SerializeField] private CracksModifier[] crackModifiers = Array.Empty<CracksModifier>();

        [Header("Impact Mix")]
        [Min(0f)] [SerializeField] private float mediumDamageThreshold = 2.25f;
        [Min(0f)] [SerializeField] private float heavyDamageThreshold = 5.5f;
        [Min(.01f)] [SerializeField] private float fullIntensityDamage = 8f;
        [Min(.01f)] [SerializeField] private float fullIntensitySpeed = 18f;
        [Min(.1f)] [SerializeField] private float lollipopAudibleSpeed = 2f;

        [Header("Character Expressions")]
        [Tooltip("Minimum spacing between spoken reactions. Every legitimate damage hit requests a reaction; this prevents overlapping voices during rapid fire.")]
        [Min(.05f)] [SerializeField] private float expressionRepeatDelay = .32f;

        [Header("Drag Foley")]
        [Min(.05f)] [SerializeField] private float stretchDistance = .55f;
        [Min(.05f)] [SerializeField] private float stretchRepeatDelay = .28f;

        private DamageReceiver2D draggedReceiver;
        private float nextStretchTime;
        private float nextExpressionTime;
        private int expressionVariation;
        private bool subscribed;

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        public void Configure(
            SoundManager sounds,
            GameplayManager gameplay,
            RagdollController controller,
            RagdollInputManager input,
            RagdollAnimationController animation,
            SandboxTool2D[] sandboxTools,
            CandyCannonController2D cannons,
            CracksModifier[] cracks)
        {
            bool wasSubscribed = subscribed;
            if (wasSubscribed) Unsubscribe();
            soundManager = sounds;
            gameplayManager = gameplay;
            ragdoll = controller;
            ragdollInput = input;
            animationController = animation;
            tools = sandboxTools ?? Array.Empty<SandboxTool2D>();
            candyCannons = cannons;
            crackModifiers = cracks ?? Array.Empty<CracksModifier>();
            if (wasSubscribed && isActiveAndEnabled) Subscribe();
        }

        /// <summary>
        /// Replaces scene-serialized service references after a gameplay scene reload. The
        /// persistent bootstrapper owns these services; the duplicate scene bootstrapper is
        /// destroyed, so keeping its references would silence every level after the first.
        /// </summary>
        public void RebindServices(SoundManager sounds, GameplayManager gameplay)
        {
            if (soundManager == sounds && gameplayManager == gameplay && subscribed) return;
            if (subscribed) Unsubscribe();
            soundManager = sounds;
            gameplayManager = gameplay;
            if (isActiveAndEnabled) Subscribe();
        }

        private void Subscribe()
        {
            if (subscribed) return;
            if (ragdoll != null)
            {
                ragdoll.OnImpactResolved += HandleImpact;
                ragdoll.OnComboAdvanced += HandleCombo;
                ragdoll.OnLimbBroken += HandleLimbBroken;
                ragdoll.OnCharacterKO += HandleKnockout;
                ragdoll.OnCharacterRevived += HandleRevived;
                ragdoll.OnCharacterDied += HandleDeath;
            }
            if (ragdollInput != null)
            {
                ragdollInput.DragStarted += HandleDragStarted;
                ragdollInput.DragUpdated += HandleDragUpdated;
                ragdollInput.DragEnded += HandleDragEnded;
            }
            if (animationController != null)
            {
                animationController.FaceReactionStarted += HandleFaceReaction;
                animationController.AnnoyedReactionPlayed += HandleAnnoyed;
                animationController.IdleAnimationStarted += HandleIdle;
            }
            if (gameplayManager != null) gameplayManager.StateChanged += HandleGameplayState;
            if (candyCannons != null)
            {
                candyCannons.CannonFired += HandleCannonFired;
                candyCannons.ProjectileHit += HandleCannonHit;
                candyCannons.ProjectileMissed += HandleCannonMiss;
            }
            for (int i = 0; i < candyGuns.Length; i++)
            {
                CandyGunController2D gun = candyGuns[i];
                if (gun == null) continue;
                gun.Fired += HandleCandyGunFired;
                gun.ProjectileHit += HandleCandyGunHit;
            }
            for (int i = 0; i < levelFourPipes.Length; i++)
            {
                LevelFourPipeController2D pipe = levelFourPipes[i];
                if (pipe == null) continue;
                pipe.ProjectileFired += HandlePipeFired;
                pipe.ProjectileImpacted += HandlePipeImpact;
            }
            for (int i = 0; i < tools.Length; i++)
            {
                SandboxTool2D tool = tools[i];
                if (tool == null) continue;
                tool.Released += HandleToolReleased;
                tool.Impacted += HandleToolImpact;
                tool.Stuck += HandleToolStuck;
                tool.Detached += HandleToolDetached;
            }
            for (int i = 0; i < crackModifiers.Length; i++)
                if (crackModifiers[i] != null)
                    crackModifiers[i].CrackStageChanged += HandleCrackStage;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (ragdoll != null)
            {
                ragdoll.OnImpactResolved -= HandleImpact;
                ragdoll.OnComboAdvanced -= HandleCombo;
                ragdoll.OnLimbBroken -= HandleLimbBroken;
                ragdoll.OnCharacterKO -= HandleKnockout;
                ragdoll.OnCharacterRevived -= HandleRevived;
                ragdoll.OnCharacterDied -= HandleDeath;
            }
            if (ragdollInput != null)
            {
                ragdollInput.DragStarted -= HandleDragStarted;
                ragdollInput.DragUpdated -= HandleDragUpdated;
                ragdollInput.DragEnded -= HandleDragEnded;
            }
            if (animationController != null)
            {
                animationController.FaceReactionStarted -= HandleFaceReaction;
                animationController.AnnoyedReactionPlayed -= HandleAnnoyed;
                animationController.IdleAnimationStarted -= HandleIdle;
            }
            if (gameplayManager != null) gameplayManager.StateChanged -= HandleGameplayState;
            if (candyCannons != null)
            {
                candyCannons.CannonFired -= HandleCannonFired;
                candyCannons.ProjectileHit -= HandleCannonHit;
                candyCannons.ProjectileMissed -= HandleCannonMiss;
            }
            for (int i = 0; i < candyGuns.Length; i++)
            {
                CandyGunController2D gun = candyGuns[i];
                if (gun == null) continue;
                gun.Fired -= HandleCandyGunFired;
                gun.ProjectileHit -= HandleCandyGunHit;
            }
            for (int i = 0; i < levelFourPipes.Length; i++)
            {
                LevelFourPipeController2D pipe = levelFourPipes[i];
                if (pipe == null) continue;
                pipe.ProjectileFired -= HandlePipeFired;
                pipe.ProjectileImpacted -= HandlePipeImpact;
            }
            for (int i = 0; i < tools.Length; i++)
            {
                SandboxTool2D tool = tools[i];
                if (tool == null) continue;
                tool.Released -= HandleToolReleased;
                tool.Impacted -= HandleToolImpact;
                tool.Stuck -= HandleToolStuck;
                tool.Detached -= HandleToolDetached;
            }
            for (int i = 0; i < crackModifiers.Length; i++)
                if (crackModifiers[i] != null)
                    crackModifiers[i].CrackStageChanged -= HandleCrackStage;
            draggedReceiver = null;
            subscribed = false;
        }

        private void HandleImpact(float damage, float impactSpeed, Vector2 point)
        {
            GameSound cue = damage >= heavyDamageThreshold
                ? GameSound.HitHeavy
                : damage >= mediumDamageThreshold
                    ? GameSound.HitMedium
                    : GameSound.HitLight;
            float damageStrength = damage / fullIntensityDamage;
            float speedStrength = impactSpeed / fullIntensitySpeed;
            Play(cue, point, Mathf.Clamp01(Mathf.Max(.2f, Mathf.Max(damageStrength, speedStrength))));
            if (damage >= mediumDamageThreshold)
                Play(GameSound.CandyRattle, point, Mathf.Clamp01(.28f + damageStrength * .55f));
            PlayDamageExpression(point, damage);
        }

        private void HandleCombo(int count, float damage, Vector2 point)
        {
            if (count < 2) return;
            Play(count >= 4 ? GameSound.ComboHigh : GameSound.Combo, point,
                Mathf.Clamp01(.55f + count * .09f));
        }

        private void HandleLimbBroken(Rigidbody2D body, Vector2 point)
        {
            Play(GameSound.LimbBreak, point, 1f);
            Play(GameSound.SpringDetach, point, .82f);
        }

        private void HandleKnockout() =>
            Play(GameSound.CharacterKO, ragdoll != null ? ragdoll.transform.position : transform.position, .9f);

        private void HandleRevived() =>
            Play(GameSound.CharacterRelief, ragdoll != null ? ragdoll.transform.position : transform.position, .75f);

        private void HandleDeath(Vector2 point) => Play(GameSound.DeathBlast, point, 1f);

        private void HandleDragStarted(DamageReceiver2D receiver, Vector2 point)
        {
            draggedReceiver = receiver;
            nextStretchTime = Time.unscaledTime + stretchRepeatDelay;
            Play(GameSound.Grab, point, .72f);
        }

        private void HandleDragUpdated(DamageReceiver2D receiver, Vector2 point)
        {
            if (receiver == null || receiver != draggedReceiver || receiver.Body == null ||
                Time.unscaledTime < nextStretchTime) return;
            if ((receiver.Body.worldCenterOfMass - point).sqrMagnitude < stretchDistance * stretchDistance) return;
            nextStretchTime = Time.unscaledTime + stretchRepeatDelay;
            Play(GameSound.Stretch, point, .56f);
        }

        private void HandleDragEnded(DamageReceiver2D receiver, Vector2 point)
        {
            Play(GameSound.Release, point, .68f);
            Play(GameSound.SpringRecoil, point, .44f);
            draggedReceiver = null;
        }

        private void HandleFaceReaction(RagdollFaceExpression expression, float duration)
        {
            Vector2 point = ragdoll != null ? ragdoll.transform.position : transform.position;
            if (expression == RagdollFaceExpression.Shock)
            {
                PlayExpression(point, false, .68f);
            }
            else if (expression == RagdollFaceExpression.Cry ||
                     expression == RagdollFaceExpression.Depressed)
            {
                PlayExpression(point, true, .72f);
            }
        }

        private void PlayDamageExpression(Vector2 point, float damage) =>
            PlayExpression(point, damage >= heavyDamageThreshold, Mathf.Clamp01(.58f + damage / fullIntensityDamage * .3f));

        private void PlayExpression(Vector2 point, bool severe, float intensity)
        {
            if (Time.unscaledTime < nextExpressionTime) return;
            nextExpressionTime = Time.unscaledTime + expressionRepeatDelay;
            int variation = expressionVariation++ % 3;
            GameSound cue = severe
                ? variation == 0 ? GameSound.CharacterDontHitMe :
                  variation == 1 ? GameSound.CharacterMan : GameSound.CharacterCry
                : variation == 0 ? GameSound.CharacterOuch :
                  variation == 1 ? GameSound.CharacterOoo : GameSound.CharacterGasp;
            Play(cue, point, intensity);
        }

        private void HandleAnnoyed(Rigidbody2D body, float strength) =>
            Play(GameSound.CharacterAnnoyed,
                body != null ? body.worldCenterOfMass : (Vector2)transform.position,
                Mathf.Lerp(.45f, .8f, strength));

        private void HandleIdle()
        {
            if (UnityEngine.Random.value <= .22f)
                Play(GameSound.CharacterSmile,
                    ragdoll != null ? ragdoll.transform.position : transform.position, .42f);
        }

        private void HandleToolReleased(SandboxTool2D tool, Vector2 point)
        {
            if (tool == null) return;
            GameSound cue = tool.Kind == SandboxToolKind.Jelly ? GameSound.JellyThrow :
                tool.Kind == SandboxToolKind.GummyBear || tool.Kind == SandboxToolKind.LooseCandy
                    ? GameSound.GummyThrow
                    : tool.Kind == SandboxToolKind.CandyGun ? GameSound.Release : GameSound.CandyToolSwing;
            Play(cue, point, .62f);
        }

        private void HandleToolImpact(SandboxTool2D tool, Rigidbody2D target, float speed, Vector2 point)
        {
            if (tool == null || speed < lollipopAudibleSpeed) return;
            if (tool.Kind == SandboxToolKind.Jelly)
            {
                Play(GameSound.JellySplat, point, Mathf.Clamp01(.38f + speed / fullIntensitySpeed));
                return;
            }
            if (target == null || target.GetComponent<RagdollPartHealth>() == null) return;
            GameSound cue = tool.Kind == SandboxToolKind.GummyBear || tool.Kind == SandboxToolKind.LooseCandy
                ? GameSound.GummyHit
                : tool.Kind == SandboxToolKind.CandyJar ? GameSound.CandyJarHit : GameSound.CandyToolHit;
            Play(cue, point, Mathf.Clamp01(.4f + speed / fullIntensitySpeed));
        }

        private void HandleToolStuck(SandboxTool2D tool, Rigidbody2D target, Vector2 point)
        {
            if (tool != null && tool.Kind == SandboxToolKind.Jelly)
                Play(GameSound.JellyStick, point, .62f);
        }

        private void HandleToolDetached(SandboxTool2D tool)
        {
            if (tool != null && tool.Kind == SandboxToolKind.Jelly)
                Play(GameSound.JellySlide, tool.transform.position, .5f);
        }

        private void HandleCandyGunFired(CandyGunController2D gun, Vector2 origin, Vector2 velocity) =>
            Play(GameSound.CandyGunFire, origin, .78f);

        private void HandleCandyGunHit(
            CandyGunController2D gun, Rigidbody2D body, Vector2 point, float speed)
        {
            if (body != null && body.GetComponent<RagdollPartHealth>() != null)
                Play(GameSound.CandyGunImpact, point, Mathf.Clamp01(.5f + speed / fullIntensitySpeed));
        }

        private void HandlePipeFired(bool bomb, Vector2 origin) =>
            Play(bomb ? GameSound.PipeBombLaunch : GameSound.PipeSodaLaunch, origin, bomb ? 1f : .82f);

        private void HandlePipeImpact(bool bomb, Vector2 point) =>
            Play(bomb ? GameSound.PipeBombBlast : GameSound.PipeSodaImpact, point, bomb ? 1f : .86f);

        private void HandleCannonFired(
            CandyCannonSide side, Vector2 origin, Vector2 velocity, bool charged) =>
            Play(charged ? GameSound.CannonChargedFire : GameSound.CannonFire, origin,
                charged ? 1f : .82f);

        private void HandleCannonHit(
            CandyCannonSide side, Rigidbody2D body, float damage, Vector2 point) =>
            Play(GameSound.CannonImpact, point,
                Mathf.Clamp01(.58f + damage / Mathf.Max(.01f, fullIntensityDamage)));

        private void HandleCannonMiss(CandyCannonSide side) =>
            Play(GameSound.CannonMiss, transform.position, .42f);

        private void HandleCrackStage(CracksModifier modifier, int stage)
        {
            if (stage <= 0 || modifier == null) return;
            Play(stage >= 2 ? GameSound.CrackSevere : GameSound.CrackNew,
                modifier.transform.position, stage >= 2 ? .82f : .58f);
        }

        private void HandleGameplayState(GameplayState previous, GameplayState next)
        {
            if (next == GameplayState.Playing) Play(GameSound.LevelStart, transform.position, .72f);
            else if (next == GameplayState.LevelComplete) Play(GameSound.LevelComplete, transform.position, 1f);
            else if (next == GameplayState.LevelFailed) Play(GameSound.LevelFailed, transform.position, .82f);
        }

        private void Play(GameSound cue, Vector2 point, float intensity)
        {
            if (soundManager != null) soundManager.PlaySfx(cue, point, intensity);
        }

        private void OnValidate()
        {
            mediumDamageThreshold = Mathf.Max(0f, mediumDamageThreshold);
            heavyDamageThreshold = Mathf.Max(mediumDamageThreshold, heavyDamageThreshold);
            fullIntensityDamage = Mathf.Max(.01f, fullIntensityDamage);
            fullIntensitySpeed = Mathf.Max(.01f, fullIntensitySpeed);
            lollipopAudibleSpeed = Mathf.Max(.1f, lollipopAudibleSpeed);
            expressionRepeatDelay = Mathf.Max(.05f, expressionRepeatDelay);
            stretchDistance = Mathf.Max(.05f, stretchDistance);
            stretchRepeatDelay = Mathf.Max(.05f, stretchRepeatDelay);
            if (tools == null) tools = Array.Empty<SandboxTool2D>();
            if (candyGuns == null) candyGuns = Array.Empty<CandyGunController2D>();
            if (levelFourPipes == null) levelFourPipes = Array.Empty<LevelFourPipeController2D>();
            if (crackModifiers == null) crackModifiers = Array.Empty<CracksModifier>();
        }
    }
}
