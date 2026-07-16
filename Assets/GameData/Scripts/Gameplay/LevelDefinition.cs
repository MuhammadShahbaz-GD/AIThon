using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    [CreateAssetMenu(menuName = "Kick The Buddy/Gameplay/Level Definition", fileName = "Level_01")]
    public sealed class LevelDefinition : ScriptableObject
    {
        [SerializeField] private string levelId = "level_01";
        [SerializeField] private string displayName = "Training Room";
        [SerializeField] private string scenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        [TextArea] [SerializeField] private string objectiveText = "Deal enough damage before time expires.";
        [Min(1f)] [SerializeField] private float targetDamage = 250f;
        [Min(5f)] [SerializeField] private float timeLimit = 60f;
        [Min(0)] [SerializeField] private int completionCoins = 100;
        [Min(0)] [SerializeField] private int oneStarScore = 250, twoStarScore = 450, threeStarScore = 700;
        public string LevelId => levelId; public string DisplayName => displayName; public string ScenePath => scenePath;
        public string ObjectiveText => objectiveText; public float TargetDamage => targetDamage; public float TimeLimit => timeLimit;
        public int CompletionCoins => completionCoins;
        public int GetStars(int score) { if (score >= threeStarScore) return 3; if (score >= twoStarScore) return 2; if (score >= oneStarScore) return 1; return 0; }
    }
}
