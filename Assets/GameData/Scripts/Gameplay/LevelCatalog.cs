using System;
using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    [CreateAssetMenu(menuName = "Kick The Buddy/Gameplay/Level Catalog", fileName = "Level Catalog")]
    public sealed class LevelCatalog : ScriptableObject
    {
        [SerializeField] private LevelDefinition[] levels = Array.Empty<LevelDefinition>();
        public int Count => levels.Length;
        public LevelDefinition Get(int index) => index >= 0 && index < levels.Length ? levels[index] : null;
        public int IndexOf(string id) { for (int i = 0; i < levels.Length; i++) if (levels[i] != null && levels[i].LevelId == id) return i; return -1; }
    }
}
