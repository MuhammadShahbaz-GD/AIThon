using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class LevelsManager : MonoBehaviour
    {
        [SerializeField] private LevelCatalog catalog;
        private GameSaveManager saves;
        public int CurrentLevelIndex { get; private set; }
        public LevelDefinition CurrentLevel => catalog != null ? catalog.Get(CurrentLevelIndex) : null;
        public event Action<int, LevelDefinition> LevelSelected; public event Action<float> LoadingProgress; public event Action<LevelDefinition> LevelLoaded;
        public void Initialize(GameSaveManager saveManager) { saves = saveManager; CurrentLevelIndex = Mathf.Clamp(saves.Data.selectedLevel, 0, Mathf.Max(0, Count - 1)); }
        public int Count => catalog != null ? catalog.Count : 0;
        public bool IsUnlocked(int index) => saves != null && index >= 0 && index <= saves.Data.highestUnlockedLevel && index < Count;
        public bool SelectLevel(int index) { if (!IsUnlocked(index)) return false; CurrentLevelIndex = index; saves.Data.selectedLevel = index; saves.Save(); LevelSelected?.Invoke(index, CurrentLevel); return true; }
        public void LoadCurrentLevel() { if (CurrentLevel != null) StartCoroutine(LoadRoutine(CurrentLevel)); }
        public bool SelectNextLevel() { int next = CurrentLevelIndex + 1; return next < Count && IsUnlocked(next) && SelectLevel(next); }
        private IEnumerator LoadRoutine(LevelDefinition definition)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(definition.ScenePath, LoadSceneMode.Single); if (operation == null) yield break; operation.allowSceneActivation = true;
            while (!operation.isDone) { LoadingProgress?.Invoke(Mathf.Clamp01(operation.progress / .9f)); yield return null; }
            LoadingProgress?.Invoke(1f); LevelLoaded?.Invoke(definition);
        }
    }
}
