using UnityEngine;

namespace Bagels.View
{
    /// <summary>
    /// Loops the main theme for the whole session, fading it in from silence when play begins. The track is loaded
    /// from <c>Assets/Resources/Audio</c>, so the scene needs no wiring: <see cref="GameView"/> adds this component
    /// at startup if the scene doesn't already have one. Starting a new game doesn't restart the music.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class BackgroundMusic : MonoBehaviour
    {
        /// <summary>The Resources path of the main theme.</summary>
        public const string ThemeResource = "Audio/BATWD_01_MainTheme";

        [Tooltip("Track to loop. If empty, the main theme is loaded from Resources.")]
        [SerializeField] AudioClip clip;
        [Tooltip("Volume once the fade-in has finished.")]
        [Range(0f, 1f)]
        [SerializeField] float volume = 0.6f;
        [Tooltip("Seconds the music takes to fade in from silence when play begins.")]
        [SerializeField] float fadeInDuration = 3f;

        AudioSource source;
        float fadeStart;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f; // 2D: the same everywhere on screen
        }

        void Start()
        {
            if (clip == null) clip = Resources.Load<AudioClip>(ThemeResource);
            if (clip == null)
            {
                Debug.LogWarning($"Bagels: no Resources/{ThemeResource}; the game will have no music.", this);
                enabled = false;
                return;
            }
            source.clip = clip;
            source.volume = 0f;
            source.Play();
            fadeStart = Time.unscaledTime;
        }

        void Update()
        {
            // Unscaled time, so the fade isn't affected if the game is ever paused with timeScale.
            float t = fadeInDuration > 0f ? Mathf.Clamp01((Time.unscaledTime - fadeStart) / fadeInDuration) : 1f;
            // Ease in: loudness is perceived roughly logarithmically, so a squared curve sounds more even than linear.
            source.volume = volume * t * t;
            if (t >= 1f) enabled = false; // fade finished; stop updating until the volume is changed
        }

        /// <summary>The volume after fading in. Setting it takes effect immediately.</summary>
        public float Volume
        {
            get => volume;
            set
            {
                volume = Mathf.Clamp01(value);
                if (source != null && !enabled) source.volume = volume;
            }
        }
    }
}
