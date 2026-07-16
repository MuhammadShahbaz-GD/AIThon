using System;
using System.Collections;
using KickTheBuddy.Physics;
using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class GameplayManager : MonoBehaviour
    {
        [Min(0f)] [SerializeField] private float deathCompletionDelay = .8f;

        private LevelsManager levels;
        private GameSaveManager saves;
        private SoundManager sounds;
        private RagdollController ragdoll;
        private RagdollInputManager ragdollInput;
        private float damage;
        private float remainingTime;
        private int score;
        private bool subscribed;
        private Coroutine deathCompletion;

        public GameplayState State { get; private set; } = GameplayState.Booting;
        public float Damage => damage;
        public int Score => score;
        public float RemainingTime => remainingTime;

        public event Action<GameplayState, GameplayState> StateChanged;
        public event Action<float, float> ObjectiveProgressChanged;
        public event Action<int> ScoreChanged;
        public event Action<int, int, Vector2, int> ScoreAwarded;
        public event Action<float> TimeChanged;
        public event Action<int, int> LevelCompleted;
        public event Action LevelFailed;

        public void Initialize(LevelsManager levelsManager, GameSaveManager saveManager, SoundManager soundManager)
        {
            levels = levelsManager;
            saves = saveManager;
            sounds = soundManager;
            ChangeState(GameplayState.MainMenu);
        }

        public void BeginLevel()
        {
            LevelDefinition level = levels.CurrentLevel;
            if (level == null) return;
            if (deathCompletion != null) StopCoroutine(deathCompletion);
            deathCompletion = null;
            BindRagdoll();
            ragdollInput?.SetInputEnabled(true);
            damage = 0f;
            score = 0;
            remainingTime = level.TimeLimit;
            Time.timeScale = 1f;
            sounds.PlayMusic(true);
            ChangeState(GameplayState.Playing);
            ObjectiveProgressChanged?.Invoke(0f, level.TargetDamage);
            ScoreChanged?.Invoke(0);
        }

        private void Update()
        {
            if (State != GameplayState.Playing) return;
            remainingTime = Mathf.Max(0f, remainingTime - Time.deltaTime);
            TimeChanged?.Invoke(remainingTime);
            if (remainingTime <= 0f) FailLevel();
        }

        public void Pause() { if (State == GameplayState.Playing) { Time.timeScale = 0f; ChangeState(GameplayState.Paused); } }
        public void Resume() { if (State == GameplayState.Paused) { Time.timeScale = 1f; ChangeState(GameplayState.Playing); } }

        public void Restart()
        {
            if (deathCompletion != null) StopCoroutine(deathCompletion);
            deathCompletion = null;
            Time.timeScale = 1f;
            ragdoll?.Revive();
            BeginLevel();
        }

        public void CompleteLevel()
        {
            if (State != GameplayState.Playing) return;
            ragdollInput?.SetInputEnabled(false);
            LevelDefinition level = levels.CurrentLevel;
            int stars = level.GetStars(score);
            saves.RecordLevel(level.LevelId, score, stars, level.CompletionCoins, levels.CurrentLevelIndex);
            sounds.Play(GameSound.LevelComplete);
            ChangeState(GameplayState.LevelComplete);
            LevelCompleted?.Invoke(score, stars);
        }

        public void FailLevel()
        {
            if (State != GameplayState.Playing) return;
            sounds.Play(GameSound.LevelFailed);
            ChangeState(GameplayState.LevelFailed);
            LevelFailed?.Invoke();
        }

        public void NextLevel()
        {
            Time.timeScale = 1f;
            if (levels.SelectNextLevel()) levels.LoadCurrentLevel();
            else ChangeState(GameplayState.MainMenu);
        }

        public void BindRagdoll()
        {
            UnbindRagdoll();
            ragdoll = FindObjectOfType<RagdollController>();
            if (ragdoll == null) return;
            ragdollInput = ragdoll.GetComponent<RagdollInputManager>();
            ragdoll.OnDamageTaken += HandleDamage;
            ragdoll.OnCharacterDied += HandleCharacterDied;
            subscribed = true;
        }

        private void HandleDamage(float amount, Vector2 point)
        {
            if (State != GameplayState.Playing) return;
            damage += amount;
            int combo = ragdoll != null ? ragdoll.CurrentCombo : 0;
            int gained = Mathf.Max(1, Mathf.RoundToInt(amount * (1f + combo * .1f)));
            score += gained;
            sounds.Play(amount >= 15f ? GameSound.HitHeavy : GameSound.HitLight, point);
            ObjectiveProgressChanged?.Invoke(damage, levels.CurrentLevel.TargetDamage);
            ScoreChanged?.Invoke(score);
            ScoreAwarded?.Invoke(gained, score, point, combo);
            if (damage >= levels.CurrentLevel.TargetDamage && (ragdoll == null || ragdoll.CurrentHealth > 0f))
                CompleteLevel();
        }

        private void HandleCharacterDied(Vector2 point)
        {
            if (State != GameplayState.Playing || deathCompletion != null) return;
            ragdollInput?.SetInputEnabled(false);
            deathCompletion = StartCoroutine(CompleteAfterDeath());
        }

        private IEnumerator CompleteAfterDeath()
        {
            float elapsed = 0f;
            while (elapsed < deathCompletionDelay)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            deathCompletion = null;
            CompleteLevel();
        }

        private void ChangeState(GameplayState next)
        {
            if (State == next) return;
            GameplayState previous = State;
            State = next;
            StateChanged?.Invoke(previous, next);
        }

        private void UnbindRagdoll()
        {
            if (subscribed && ragdoll != null)
            {
                ragdoll.OnDamageTaken -= HandleDamage;
                ragdoll.OnCharacterDied -= HandleCharacterDied;
            }
            subscribed = false;
            ragdoll = null;
            ragdollInput = null;
        }

        private void OnDestroy()
        {
            UnbindRagdoll();
            Time.timeScale = 1f;
        }

        private void OnValidate() => deathCompletionDelay = Mathf.Max(0f, deathCompletionDelay);
    }
}
