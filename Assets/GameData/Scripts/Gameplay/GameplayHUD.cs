using KickTheBuddy.Haptics;
using UnityEngine;
using UnityEngine.UI;

namespace KickTheBuddy.Gameplay
{
    /// <summary>
    /// Presentation-only bridge between the gameplay services and the in-game HUD.
    /// Gameplay rules remain owned by <see cref="GameplayManager"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameplayHUD : MonoBehaviour
    {
        [Header("Labels")]
        [SerializeField] private Text levelText;
        [SerializeField] private Text objectiveText;
        [SerializeField] private Text scoreText;
        [SerializeField] private Text timerText;
        [SerializeField] private Text resultText;
        [SerializeField] private Text objectiveProgressText;

        [Header("Level Progress")]
        [Tooltip("Legacy slider support. New scenes use the cheaper filled Image below.")]
        [SerializeField] private Slider objectiveSlider;
        [SerializeField] private Image objectiveFillImage;

        [Header("Panels")]
        [SerializeField] private GameObject gameplayPanel;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private GameObject settingsPanel;

        [Header("Buttons")]
        [SerializeField] private Button pauseButton;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button closeSettingsButton;
        [SerializeField] private Button mainMenuButton;

        [Header("Settings")]
        [SerializeField] private Toggle musicToggle;
        [SerializeField] private Toggle soundToggle;
        [SerializeField] private Toggle vibrationToggle;

        private GameplayManager gameplay;
        private LevelsManager levels;
        private GameSaveManager saves;
        private SoundManager sounds;
        private GameplayHapticsAdapter haptics;
        private int displayedSeconds = -1;
        private bool settingsOpen;
        private bool updatingSettings;
        private float enabledMusicVolume = .8f;
        private float enabledSoundVolume = 1f;

        private void Start()
        {
            GameBootstrapper root = GameBootstrapper.Instance;
            if (root == null)
            {
                enabled = false;
                return;
            }

            gameplay = root.Gameplay;
            levels = root.Levels;
            saves = root.Saves;
            sounds = root.Sounds;
            haptics = root.GetComponent<GameplayHapticsAdapter>();

            gameplay.StateChanged += HandleState;
            gameplay.ObjectiveProgressChanged += HandleObjective;
            gameplay.ScoreChanged += HandleScore;
            gameplay.TimeChanged += HandleTime;
            gameplay.LevelCompleted += HandleComplete;
            gameplay.LevelFailed += HandleFailed;

            AddButtonListeners();
            AddSettingsListeners();
            LoadSettingsView();

            LevelDefinition level = levels.CurrentLevel;
            if (level != null)
            {
                Set(levelText, level.DisplayName);
                Set(objectiveText, level.ObjectiveText);
            }

            HandleState(GameplayState.Booting, gameplay.State);
        }

        public void OpenSettings()
        {
            if (gameplay == null || gameplay.State != GameplayState.Playing) return;
            settingsOpen = true;
            LoadSettingsView();
            sounds?.Play(GameSound.Button);
            haptics?.Selection();
            gameplay.Pause();
        }

        public void CloseSettings()
        {
            if (!settingsOpen) return;
            settingsOpen = false;
            if (settingsPanel != null) settingsPanel.SetActive(false);
            sounds?.Play(GameSound.Button);
            haptics?.Selection();
            gameplay?.Resume();
        }

        private void HandleState(GameplayState previous, GameplayState current)
        {
            if (current != GameplayState.Playing && current != GameplayState.Paused) settingsOpen = false;
            if (gameplayPanel != null) gameplayPanel.SetActive(current == GameplayState.Playing || (current == GameplayState.Paused && settingsOpen));
            if (pausePanel != null) pausePanel.SetActive(current == GameplayState.Paused && !settingsOpen);
            if (settingsPanel != null) settingsPanel.SetActive(current == GameplayState.Paused && settingsOpen);
            if (resultPanel != null) resultPanel.SetActive(current == GameplayState.LevelComplete || current == GameplayState.LevelFailed);
        }

        private void HandleObjective(float value, float target)
        {
            float normalized = target > 0f ? Mathf.Clamp01(value / target) : 0f;
            if (objectiveFillImage != null) objectiveFillImage.fillAmount = normalized;
            if (objectiveProgressText != null) objectiveProgressText.text = $"{Mathf.FloorToInt(value)} / {Mathf.CeilToInt(target)}";
            if (objectiveSlider != null)
            {
                objectiveSlider.maxValue = target;
                objectiveSlider.value = value;
            }
        }

        private void HandleScore(int value) => Set(scoreText, value.ToString("N0"));

        private void HandleTime(float value)
        {
            int seconds = Mathf.CeilToInt(value);
            if (seconds == displayedSeconds) return;
            displayedSeconds = seconds;
            Set(timerText, $"{seconds / 60:00}:{seconds % 60:00}");
        }

        private void HandleComplete(int score, int stars) => Set(resultText, $"LEVEL COMPLETE\nScore {score}\nStars {stars}/3");
        private void HandleFailed() => Set(resultText, "TRY AGAIN");

        private void LoadSettingsView()
        {
            if (saves == null || saves.Data == null) return;
            if (saves.Data.musicVolume > .001f) enabledMusicVolume = saves.Data.musicVolume;
            if (saves.Data.soundVolume > .001f) enabledSoundVolume = saves.Data.soundVolume;

            updatingSettings = true;
            if (musicToggle != null) musicToggle.SetIsOnWithoutNotify(saves.Data.musicVolume > .001f);
            if (soundToggle != null) soundToggle.SetIsOnWithoutNotify(saves.Data.soundVolume > .001f);
            if (vibrationToggle != null) vibrationToggle.SetIsOnWithoutNotify(saves.Data.vibrationEnabled);
            updatingSettings = false;
        }

        private void SetMusicEnabled(bool value)
        {
            if (updatingSettings || saves == null) return;
            saves.Data.musicVolume = value ? Mathf.Max(.05f, enabledMusicVolume) : 0f;
            ApplyAndSaveSettings();
        }

        private void SetSoundEnabled(bool value)
        {
            if (updatingSettings || saves == null) return;
            saves.Data.soundVolume = value ? Mathf.Max(.05f, enabledSoundVolume) : 0f;
            ApplyAndSaveSettings();
            if (value) sounds?.Play(GameSound.Button);
        }

        private void SetVibrationEnabled(bool value)
        {
            if (updatingSettings || saves == null) return;
            if (haptics != null) haptics.SetEnabled(value);
            else
            {
                saves.Data.vibrationEnabled = value;
                saves.Save();
            }
        }

        private void ApplyAndSaveSettings()
        {
            sounds?.ApplyVolumes(saves.Data.musicVolume, saves.Data.soundVolume);
            saves.Save();
        }

        private void AddButtonListeners()
        {
            if (pauseButton != null) pauseButton.onClick.AddListener(gameplay.Pause);
            if (resumeButton != null) resumeButton.onClick.AddListener(gameplay.Resume);
            if (restartButton != null) restartButton.onClick.AddListener(gameplay.Restart);
            if (nextButton != null) nextButton.onClick.AddListener(gameplay.NextLevel);
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
            if (closeSettingsButton != null) closeSettingsButton.onClick.AddListener(CloseSettings);
            if (mainMenuButton != null) mainMenuButton.onClick.AddListener(ReturnToMainMenu);
        }

        private void ReturnToMainMenu()
        {
            sounds?.Play(GameSound.Button);
            haptics?.Selection();
            GameBootstrapper.Instance?.Flow?.ShowMainMenu();
        }

        private void AddSettingsListeners()
        {
            if (musicToggle != null) musicToggle.onValueChanged.AddListener(SetMusicEnabled);
            if (soundToggle != null) soundToggle.onValueChanged.AddListener(SetSoundEnabled);
            if (vibrationToggle != null) vibrationToggle.onValueChanged.AddListener(SetVibrationEnabled);
        }

        private static void Set(Text label, string value)
        {
            if (label != null) label.text = value;
        }

        private void OnDestroy()
        {
            if (gameplay != null)
            {
                gameplay.StateChanged -= HandleState;
                gameplay.ObjectiveProgressChanged -= HandleObjective;
                gameplay.ScoreChanged -= HandleScore;
                gameplay.TimeChanged -= HandleTime;
                gameplay.LevelCompleted -= HandleComplete;
                gameplay.LevelFailed -= HandleFailed;
            }

            if (musicToggle != null) musicToggle.onValueChanged.RemoveListener(SetMusicEnabled);
            if (soundToggle != null) soundToggle.onValueChanged.RemoveListener(SetSoundEnabled);
            if (vibrationToggle != null) vibrationToggle.onValueChanged.RemoveListener(SetVibrationEnabled);
            if (settingsButton != null) settingsButton.onClick.RemoveListener(OpenSettings);
            if (closeSettingsButton != null) closeSettingsButton.onClick.RemoveListener(CloseSettings);
            if (mainMenuButton != null) mainMenuButton.onClick.RemoveListener(ReturnToMainMenu);
        }
    }
}
