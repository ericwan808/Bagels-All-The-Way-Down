using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Bagels.View
{
    /// <summary>
    /// The title scene: the game's name and a start button that loads the game scene. Builds its UI (and a camera,
    /// if the scene has none) when it wakes, so the scene file only needs this component.
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        /// <summary>Scene names, as listed in the build settings.</summary>
        public const string TitleScene = "title";
        public const string GameScene = "main";

        const float ReferenceHeight = 1080f;
        static readonly Color Background = new Color(0.98f, 0.95f, 0.9f); // the game scene's backdrop
        static readonly Color Brown = new Color(0.24f, 0.17f, 0.12f);
        static readonly Color ButtonColor = new Color(0.55f, 0.36f, 0.22f);

        [SerializeField] string title = "Bagels All The Way Down";

        void Awake()
        {
            if (Camera.main == null)
            {
                var camGo = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" };
                var cam = camGo.GetComponent<Camera>();
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Background;
                camGo.transform.position = new Vector3(0f, 0f, -10f);
            }
            SidePanel.EnsureEventSystem();
            Build();
        }

        void Build()
        {
            var go = new GameObject("Title Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, ReferenceHeight);
            scaler.matchWidthOrHeight = 1f;
            var root = (RectTransform)go.transform;

            var backdrop = UiFactory.Image("Background", root, null, Background, Vector2.zero);
            UiFactory.Fill(backdrop.rectTransform);

            var heading = UiFactory.Text("Title", root, title, 96, Brown, TextAnchor.MiddleCenter, FontStyle.Bold);
            heading.rectTransform.sizeDelta = new Vector2(1600f, 220f);
            heading.rectTransform.anchoredPosition = new Vector2(0f, 140f);

            var size = new Vector2(320f, 84f);
            var img = UiFactory.Image("Start Button", root, null, ButtonColor, size);
            img.raycastTarget = true;
            img.rectTransform.anchoredPosition = new Vector2(0f, -120f);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var label = UiFactory.Text("Label", img.rectTransform, "Start Game", 36, ViewColors.Paper,
                                       TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.Fill(label.rectTransform);
            button.onClick.AddListener(StartGame);
        }

        static void StartGame() => SceneManager.LoadScene(GameScene);

        /// <summary>Leaves the game for the title scene. The game scene is rebuilt from scratch the next time it loads.</summary>
        public static void QuitToTitle() => SceneManager.LoadScene(TitleScene);
    }
}
