using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Gameplay
{
    /// <summary>Owns application-level scene transitions. Gameplay rules remain in GameplayManager.</summary>
    [DisallowMultipleComponent]
    public sealed class GameFlowController : MonoBehaviour
    {
        [SerializeField] private string splashSceneName = "Splash";
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        private LevelsManager levels;
        private GameSaveManager saves;
        private SoundManager sounds;
        private GameplayManager gameplay;
        private bool initialized;
        private bool transitionInProgress;

        public bool IsTransitioning => transitionInProgress;
        public event Action<string> TransitionStarted;
        public event Action<string> TransitionCompleted;
        public event Action MainMenuShown;
        public event Action<LevelDefinition> GameplayStarted;

        public void Initialize(LevelsManager levelsManager, GameSaveManager saveManager, SoundManager soundManager, GameplayManager gameplayManager)
        {
            if (initialized) return;
            levels = levelsManager;
            saves = saveManager;
            sounds = soundManager;
            gameplay = gameplayManager;
            gameplay.MainMenuRequested += ShowMainMenu;
            initialized = true;
        }

        public void StartApplication(Scene activeScene, bool allowDirectGameplayStart)
        {
            if (!initialized) return;
            if (activeScene.name == splashSceneName || activeScene.name == mainMenuSceneName)
            {
                HandleSceneLoaded(activeScene);
                return;
            }

            if (allowDirectGameplayStart || IsCurrentGameplayScene(activeScene)) BeginGameplayScene();
            else ShowMainMenu();
        }

        public void HandleSceneLoaded(Scene scene)
        {
            if (!initialized) return;
            transitionInProgress = false;
            TransitionCompleted?.Invoke(scene.name);

            if (scene.name == splashSceneName)
            {
                gameplay.EnterMainMenu();
                sounds.PlayMusic(false);
            }
            else if (scene.name == mainMenuSceneName)
            {
                gameplay.EnterMainMenu();
                sounds.PlayMusic(false);
                MainMenuShown?.Invoke();
            }
            else if (IsCurrentGameplayScene(scene)) BeginGameplayScene();
        }

        public void ContinueGame()
        {
            int lastUnlocked = Mathf.Min(saves.Data.highestUnlockedLevel, Mathf.Max(0, levels.Count - 1));
            PlayLevel(Mathf.Clamp(saves.Data.selectedLevel, 0, lastUnlocked));
        }

        public void PlayLevel(int levelIndex)
        {
            if (!initialized || transitionInProgress || !levels.SelectLevel(levelIndex)) return;
            saves.RecordLevelSelection(levelIndex, true);
            gameplay.PrepareForLevelLoad();
            transitionInProgress = true;
            TransitionStarted?.Invoke(levels.CurrentLevel.ScenePath);
            levels.LoadCurrentLevel();
        }

        public void ShowMainMenu()
        {
            if (!initialized || transitionInProgress) return;
            Time.timeScale = 1f;
            gameplay.EnterMainMenu();
            if (SceneManager.GetActiveScene().name == mainMenuSceneName)
            {
                MainMenuShown?.Invoke();
                return;
            }

            transitionInProgress = true;
            TransitionStarted?.Invoke(mainMenuSceneName);
            SceneManager.LoadSceneAsync(mainMenuSceneName, LoadSceneMode.Single);
        }

        public void QuitGame()
        {
            saves?.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void Shutdown()
        {
            if (!initialized) return;
            gameplay.MainMenuRequested -= ShowMainMenu;
            initialized = false;
        }

        private bool IsCurrentGameplayScene(Scene scene)
        {
            LevelDefinition level = levels.CurrentLevel;
            return level != null && string.Equals(scene.path, level.ScenePath, StringComparison.OrdinalIgnoreCase);
        }

        private void BeginGameplayScene()
        {
            transitionInProgress = false;
            gameplay.BeginLevel();
            GameplayStarted?.Invoke(levels.CurrentLevel);
        }
    }
}
