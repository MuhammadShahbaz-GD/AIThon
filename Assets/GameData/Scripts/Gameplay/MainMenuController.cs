using KickTheBuddy.Haptics;
using UnityEngine;
using UnityEngine.UI;

namespace KickTheBuddy.Gameplay
{
    /// <summary>Main-menu presenter. It displays saved data and forwards commands to the flow service.</summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [Header("Saved Progress")]
        [SerializeField] private Text playButtonText;
        [SerializeField] private Text levelText;
        [SerializeField] private Text bestScoreText;
        [SerializeField] private Text totalScoreText;
        [SerializeField] private Text coinsText;

        [Header("Commands")]
        [SerializeField] private Button playButton;
        [SerializeField] private Button previousLevelButton;
        [SerializeField] private Button nextLevelButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button closeSettingsButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private GameObject settingsPanel;

        [Header("Settings")]
        [SerializeField] private Toggle musicToggle;
        [SerializeField] private Toggle soundToggle;
        [SerializeField] private Toggle vibrationToggle;

        private GameBootstrapper root;
        private GameSaveManager saves;
        private LevelsManager levels;
        private SoundManager sounds;
        private GameplayHapticsAdapter haptics;
        private bool updatingSettings;
        private float enabledMusicVolume = .8f;
        private float enabledSoundVolume = 1f;

        private void Start()
        {
            root = GameBootstrapper.Instance;
            if (root == null) { Debug.LogError("Main Menu requires GameBootstrapper.", this); enabled = false; return; }
            saves = root.Saves;
            levels = root.Levels;
            sounds = root.Sounds;
            haptics = root.GetComponent<GameplayHapticsAdapter>();
            AddListeners();
            Refresh();
            LoadSettings();
            if (settingsPanel != null) settingsPanel.SetActive(false);
        }

        public void Refresh()
        {
            LevelDefinition level = levels.CurrentLevel;
            if (playButtonText != null) playButtonText.text = saves.Data.hasStartedGame ? "CONTINUE" : "PLAY";
            if (levelText != null) levelText.text = level != null ? level.DisplayName : "NO LEVEL";
            LevelProgressData progress = level != null ? saves.GetLevel(level.LevelId) : default;
            if (bestScoreText != null) bestScoreText.text = $"BEST  {progress.bestScore:N0}";
            if (totalScoreText != null) totalScoreText.text = $"SCORE  {saves.Data.totalScore:N0}";
            if (coinsText != null) coinsText.text = $"COINS  {saves.Data.totalCoins:N0}";
            if (previousLevelButton != null) previousLevelButton.interactable = levels.CurrentLevelIndex > 0;
            if (nextLevelButton != null) nextLevelButton.interactable = levels.IsUnlocked(levels.CurrentLevelIndex + 1);
        }

        private void Play() { Feedback(); root.Flow.ContinueGame(); }
        private void SelectPrevious() => SelectLevel(levels.CurrentLevelIndex - 1);
        private void SelectNext() => SelectLevel(levels.CurrentLevelIndex + 1);
        private void SelectLevel(int index)
        {
            if (!levels.SelectLevel(index)) return;
            Feedback();
            Refresh();
        }

        private void OpenSettings() { Feedback(); LoadSettings(); if (settingsPanel != null) settingsPanel.SetActive(true); }
        private void CloseSettings() { Feedback(); if (settingsPanel != null) settingsPanel.SetActive(false); }

        private void LoadSettings()
        {
            if (saves.Data.musicVolume > .001f) enabledMusicVolume = saves.Data.musicVolume;
            if (saves.Data.soundVolume > .001f) enabledSoundVolume = saves.Data.soundVolume;
            updatingSettings = true;
            if (musicToggle != null) musicToggle.SetIsOnWithoutNotify(saves.Data.musicVolume > .001f);
            if (soundToggle != null) soundToggle.SetIsOnWithoutNotify(saves.Data.soundVolume > .001f);
            if (vibrationToggle != null) vibrationToggle.SetIsOnWithoutNotify(saves.Data.vibrationEnabled);
            updatingSettings = false;
        }

        private void SetMusic(bool value) { if (!updatingSettings) { saves.Data.musicVolume = value ? Mathf.Max(.05f, enabledMusicVolume) : 0f; ApplySettings(); } }
        private void SetSound(bool value) { if (!updatingSettings) { saves.Data.soundVolume = value ? Mathf.Max(.05f, enabledSoundVolume) : 0f; ApplySettings(); } }
        private void SetVibration(bool value)
        {
            if (updatingSettings) return;
            if (haptics != null) haptics.SetEnabled(value);
            else { saves.Data.vibrationEnabled = value; saves.Save(); }
        }
        private void ApplySettings() { sounds.ApplyVolumes(saves.Data.musicVolume, saves.Data.soundVolume); saves.Save(); }
        private void Feedback() { sounds.Play(GameSound.Button); haptics?.Selection(); }

        private void AddListeners()
        {
            playButton?.onClick.AddListener(Play);
            previousLevelButton?.onClick.AddListener(SelectPrevious);
            nextLevelButton?.onClick.AddListener(SelectNext);
            settingsButton?.onClick.AddListener(OpenSettings);
            closeSettingsButton?.onClick.AddListener(CloseSettings);
            quitButton?.onClick.AddListener(root.Flow.QuitGame);
            musicToggle?.onValueChanged.AddListener(SetMusic);
            soundToggle?.onValueChanged.AddListener(SetSound);
            vibrationToggle?.onValueChanged.AddListener(SetVibration);
        }

        private void OnDestroy()
        {
            playButton?.onClick.RemoveListener(Play);
            previousLevelButton?.onClick.RemoveListener(SelectPrevious);
            nextLevelButton?.onClick.RemoveListener(SelectNext);
            settingsButton?.onClick.RemoveListener(OpenSettings);
            closeSettingsButton?.onClick.RemoveListener(CloseSettings);
            if (root != null) quitButton?.onClick.RemoveListener(root.Flow.QuitGame);
            musicToggle?.onValueChanged.RemoveListener(SetMusic);
            soundToggle?.onValueChanged.RemoveListener(SetSound);
            vibrationToggle?.onValueChanged.RemoveListener(SetVibration);
        }
    }
}
