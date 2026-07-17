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
        private SandboxToolInput2D sandboxToolInput;
        private float damage;
        private float remainingTime;
        private int score;
        private bool subscribed;
        private Coroutine deathCompletion;
        private int lastReportedSecond = -1;

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
        public event Action NextLevelRequested;
        public event Action RetryLevelRequested;

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
            if (!BindRagdoll())
            {
                ChangeState(GameplayState.LevelFailed);
                LevelFailed?.Invoke();
                return;
            }
            ragdollInput?.SetInputEnabled(true);
            sandboxToolInput?.ResetTools();
            sandboxToolInput?.SetInputEnabled(true);
            damage = 0f;
            score = 0;
            remainingTime = level.TimeLimit;
            lastReportedSecond = Mathf.CeilToInt(remainingTime);
            Time.timeScale = 1f;
            sounds.PlayMusic(true);
            ChangeState(GameplayState.Playing);
            ObjectiveProgressChanged?.Invoke(0f, level.TargetDamage);
            ScoreChanged?.Invoke(0);
            TimeChanged?.Invoke(remainingTime);
        }

        public void PrepareForLevelLoad()
        {
            StopDeathCompletion();
            ragdollInput?.SetInputEnabled(false);
            sandboxToolInput?.SetInputEnabled(false);
            UnbindRagdoll();
            Time.timeScale = 1f;
            ChangeState(GameplayState.Loading);
        }

        public void EnterMainMenu()
        {
            StopDeathCompletion();
            ragdollInput?.SetInputEnabled(false);
            sandboxToolInput?.SetInputEnabled(false);
            UnbindRagdoll();
            Time.timeScale = 1f;
            sounds?.PlayMusic(false);
            ChangeState(GameplayState.MainMenu);
        }

        private void Update()
        {
            if (State != GameplayState.Playing) return;
            remainingTime = Mathf.Max(0f, remainingTime - Time.deltaTime);
            int displayedSecond = Mathf.CeilToInt(remainingTime);
            if (displayedSecond != lastReportedSecond)
            {
                lastReportedSecond = displayedSecond;
                TimeChanged?.Invoke(remainingTime);
            }
            if (remainingTime <= 0f) FailLevel();
        }

        public void Pause() { if (State == GameplayState.Playing) { Time.timeScale = 0f; ChangeState(GameplayState.Paused); } }
        public void Resume() { if (State == GameplayState.Paused) { Time.timeScale = 1f; ChangeState(GameplayState.Playing); } }

        public void Restart()
        {
            if (State == GameplayState.Booting || State == GameplayState.MainMenu ||
                State == GameplayState.Loading) return;
            Time.timeScale = 1f;
            RetryLevelRequested?.Invoke();
        }

        public void CompleteLevel()
        {
            if (State != GameplayState.Playing) return;
            ragdollInput?.SetInputEnabled(false);
            sandboxToolInput?.SetInputEnabled(false);
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
            ragdollInput?.SetInputEnabled(false);
            sandboxToolInput?.SetInputEnabled(false);
            sounds.Play(GameSound.LevelFailed);
            ChangeState(GameplayState.LevelFailed);
            LevelFailed?.Invoke();
        }

        public void NextLevel()
        {
            if (State != GameplayState.LevelComplete) return;
            Time.timeScale = 1f;
            NextLevelRequested?.Invoke();
        }

        public bool BindRagdoll()
        {
            UnbindRagdoll();
            GameplayLevelSceneController sceneLevels = GameplayLevelSceneController.Active;
            if (sceneLevels == null || sceneLevels.ActiveLevelRoot == null)
            {
                Debug.LogError("The gameplay scene Levels controller did not activate its saved level.", this);
                return false;
            }

            ragdoll = sceneLevels.ActiveRagdoll;
            ragdollInput = sceneLevels.ActiveRagdollInput;
            sandboxToolInput = sceneLevels.ActiveSandboxToolInput;
            if (ragdoll == null || ragdollInput == null)
            {
                Debug.LogError("The selected level has incomplete ragdoll references.", sceneLevels);
                return false;
            }
            ragdoll.OnDamageTaken += HandleDamage;
            ragdoll.OnCharacterDied += HandleCharacterDied;
            subscribed = true;
            return true;
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
            if (levels.CurrentLevel.CompletionRule == LevelCompletionRule.TargetDamage &&
                damage >= levels.CurrentLevel.TargetDamage &&
                (ragdoll == null || ragdoll.CurrentHealth > 0f))
                CompleteLevel();
        }

        private void HandleCharacterDied(Vector2 point)
        {
            if (State != GameplayState.Playing || deathCompletion != null) return;
            ragdollInput?.SetInputEnabled(false);
            sandboxToolInput?.SetInputEnabled(false);
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
            sandboxToolInput?.SetInputEnabled(false);
            ragdoll = null;
            ragdollInput = null;
            sandboxToolInput = null;
        }

        private void StopDeathCompletion()
        {
            if (deathCompletion == null) return;
            StopCoroutine(deathCompletion);
            deathCompletion = null;
        }

        private void OnDestroy()
        {
            UnbindRagdoll();
            Time.timeScale = 1f;
        }

        private void OnValidate() => deathCompletionDelay = Mathf.Max(0f, deathCompletionDelay);
    }
}
