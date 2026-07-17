using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    public enum LevelCompletionRule
    {
        TargetDamage,
        CharacterDestroyed
    }

    [CreateAssetMenu(menuName = "Kick The Buddy/Gameplay/Level Definition", fileName = "Level_01")]
    public sealed class LevelDefinition : ScriptableObject
    {
        public const float MinimumPlayTimeSeconds = 45f;

        [SerializeField] private string levelId = "level_01";
        [SerializeField] private string displayName = "Training Room";
        [SerializeField] private string scenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        [TextArea] [SerializeField] private string objectiveText = "Deal enough damage before time expires.";
        [Tooltip("Target Damage completes immediately; Character Destroyed waits for the ragdoll death event.")]
        [SerializeField] private LevelCompletionRule completionRule = LevelCompletionRule.TargetDamage;
        [Min(1f)] [SerializeField] private float targetDamage = 250f;
        [Tooltip("Maximum level play time. All levels are clamped to at least 45 seconds.")]
        [Min(MinimumPlayTimeSeconds)] [SerializeField] private float timeLimit = MinimumPlayTimeSeconds;
        [Min(0)] [SerializeField] private int completionCoins = 100;
        [Min(0)] [SerializeField] private int oneStarScore = 250, twoStarScore = 450, threeStarScore = 700;

        [Header("Shared Room Damage")]
        [Tooltip("Fixed wall damage once the minimum impact speed is reached.")]
        [Min(0f)] [SerializeField] private float wallBaseDamage;
        [Tooltip("Additional wall damage for each speed unit above the threshold.")]
        [Min(0f)] [SerializeField] private float wallDamagePerSpeed = 1.25f;
        [Min(0f)] [SerializeField] private float wallMinimumImpactSpeed = 4f;
        [Min(0f)] [SerializeField] private float wallMaximumDamage = 16f;

        public string LevelId => levelId; public string DisplayName => displayName; public string ScenePath => scenePath;
        public string ObjectiveText => objectiveText; public LevelCompletionRule CompletionRule => completionRule;
        public float TargetDamage => targetDamage; public float TimeLimit => Mathf.Max(MinimumPlayTimeSeconds, timeLimit);
        public int CompletionCoins => completionCoins;
        public float WallBaseDamage => wallBaseDamage;
        public float WallDamagePerSpeed => wallDamagePerSpeed;
        public float WallMinimumImpactSpeed => wallMinimumImpactSpeed;
        public float WallMaximumDamage => Mathf.Max(wallBaseDamage, wallMaximumDamage);
        public int GetStars(int score) { if (score >= threeStarScore) return 3; if (score >= twoStarScore) return 2; if (score >= oneStarScore) return 1; return 0; }

        private void OnValidate()
        {
            timeLimit = Mathf.Max(MinimumPlayTimeSeconds, timeLimit);
            wallBaseDamage = Mathf.Max(0f, wallBaseDamage);
            wallDamagePerSpeed = Mathf.Max(0f, wallDamagePerSpeed);
            wallMinimumImpactSpeed = Mathf.Max(0f, wallMinimumImpactSpeed);
            wallMaximumDamage = Mathf.Max(wallBaseDamage, wallMaximumDamage);
        }
    }
}
