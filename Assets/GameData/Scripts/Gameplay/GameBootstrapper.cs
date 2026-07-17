using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Gameplay
{
    /// <summary>Persistent composition root for save, audio, levels, and the active gameplay session.</summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class GameBootstrapper : MonoBehaviour
    {
        [Tooltip("Keeps direct gameplay-scene Play Mode convenient. Builds still start from the Splash scene.")]
        [SerializeField] private bool startGameplayImmediately = true;
        private GameSaveManager saves; private SoundManager sounds; private LevelsManager levels; private GameplayManager gameplay; private GameFlowController flow;
        public static GameBootstrapper Instance { get; private set; }
        public GameSaveManager Saves => saves; public SoundManager Sounds => sounds; public LevelsManager Levels => levels; public GameplayManager Gameplay => gameplay; public GameFlowController Flow => flow;
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; DontDestroyOnLoad(gameObject);
            saves = GetComponent<GameSaveManager>(); sounds = GetComponent<SoundManager>(); levels = GetComponent<LevelsManager>(); gameplay = GetComponent<GameplayManager>(); flow = GetComponent<GameFlowController>();
            if (saves == null || sounds == null || levels == null || gameplay == null || flow == null) { Debug.LogError("Game Systems is incomplete. Run Tools/Game/Build Full Game Flow.", this); enabled = false; return; }
            saves.Load(); sounds.ApplyVolumes(saves.Data.musicVolume, saves.Data.soundVolume); levels.Initialize(saves); gameplay.Initialize(levels, saves, sounds); flow.Initialize(levels, saves, sounds, gameplay); saves.RecordAppLaunch(); SceneManager.sceneLoaded += HandleSceneLoaded;
        }
        private void Start() => flow.StartApplication(SceneManager.GetActiveScene(), startGameplayImmediately);
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) { if (scene.buildIndex >= 0) flow.HandleSceneLoaded(scene); }
        private void OnDestroy() { if (Instance != this) return; SceneManager.sceneLoaded -= HandleSceneLoaded; flow?.Shutdown(); Instance = null; }
    }
}
