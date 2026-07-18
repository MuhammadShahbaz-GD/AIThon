using System;
using System.Collections;
using KickTheBuddy.Physics;
using UnityEngine;
using UnityEngine.Serialization;

namespace KickTheBuddy.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class GameplayManager : MonoBehaviour
    {
        [FormerlySerializedAs("deathCompletionDelay")]
        [Range(2f, 3f)] [SerializeField] private float deathNextLevelDelay = 2.5f;

        private LevelsManager levels;
        private GameSaveManager saves;
        private SoundManager sounds;
        private RagdollController ragdoll;
        private RagdollInputManager ragdollInput;
        private SandboxToolInput2D sandboxToolInput;
        private CandyCannonController2D candyCannons;
        private LevelFourPipeController2D levelFourPipes;
        private float damage;
        private int score;
        private bool subscribed;
        private Coroutine deathCompletion;

        public GameplayState State { get; private set; } = GameplayState.Booting;
        public float Damage => damage;
        public int Score => score;

        public event Action<GameplayState, GameplayState> StateChanged;
        /// <summary>Reports actual ragdoll health lost and aggregate maximum health for the HUD.</summary>
        public event Action<float, float> ObjectiveProgressChanged;
        public event Action<int> ScoreChanged;
        public event Action<int, int, Vector2, int> ScoreAwarded;
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
            candyCannons?.ResetCannons();
            candyCannons?.SetInputEnabled(true);
            levelFourPipes?.ResetController();
            levelFourPipes?.SetInputEnabled(true);
            damage = 0f;
            score = 0;
            Time.timeScale = 1f;
            sounds.PlayMusic(true);
            ChangeState(GameplayState.Playing);
            PublishRagdollDamageProgress();
            ScoreChanged?.Invoke(0);
        }

        public void PrepareForLevelLoad()
        {
            StopDeathCompletion();
            ragdollInput?.SetInputEnabled(false);
            sandboxToolInput?.SetInputEnabled(false);
            candyCannons?.SetInputEnabled(false);
            levelFourPipes?.SetInputEnabled(false);
            UnbindRagdoll();
            Time.timeScale = 1f;
            ChangeState(GameplayState.Loading);
        }

        public void EnterMainMenu()
        {
            StopDeathCompletion();
            ragdollInput?.SetInputEnabled(false);
            sandboxToolInput?.SetInputEnabled(false);
            candyCannons?.SetInputEnabled(false);
            levelFourPipes?.SetInputEnabled(false);
            UnbindRagdoll();
            Time.timeScale = 1f;
            sounds?.PlayMusic(false);
            ChangeState(GameplayState.MainMenu);
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
            candyCannons?.CompleteInteraction();
            levelFourPipes?.SetInputEnabled(false);
            LevelDefinition level = levels.CurrentLevel;
            int stars = level.GetStars(score);
            saves.RecordLevel(level.LevelId, score, stars, level.CompletionCoins, levels.CurrentLevelIndex);
            ChangeState(GameplayState.LevelComplete);
            LevelCompleted?.Invoke(score, stars);
        }

        public void FailLevel()
        {
            if (State != GameplayState.Playing) return;
            ragdollInput?.SetInputEnabled(false);
            sandboxToolInput?.SetInputEnabled(false);
            candyCannons?.CompleteInteraction();
            levelFourPipes?.SetInputEnabled(false);
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
            candyCannons = sceneLevels.ActiveCandyCannons;
            levelFourPipes = sceneLevels.ActiveLevelFourPipes;
            if (candyCannons != null) candyCannons.ConfigureAudio(sounds);
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
            PublishRagdollDamageProgress();
            ScoreChanged?.Invoke(score);
            ScoreAwarded?.Invoke(gained, score, point, combo);
            if (levels.CurrentLevel.CompletionRule == LevelCompletionRule.TargetDamage &&
                damage >= levels.CurrentLevel.TargetDamage &&
                (ragdoll == null || ragdoll.CurrentHealth > 0f))
                CompleteLevel();
        }

        private void PublishRagdollDamageProgress()
        {
            float maximumHealth = ragdoll != null ? Mathf.Max(1f, ragdoll.MaximumHealth) : 1f;
            float currentHealth = ragdoll != null
                ? Mathf.Clamp(ragdoll.CurrentHealth, 0f, maximumHealth)
                : maximumHealth;
            ObjectiveProgressChanged?.Invoke(maximumHealth - currentHealth, maximumHealth);
        }

        private void HandleCharacterDied(Vector2 point)
        {
            if (State != GameplayState.Playing || deathCompletion != null) return;
            ragdollInput?.SetInputEnabled(false);
            sandboxToolInput?.SetInputEnabled(false);
            candyCannons?.CompleteInteraction();
            levelFourPipes?.SetInputEnabled(false);
            deathCompletion = StartCoroutine(CompleteAfterDeath());
        }

        private IEnumerator CompleteAfterDeath()
        {
            float elapsed = 0f;
            float delay = Mathf.Clamp(deathNextLevelDelay, 2f, 3f);
            while (elapsed < delay)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            deathCompletion = null;
            CompleteLevel();
            NextLevel();
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
            candyCannons?.SetInputEnabled(false);
            levelFourPipes?.SetInputEnabled(false);
            ragdoll = null;
            ragdollInput = null;
            sandboxToolInput = null;
            candyCannons = null;
            levelFourPipes = null;
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

        private void OnValidate() => deathNextLevelDelay = Mathf.Clamp(deathNextLevelDelay, 2f, 3f);
    }
}
