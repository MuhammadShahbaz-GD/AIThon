using System;
using KickTheBuddy.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace KickTheBuddy.VFX
{
    /// <summary>Pooled UI coins that fly from authoritative world damage points to the score label.</summary>
    [DisallowMultipleComponent]
    public sealed class CoinFlyVFXController : MonoBehaviour
    {
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform vfxLayer;
        [SerializeField] private RectTransform scoreTarget;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Image[] coinPool = Array.Empty<Image>();
        [Range(1, 3)] [SerializeField] private int maximumCoinsPerAward = 3;
        [Min(.1f)] [SerializeField] private float minimumDuration = .45f;
        [Min(.1f)] [SerializeField] private float maximumDuration = .65f;

        private GameplayManager gameplay;
        private Flight[] flights = Array.Empty<Flight>();
        private int cursor;
        private int activeFlights;

        private struct Flight
        {
            public bool Active;
            public Vector2 Start;
            public Vector2 Control;
            public Vector2 End;
            public float Elapsed;
            public float Duration;
            public float Delay;
        }

        private void Start()
        {
            if (coinPool == null) coinPool = Array.Empty<Image>();
            flights = new Flight[coinPool.Length];
            GameBootstrapper bootstrap = GameBootstrapper.Instance;
            gameplay = bootstrap != null ? bootstrap.Gameplay : null;
            if (gameplay != null) gameplay.ScoreAwarded += HandleScoreAwarded;
            ResetPool();
            enabled = false;
        }

        private void OnDestroy()
        {
            if (gameplay != null) gameplay.ScoreAwarded -= HandleScoreAwarded;
        }

        private void Update()
        {
            if (vfxLayer == null || activeFlights <= 0) { enabled = false; return; }
            for (int i = 0; i < flights.Length; i++)
            {
                Flight flight = flights[i];
                if (!flight.Active || coinPool[i] == null) continue;
                flight.Elapsed += Time.unscaledDeltaTime;
                if (flight.Elapsed < flight.Delay)
                {
                    flights[i] = flight;
                    continue;
                }

                float t = Mathf.Clamp01((flight.Elapsed - flight.Delay) / flight.Duration);
                float inverse = 1f - t;
                Vector2 point = inverse * inverse * flight.Start +
                                2f * inverse * t * flight.Control +
                                t * t * flight.End;
                RectTransform rect = coinPool[i].rectTransform;
                rect.anchoredPosition = point;
                rect.localEulerAngles = new Vector3(0f, 0f, t * 360f);
                float scale = t < .25f ? Mathf.Lerp(0f, 1.15f, t / .25f) : Mathf.Lerp(1.15f, .72f, (t - .25f) / .75f);
                rect.localScale = Vector3.one * scale;

                if (t >= 1f)
                {
                    flight.Active = false;
                    coinPool[i].gameObject.SetActive(false);
                    activeFlights = Mathf.Max(0, activeFlights - 1);
                }
                flights[i] = flight;
            }
            if (activeFlights == 0) enabled = false;
        }

        private void HandleScoreAwarded(int gained, int total, Vector2 worldPoint, int combo)
        {
            if (worldCamera == null || canvas == null || vfxLayer == null || scoreTarget == null) return;
            int count = Mathf.Clamp(1 + gained / 12, 1, maximumCoinsPerAward);
            Vector2 start = ScreenToLayer(worldCamera.WorldToScreenPoint(worldPoint));
            Vector2 end = WorldUIToLocal(scoreTarget.position);
            enabled = true;
            for (int i = 0; i < count; i++) StartFlight(start, end, i);
            GameBootstrapper.Instance?.Sounds?.Play(GameSound.Coin);
        }

        private void StartFlight(Vector2 start, Vector2 end, int sequence)
        {
            if (coinPool.Length == 0) return;
            int index = cursor++ % coinPool.Length;
            Image coin = coinPool[index];
            if (coin == null) return;

            if (!flights[index].Active) activeFlights++;

            float side = ((index * 37) % 101) / 100f;
            Vector2 control = Vector2.Lerp(start, end, .45f) +
                              new Vector2(Mathf.Lerp(-55f, 55f, side), 90f + sequence * 18f);
            flights[index] = new Flight
            {
                Active = true,
                Start = start,
                Control = control,
                End = end,
                Duration = Mathf.Lerp(minimumDuration, maximumDuration, side),
                Delay = sequence * .04f,
                Elapsed = 0f
            };
            coin.gameObject.SetActive(true);
            RectTransform rect = coin.rectTransform;
            rect.anchoredPosition = start;
            rect.localScale = Vector3.zero;
            rect.localRotation = Quaternion.identity;
        }

        private Vector2 ScreenToLayer(Vector2 screenPoint)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                vfxLayer, screenPoint, canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                out Vector2 local);
            return local;
        }

        private Vector2 WorldUIToLocal(Vector3 worldPosition)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, worldPosition);
            return ScreenToLayer(screen);
        }

        private void ResetPool()
        {
            activeFlights = 0;
            for (int i = 0; i < coinPool.Length; i++)
            {
                flights[i].Active = false;
                if (coinPool[i] != null) coinPool[i].gameObject.SetActive(false);
            }
        }

        private void OnValidate()
        {
            maximumDuration = Mathf.Max(minimumDuration, maximumDuration);
            maximumCoinsPerAward = Mathf.Clamp(maximumCoinsPerAward, 1, 3);
        }
    }
}
