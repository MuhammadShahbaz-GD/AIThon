using System;
using KickTheBuddy.Physics;
using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class GameplayManager : MonoBehaviour
    {
        private LevelsManager levels; private GameSaveManager saves; private SoundManager sounds; private RagdollController ragdoll; private RagdollInputManager ragdollInput;
        private float damage, remainingTime; private int score; private bool subscribed;
        public GameplayState State { get; private set; } = GameplayState.Booting;
        public float Damage => damage; public int Score => score; public float RemainingTime => remainingTime;
        public event Action<GameplayState, GameplayState> StateChanged; public event Action<float, float> ObjectiveProgressChanged; public event Action<int> ScoreChanged; public event Action<float> TimeChanged; public event Action<int, int> LevelCompleted; public event Action LevelFailed;
        public void Initialize(LevelsManager levelsManager, GameSaveManager saveManager, SoundManager soundManager) { levels = levelsManager; saves = saveManager; sounds = soundManager; ChangeState(GameplayState.MainMenu); }
        public void BeginLevel()
        {
            LevelDefinition level = levels.CurrentLevel; if (level == null) return; BindRagdoll(); ragdollInput?.SetInputEnabled(true); damage = 0f; score = 0; remainingTime = level.TimeLimit; Time.timeScale = 1f; sounds.PlayMusic(true); ChangeState(GameplayState.Playing); ObjectiveProgressChanged?.Invoke(0f, level.TargetDamage); ScoreChanged?.Invoke(0);
        }
        private void Update() { if (State != GameplayState.Playing) return; remainingTime = Mathf.Max(0f, remainingTime - Time.deltaTime); TimeChanged?.Invoke(remainingTime); if (remainingTime <= 0f) FailLevel(); }
        public void Pause() { if (State != GameplayState.Playing) return; Time.timeScale = 0f; ChangeState(GameplayState.Paused); }
        public void Resume() { if (State != GameplayState.Paused) return; Time.timeScale = 1f; ChangeState(GameplayState.Playing); }
        public void Restart() { Time.timeScale = 1f; if (ragdoll != null) ragdoll.Revive(); BeginLevel(); }
        public void CompleteLevel()
        {
            if (State != GameplayState.Playing) return; ragdollInput?.SetInputEnabled(false); LevelDefinition level = levels.CurrentLevel; int stars = level.GetStars(score); saves.RecordLevel(level.LevelId, score, stars, level.CompletionCoins, levels.CurrentLevelIndex); sounds.Play(GameSound.LevelComplete); ChangeState(GameplayState.LevelComplete); LevelCompleted?.Invoke(score, stars);
        }
        public void FailLevel() { if (State != GameplayState.Playing) return; sounds.Play(GameSound.LevelFailed); ChangeState(GameplayState.LevelFailed); LevelFailed?.Invoke(); }
        public void NextLevel() { Time.timeScale = 1f; if (levels.SelectNextLevel()) levels.LoadCurrentLevel(); else ChangeState(GameplayState.MainMenu); }
        public void BindRagdoll()
        {
            UnbindRagdoll(); ragdoll = FindObjectOfType<RagdollController>(); if (ragdoll == null) return; ragdollInput = ragdoll.GetComponent<RagdollInputManager>(); ragdoll.OnDamageTaken += HandleDamage; ragdoll.OnCharacterKO += HandleCharacterKO; subscribed = true;
        }
        private void HandleDamage(float amount, Vector2 point) { if (State != GameplayState.Playing) return; damage += amount; int gained = Mathf.Max(1, Mathf.RoundToInt(amount * (1f + ragdoll.CurrentCombo * .1f))); score += gained; sounds.Play(amount >= 15f ? GameSound.HitHeavy : GameSound.HitLight, point); ObjectiveProgressChanged?.Invoke(damage, levels.CurrentLevel.TargetDamage); ScoreChanged?.Invoke(score); if (damage >= levels.CurrentLevel.TargetDamage) CompleteLevel(); }
        private void HandleCharacterKO()
        {
            // Knockout is also used for temporary force reactions. Only zero health is a terminal death.
            if (State == GameplayState.Playing && ragdoll != null && ragdoll.CurrentHealth <= 0f) CompleteLevel();
        }
        private void ChangeState(GameplayState next) { if (State == next) return; GameplayState previous = State; State = next; StateChanged?.Invoke(previous, next); }
        private void UnbindRagdoll() { if (subscribed && ragdoll != null) { ragdoll.OnDamageTaken -= HandleDamage; ragdoll.OnCharacterKO -= HandleCharacterKO; } subscribed = false; ragdoll = null; ragdollInput = null; }
        private void OnDestroy() { UnbindRagdoll(); Time.timeScale = 1f; }
    }
}
