using System;
using UnityEngine;
using UnityEngine.UI;

namespace KickTheBuddy.Gameplay
{
    /// <summary>Thin UI presenter; all game rules remain in GameplayManager.</summary>
    [DisallowMultipleComponent]
    public sealed class GameplayHUD : MonoBehaviour
    {
        [SerializeField] private Text levelText, objectiveText, scoreText, timerText, resultText;
        [SerializeField] private Slider objectiveSlider;
        [SerializeField] private GameObject gameplayPanel, resultPanel, pausePanel;
        [SerializeField] private Button pauseButton, resumeButton, restartButton, nextButton;
        private GameplayManager gameplay; private LevelsManager levels;
        private void Start()
        {
            GameBootstrapper root = GameBootstrapper.Instance; if (root == null) { enabled = false; return; } gameplay = root.Gameplay; levels = root.Levels;
            gameplay.StateChanged += HandleState; gameplay.ObjectiveProgressChanged += HandleObjective; gameplay.ScoreChanged += HandleScore; gameplay.TimeChanged += HandleTime; gameplay.LevelCompleted += HandleComplete; gameplay.LevelFailed += HandleFailed;
            if (pauseButton != null) pauseButton.onClick.AddListener(gameplay.Pause); if (resumeButton != null) resumeButton.onClick.AddListener(gameplay.Resume); if (restartButton != null) restartButton.onClick.AddListener(gameplay.Restart); if (nextButton != null) nextButton.onClick.AddListener(gameplay.NextLevel);
            LevelDefinition level = levels.CurrentLevel; if (level != null) { Set(levelText, level.DisplayName); Set(objectiveText, level.ObjectiveText); }
            HandleState(GameplayState.Booting, gameplay.State);
        }
        private void HandleState(GameplayState previous, GameplayState current) { if (gameplayPanel != null) gameplayPanel.SetActive(current == GameplayState.Playing); if (pausePanel != null) pausePanel.SetActive(current == GameplayState.Paused); if (resultPanel != null) resultPanel.SetActive(current == GameplayState.LevelComplete || current == GameplayState.LevelFailed); }
        private void HandleObjective(float value, float target) { if (objectiveSlider != null) { objectiveSlider.maxValue = target; objectiveSlider.value = value; } }
        private void HandleScore(int value) { Set(scoreText, "Score: " + value); }
        private void HandleTime(float value) { int seconds = Mathf.CeilToInt(value); Set(timerText, $"{seconds / 60:00}:{seconds % 60:00}"); }
        private void HandleComplete(int score, int stars) { Set(resultText, $"LEVEL COMPLETE\nScore {score}\nStars {stars}/3"); }
        private void HandleFailed() { Set(resultText, "TRY AGAIN"); }
        private static void Set(Text label, string value) { if (label != null) label.text = value; }
        private void OnDestroy()
        {
            if (gameplay == null) return; gameplay.StateChanged -= HandleState; gameplay.ObjectiveProgressChanged -= HandleObjective; gameplay.ScoreChanged -= HandleScore; gameplay.TimeChanged -= HandleTime; gameplay.LevelCompleted -= HandleComplete; gameplay.LevelFailed -= HandleFailed;
        }
    }
}
