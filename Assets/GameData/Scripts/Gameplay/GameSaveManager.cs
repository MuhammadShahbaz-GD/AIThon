using System;
using System.IO;
using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    /// <summary>Versioned JSON persistence with atomic replacement and corruption recovery.</summary>
    [DisallowMultipleComponent]
    public sealed class GameSaveManager : MonoBehaviour
    {
        private const int CurrentVersion = 2;
        [SerializeField] private string fileName = "player-progress.json";
        private string SavePath => Path.Combine(Application.persistentDataPath, fileName);
        public PlayerProgressData Data { get; private set; }
        public event Action<PlayerProgressData> Loaded; public event Action<PlayerProgressData> Saved; public event Action ResetPerformed;

        public void Load()
        {
            try { Data = File.Exists(SavePath) ? JsonUtility.FromJson<PlayerProgressData>(File.ReadAllText(SavePath)) : null; }
            catch (Exception exception) { Debug.LogWarning("Save data was invalid and has been reset: " + exception.Message, this); Data = null; }
            if (Data == null) Data = new PlayerProgressData(); Normalize(); Loaded?.Invoke(Data);
        }
        public void Save()
        {
            if (Data == null) Data = new PlayerProgressData(); string temporary = SavePath + ".tmp";
            try { File.WriteAllText(temporary, JsonUtility.ToJson(Data, true)); if (File.Exists(SavePath)) File.Delete(SavePath); File.Move(temporary, SavePath); Saved?.Invoke(Data); }
            catch (Exception exception) { Debug.LogError("Unable to save player progress: " + exception.Message, this); }
        }
        public void ResetData() { Data = new PlayerProgressData(); Save(); ResetPerformed?.Invoke(); }
        public void RecordAppLaunch()
        {
            if (Data == null) Load();
            Data.launchCount = Mathf.Max(0, Data.launchCount) + 1;
            Save();
        }
        public void RecordLevelSelection(int levelIndex, bool markStarted)
        {
            if (Data == null) Load();
            Data.selectedLevel = Mathf.Max(0, levelIndex);
            Data.hasStartedGame |= markStarted;
            if (markStarted) Data.lastPlayedUtcTicks = DateTime.UtcNow.Ticks;
            Save();
        }
        public LevelProgressData GetLevel(string id) { EnsureLevel(id); for (int i = 0; i < Data.levels.Length; i++) if (Data.levels[i].levelId == id) return Data.levels[i]; return default; }
        public void RecordLevel(string id, int score, int stars, int coins, int levelIndex)
        {
            EnsureLevel(id); for (int i = 0; i < Data.levels.Length; i++) if (Data.levels[i].levelId == id) { LevelProgressData entry = Data.levels[i]; bool first = !entry.completed; entry.completed = true; entry.bestScore = Mathf.Max(entry.bestScore, score); entry.stars = Mathf.Max(entry.stars, stars); Data.levels[i] = entry; if (first) Data.totalCoins += coins; break; }
            Data.totalScore += score; Data.highestUnlockedLevel = Mathf.Max(Data.highestUnlockedLevel, levelIndex + 1); Save();
        }
        private void EnsureLevel(string id) { if (Data == null) Load(); for (int i = 0; i < Data.levels.Length; i++) if (Data.levels[i].levelId == id) return; Array.Resize(ref Data.levels, Data.levels.Length + 1); Data.levels[Data.levels.Length - 1] = new LevelProgressData { levelId = id }; }
        private void Normalize()
        {
            Data.highestUnlockedLevel = Mathf.Max(0, Data.highestUnlockedLevel);
            Data.selectedLevel = Mathf.Max(0, Data.selectedLevel);
            Data.launchCount = Mathf.Max(0, Data.launchCount);
            Data.musicVolume = Mathf.Clamp01(Data.musicVolume);
            Data.soundVolume = Mathf.Clamp01(Data.soundVolume);
            if (Data.levels == null) Data.levels = Array.Empty<LevelProgressData>();
            if (Data.version < CurrentVersion)
            {
                Data.hasStartedGame |= Data.selectedLevel > 0 || Data.totalScore > 0 || Data.levels.Length > 0;
                Data.version = CurrentVersion;
            }
        }
        private void OnApplicationPause(bool paused) { if (paused) Save(); }
        private void OnApplicationQuit() { Save(); }
    }
}
