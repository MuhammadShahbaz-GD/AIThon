using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Gameplay
{
    /// <summary>Persistent composition root for save, audio, levels, and the active gameplay session.</summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class GameBootstrapper : MonoBehaviour
    {
        [SerializeField] private bool startGameplayImmediately = true;
        private GameSaveManager saves; private SoundManager sounds; private LevelsManager levels; private GameplayManager gameplay;
        public static GameBootstrapper Instance { get; private set; }
        public GameSaveManager Saves => saves; public SoundManager Sounds => sounds; public LevelsManager Levels => levels; public GameplayManager Gameplay => gameplay;
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; DontDestroyOnLoad(gameObject);
            saves = GetComponent<GameSaveManager>(); sounds = GetComponent<SoundManager>(); levels = GetComponent<LevelsManager>(); gameplay = GetComponent<GameplayManager>();
            saves.Load(); sounds.ApplyVolumes(saves.Data.musicVolume, saves.Data.soundVolume); levels.Initialize(saves); gameplay.Initialize(levels, saves, sounds); SceneManager.sceneLoaded += HandleSceneLoaded;
        }
        private void Start() { if (startGameplayImmediately) gameplay.BeginLevel(); else sounds.PlayMusic(false); }
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) { if (scene.buildIndex < 0) return; gameplay.BindRagdoll(); if (gameplay.State == GameplayState.Loading || gameplay.State == GameplayState.LevelComplete) gameplay.BeginLevel(); }
        private void OnDestroy() { if (Instance != this) return; SceneManager.sceneLoaded -= HandleSceneLoaded; Instance = null; }
    }
}
