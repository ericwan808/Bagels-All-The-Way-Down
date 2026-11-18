using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Bagels.View
{
    /// <summary>
    /// A small window in the lower right of the board, just left of the orders panel, that loops a video clip from
    /// Resources until the player closes it with its X button (design.md, "Tutorial"). The video plays muted, so it
    /// doesn't talk over the music. It has its own canvas, so it stays up while the tutorial overlay is hidden, and
    /// shows over the help popup. Only one window shows at a time.
    /// </summary>
    public sealed class TutorialVideo : MonoBehaviour
    {
        const float ReferenceHeight = 1080f;
        const float VideoWidth = 480f;
        const float Margin = 20f;
        const float Border = 6f;
        const float HeaderHeight = 34f;
        const float CloseSize = 28f;
        const int SortingOrder = 210; // above the tutorial overlay, so its X can be clicked while a page is up

        static readonly Color BoxColor = new Color(0.24f, 0.17f, 0.12f, 0.96f);
        static readonly Color Gold = new Color(0.98f, 0.8f, 0.25f);
        static readonly Color CloseColor = new Color(0.55f, 0.2f, 0.17f);

        VideoPlayer player;
        RenderTexture texture;

        /// <summary>The window showing, if any: opening another replaces it.</summary>
        static TutorialVideo current;

        /// <summary>Closes the window showing, if any.</summary>
        public static void CloseAny()
        {
            if (current != null) current.Close();
            current = null;
        }

        /// <summary>Opens a window playing <c>Resources/&lt;resource&gt;</c>, or returns null if there is no such clip.</summary>
        public static TutorialVideo Open(Transform parent, string resource, string title)
        {
            var clip = Resources.Load<VideoClip>(resource);
            if (clip == null)
            {
                Debug.LogWarning($"Bagels: no video clip at Resources/{resource}; the tutorial video is skipped.");
                return null;
            }
            CloseAny();
            var go = new GameObject("Tutorial Video", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var video = go.AddComponent<TutorialVideo>();
            video.Build(clip, title);
            current = video;
            return video;
        }

        public void Close() => Destroy(gameObject);

        void Build(VideoClip clip, string title)
        {
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, ReferenceHeight);
            scaler.matchWidthOrHeight = 1f; // same as the side panels, so the orders panel's width is exact

            float aspect = clip.width > 0 ? (float)clip.height / clip.width : 9f / 16f;
            float videoHeight = VideoWidth * aspect;
            var size = new Vector2(VideoWidth + 2 * Border, HeaderHeight + videoHeight + Border);

            var box = UiFactory.Image("Window", transform, null, BoxColor, size);
            box.raycastTarget = true; // clicks on the window don't reach the board
            var rt = box.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-(SidePanel.RightWidth + Margin), Margin);
            box.gameObject.AddComponent<Outline>().effectColor = Gold;

            var heading = UiFactory.Text("Title", rt, title, 18, Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            var hr = heading.rectTransform;
            hr.anchorMin = new Vector2(0f, 1f);
            hr.anchorMax = new Vector2(1f, 1f);
            hr.pivot = new Vector2(0f, 1f);
            hr.offsetMin = new Vector2(Border + 4f, -HeaderHeight);
            hr.offsetMax = new Vector2(-(CloseSize + 2 * Border), 0f);

            // The X in the top-right corner.
            var close = UiFactory.Image("Close", rt, null, CloseColor, new Vector2(CloseSize, CloseSize));
            close.raycastTarget = true;
            var cr = close.rectTransform;
            cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(1f, 1f);
            cr.anchoredPosition = new Vector2(-Border, -(HeaderHeight - CloseSize) * 0.5f);
            var x = UiFactory.Text("X", cr, "X", 18, ViewColors.Paper, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.Fill(x.rectTransform);
            x.raycastTarget = false;
            var button = close.gameObject.AddComponent<Button>();
            button.targetGraphic = close;
            button.onClick.AddListener(Close);

            texture = new RenderTexture((int)Mathf.Max(1, clip.width), (int)Mathf.Max(1, clip.height), 0);
            var screen = new GameObject("Video", typeof(RectTransform), typeof(RawImage));
            screen.transform.SetParent(rt, false);
            var image = screen.GetComponent<RawImage>();
            image.texture = texture;
            image.color = Color.white;
            image.raycastTarget = false;
            var sr = image.rectTransform;
            sr.anchorMin = sr.anchorMax = sr.pivot = new Vector2(0.5f, 0f);
            sr.sizeDelta = new Vector2(VideoWidth, videoHeight);
            sr.anchoredPosition = new Vector2(0f, Border);

            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = VideoSource.VideoClip;
            player.clip = clip;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = texture;
            player.isLooping = true;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.Play();
        }

        void OnDestroy()
        {
            if (current == this) current = null;
            if (player != null) player.Stop();
            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
            }
        }
    }
}
