using System;
using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    public enum GameplayState { Booting, MainMenu, Loading, Playing, Paused, LevelComplete, LevelFailed }

    [Serializable]
    public sealed class PlayerProgressData
    {
        public int version = 1;
        public int highestUnlockedLevel;
        public int selectedLevel;
        public int totalCoins;
        public int totalScore;
        public float musicVolume = .8f;
        public float soundVolume = 1f;
        public bool vibrationEnabled = true;
        public LevelProgressData[] levels = Array.Empty<LevelProgressData>();
    }

    [Serializable]
    public struct LevelProgressData
    {
        public string levelId;
        public bool completed;
        public int bestScore;
        public int stars;
    }
}
