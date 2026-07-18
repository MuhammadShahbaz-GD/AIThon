using System;
using System.Collections.Generic;
using KickTheBuddy.Physics;
using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    /// <summary>
    /// Selects the authored content for the saved level inside the single gameplay scene.
    /// Every reference is assigned in the Inspector; no hierarchy scans or runtime component creation are used.
    /// </summary>
    [DefaultExecutionOrder(-400)]
    [DisallowMultipleComponent]
    public sealed class GameplayLevelSceneController : MonoBehaviour
    {
        [Serializable]
        private sealed class LevelContent
        {
            [SerializeField] private string levelId;
            [SerializeField] private GameObject root;
            [SerializeField] private RagdollController ragdoll;
            [SerializeField] private RagdollInputManager ragdollInput;
            [SerializeField] private SandboxToolInput2D sandboxToolInput;
            [SerializeField] private CandyCannonController2D candyCannons;
            [SerializeField] private LevelFourPipeController2D levelFourPipes;

            public string LevelId => levelId;
            public GameObject Root => root;
            public RagdollController Ragdoll => ragdoll;
            public RagdollInputManager RagdollInput => ragdollInput;
            public SandboxToolInput2D SandboxToolInput => sandboxToolInput;
            public CandyCannonController2D CandyCannons => candyCannons;
            public LevelFourPipeController2D LevelFourPipes => levelFourPipes;
        }

        [Tooltip("Entries must match the Level Catalog order. Their roots must be direct children of this Levels object.")]
        [SerializeField] private LevelContent[] levels = Array.Empty<LevelContent>();

        [Header("Shared Room")]
        [Tooltip("One persistent Room shared by every level. Only its damage profile changes per LevelDefinition.")]
        [SerializeField] private GameObject sharedRoom;
        [Tooltip("Explicit wall attack references under the shared Room; populated by the single-scene authoring tool.")]
        [SerializeField] private RagdollAttackManager2D[] sharedRoomAttacks =
            Array.Empty<RagdollAttackManager2D>();
        [Tooltip("Shared floor collider. It remains physical but never deals ragdoll damage.")]
        [SerializeField] private RagdollAttackManager2D sharedFloorAttack;

        private int activeLevelIndex = -1;
        private LevelContent activeContent;

        public static GameplayLevelSceneController Active { get; private set; }
        public int ActiveLevelIndex => activeLevelIndex;
        public string ActiveLevelId => activeContent != null ? activeContent.LevelId : string.Empty;
        public GameObject ActiveLevelRoot => activeContent != null ? activeContent.Root : null;
        public RagdollController ActiveRagdoll => activeContent != null ? activeContent.Ragdoll : null;
        public RagdollInputManager ActiveRagdollInput => activeContent != null ? activeContent.RagdollInput : null;
        public SandboxToolInput2D ActiveSandboxToolInput =>
            activeContent != null ? activeContent.SandboxToolInput : null;
        public CandyCannonController2D ActiveCandyCannons =>
            activeContent != null ? activeContent.CandyCannons : null;
        public LevelFourPipeController2D ActiveLevelFourPipes =>
            activeContent != null ? activeContent.LevelFourPipes : null;
        public GameObject SharedRoom => sharedRoom;
        public IReadOnlyList<RagdollAttackManager2D> SharedRoomAttacks => sharedRoomAttacks;
        public int LevelCount => levels.Length;

        public event Action<int, string, GameObject> LevelContentActivated;
        public event Action<LevelDefinition> SharedRoomConfigured;

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                Debug.LogError("Only one GameplayLevelSceneController may exist in the gameplay scene.", this);
                enabled = false;
                return;
            }

            Active = this;
            ActivateSavedLevel();
        }

        /// <summary>Reads the in-memory save selection owned by the persistent game bootstrapper.</summary>
        public bool ActivateSavedLevel()
        {
            GameBootstrapper bootstrapper = GameBootstrapper.Instance;
            LevelDefinition definition = bootstrapper != null ? bootstrapper.Levels.CurrentLevel : null;
            int catalogIndex = bootstrapper != null ? bootstrapper.Levels.CurrentLevelIndex : 0;
            string levelId = definition != null ? definition.LevelId : string.Empty;
            return ActivateLevel(catalogIndex, levelId, definition);
        }

        /// <summary>Activates exactly one authored root using the matching saved LevelDefinition.</summary>
        private bool ActivateLevel(int catalogIndex, string levelId, LevelDefinition definition)
        {
            int selected = FindLevel(levelId);
            if (selected < 0 && catalogIndex >= 0 && catalogIndex < levels.Length)
                selected = catalogIndex;
            if (selected < 0)
            {
                Debug.LogError($"No scene content is authored for level '{levelId}' at index {catalogIndex}.", this);
                DisableAllLevels();
                return false;
            }

            LevelContent selectedContent = levels[selected];
            if (selectedContent == null || selectedContent.Root == null || selectedContent.Ragdoll == null ||
                selectedContent.RagdollInput == null)
            {
                Debug.LogError($"Level '{levelId}' has incomplete authored gameplay references.", this);
                DisableAllLevels();
                return false;
            }

            // GameplayManager enables the selected inputs only after it has reset and subscribed to the level.
            selectedContent.RagdollInput.SetInputEnabled(false);
            selectedContent.SandboxToolInput?.SetInputEnabled(false);
            selectedContent.CandyCannons?.SetInputEnabled(false);
            selectedContent.LevelFourPipes?.SetInputEnabled(false);
            if (!ConfigureSharedRoom(definition))
            {
                DisableAllLevels();
                return false;
            }

            for (int i = 0; i < levels.Length; i++)
            {
                LevelContent content = levels[i];
                if (content != null && content.Root != null)
                    content.Root.SetActive(i == selected);
            }

            selectedContent.Ragdoll.ConfigureLevelDurability(
                definition.TargetDurabilityMultiplier,
                definition.MinimumHitsToDepletePart);

            activeLevelIndex = selected;
            activeContent = selectedContent;
            SharedRoomConfigured?.Invoke(definition);
            LevelContentActivated?.Invoke(selected, activeContent.LevelId, activeContent.Root);
            return true;
        }

        private bool ConfigureSharedRoom(LevelDefinition definition)
        {
            if (definition == null || sharedRoom == null || sharedRoomAttacks == null ||
                sharedRoomAttacks.Length == 0 || sharedFloorAttack == null)
            {
                Debug.LogError("The selected LevelDefinition, shared Room, or non-damaging floor reference is incomplete.", this);
                return false;
            }

            for (int i = 0; i < sharedRoomAttacks.Length; i++)
            {
                if (sharedRoomAttacks[i] != null) continue;
                Debug.LogError("The shared Room contains an unassigned wall attack reference.", this);
                return false;
            }

            sharedRoom.SetActive(true);
            for (int i = 0; i < sharedRoomAttacks.Length; i++)
            {
                if (sharedRoomAttacks[i] == sharedFloorAttack)
                {
                    sharedRoomAttacks[i].Configure(RagdollAttackType.Wall, 0f, 0f, 0f, 0f);
                    sharedRoomAttacks[i].SetDamageEnabled(false);
                    continue;
                }
                sharedRoomAttacks[i].Configure(
                    RagdollAttackType.Wall,
                    definition.WallBaseDamage,
                    definition.WallDamagePerSpeed,
                    definition.WallMinimumImpactSpeed,
                    definition.WallMaximumDamage);
            }
            return true;
        }

        public bool HasAuthoredLevel(string levelId)
        {
            return FindLevel(levelId) >= 0;
        }

        private int FindLevel(string levelId)
        {
            if (string.IsNullOrWhiteSpace(levelId)) return -1;
            for (int i = 0; i < levels.Length; i++)
            {
                LevelContent content = levels[i];
                if (content != null && string.Equals(content.LevelId, levelId, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        private void DisableAllLevels()
        {
            for (int i = 0; i < levels.Length; i++)
            {
                LevelContent content = levels[i];
                if (content == null) continue;
                content.RagdollInput?.SetInputEnabled(false);
                content.SandboxToolInput?.SetInputEnabled(false);
                content.CandyCannons?.SetInputEnabled(false);
                content.LevelFourPipes?.SetInputEnabled(false);
                if (content.Root != null) content.Root.SetActive(false);
            }
            if (sharedRoom != null) sharedRoom.SetActive(false);
            activeLevelIndex = -1;
            activeContent = null;
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
        }
    }
}
