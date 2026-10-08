#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using ForbiddenEden.Net;
using ForbiddenEden.Screens;
using ForbiddenEden.UI;
using ForbiddenEden.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ForbiddenEden.Core
{
    /// <summary>
    /// Boots the game without any authored scene content: settings, the 3D stage, the
    /// uGUI canvas, the EDEN API client, and the screen stack.
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        public static GameApp Instance { get; private set; } = null!;

        public GameSettings Settings { get; private set; } = null!;
        public GameSession Session { get; private set; } = null!;
        public EdenApiClient Api { get; private set; } = null!;
        public Stage Stage { get; private set; } = null!;
        public CameraRig Rig { get; private set; } = null!;
        public Canvas Canvas { get; private set; } = null!;
        public AmbientMusic Music { get; private set; } = null!;

        private RectTransform _screenLayer = null!;
        private RectTransform _overlayLayer = null!;
        private RectTransform _modalLayer = null!;
        private RectTransform _toastLayer = null!;
        private readonly Dictionary<Type, GameScreen> _screens = new Dictionary<Type, GameScreen>();
        private readonly List<GameScreen> _overlays = new List<GameScreen>();
        private GameScreen? _current;
        private GameObject? _busy;
        private Text? _busyText;
        private StreamWriter? _log;

        public GameScreen? Current => _current;
        public bool HasOverlay => _overlays.Count > 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Instance == null)
                new GameObject("GameApp").AddComponent<GameApp>();
        }

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;
            Settings = GameSettings.Load();
            OpenLog();
            Log("Boot " + Application.productName + " " + Application.version + " data=" + GameSettings.UserDataDir);

            string? server = Arg("-server");
            if (!string.IsNullOrEmpty(server))
                Settings.serverUrl = server!;

            ApplyDisplaySettings();
            UIKit.FontScale = Settings.fontScale;
            Session = new GameSession();
            Api = new EdenApiClient(this, Settings.serverUrl, Log);
            Rig = CameraRig.Attach();
            Rig.Speed = Settings.cameraSpeed;
            Stage = Stage.Create();
            Music = AmbientMusic.Create(transform, Settings.bgmVolume);
            BuildCanvas();
        }

        private void Start()
        {
            string? hs2Tour = Arg("-hs2tour");
            if (hs2Tour != null)
            {
                StartCoroutine(ScreenshotTour.Hs2Run(this, hs2Tour.Length > 0 ? hs2Tour : Path.Combine(GameSettings.UserDataDir, "hs2tour"),
                    Arg("-hs2map") is string m && m.Length > 0 ? m : "map/scene/34/classroom.unity3d"));
                return;
            }
            string? tour = Arg("-tour");
            if (tour != null)
            {
                StartCoroutine(ScreenshotTour.Run(this, tour.Length > 0 ? tour : Path.Combine(GameSettings.UserDataDir, "tour")));
                return;
            }
            Show<LauncherScreen>();
        }

        private void OnDestroy()
        {
            _log?.Dispose();
        }

        // ---- screens -------------------------------------------------------------------

        public T Show<T>() where T : GameScreen
        {
            CloseAllOverlays();
            T screen = GetOrCreate<T>(_screenLayer);
            if (_current != null && _current != screen)
            {
                _current.OnHide();
                _current.gameObject.SetActive(false);
            }
            _current = screen;
            screen.gameObject.SetActive(true);
            screen.OnShow();
            return screen;
        }

        public T Push<T>() where T : GameScreen
        {
            T screen = GetOrCreate<T>(_overlayLayer);
            _overlays.Remove(screen);
            _overlays.Add(screen);
            screen.transform.SetAsLastSibling();
            screen.gameObject.SetActive(true);
            screen.OnShow();
            RefreshVisibility();
            return screen;
        }

        public void Pop()
        {
            if (_overlays.Count == 0)
                return;
            GameScreen top = _overlays[_overlays.Count - 1];
            _overlays.RemoveAt(_overlays.Count - 1);
            top.OnHide();
            top.gameObject.SetActive(false);
            RefreshVisibility();
            if (_overlays.Count == 0)
                _current?.OnOverlaysClosed();
        }

        /// <summary>Everything under the top-most full-screen overlay is hidden.</summary>
        private void RefreshVisibility()
        {
            bool visible = true;
            for (int i = _overlays.Count - 1; i >= 0; i--)
            {
                _overlays[i].gameObject.SetActive(visible);
                if (_overlays[i].HidesBelow)
                    visible = false;
            }
            if (_current != null)
                _current.gameObject.SetActive(visible);
        }

        public void CloseAllOverlays()
        {
            while (_overlays.Count > 0)
                Pop();
        }

        public T? Find<T>() where T : GameScreen => _screens.TryGetValue(typeof(T), out GameScreen s) ? (T)s : null;

        /// <summary>Rebuilds every screen (after a font size change) and returns to the current one.</summary>
        public void RebuildAll()
        {
            Type? current = _current?.GetType();
            CloseAllOverlays();
            foreach (GameScreen s in _screens.Values)
                Destroy(s.gameObject);
            _screens.Clear();
            _current = null;
            if (current != null)
            {
                var method = typeof(GameApp).GetMethod(nameof(Show))!.MakeGenericMethod(current);
                method.Invoke(this, null);
            }
        }

        private T GetOrCreate<T>(RectTransform layer) where T : GameScreen
        {
            if (_screens.TryGetValue(typeof(T), out GameScreen existing) && existing != null)
                return (T)existing;
            RectTransform rt = UIKit.Rect(layer, typeof(T).Name).Fill();
            var screen = rt.gameObject.AddComponent<T>();
            screen.App = this;
            screen.Root = rt;
            screen.Build();
            rt.gameObject.SetActive(false);
            _screens[typeof(T)] = screen;
            return screen;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_modalLayer.childCount > 0)
                    return;
                if (_overlays.Count > 0)
                {
                    GameScreen top = _overlays[_overlays.Count - 1];
                    if (!top.HandleEscape())
                        Pop();
                    return;
                }
                _current?.HandleEscape();
            }
            if (Input.GetKeyDown(KeyCode.F12))
                Screenshot("manual");
        }

        // ---- modal helpers ------------------------------------------------------------

        public void Confirm(string title, string body, string okLabel, Action? onOk, string? cancelLabel = "取消", Action? onCancel = null)
        {
            Image shade = UIKit.Block(_modalLayer, "Modal", new Color(0, 0, 0, 0.55f), true);
            shade.rectTransform.Fill();
            Image box = UIKit.Panel(shade.transform, "Box", Theme.Panel);
            box.rectTransform.Place(0.32f, 0.33f, 0.68f, 0.67f);
            Image edge = UIKit.Block(box.transform, "Edge", Theme.Accent);
            edge.rectTransform.Place(0, 1, 1, 1);
            edge.rectTransform.offsetMin = new Vector2(24, -4);
            edge.rectTransform.offsetMax = new Vector2(-24, 0);
            Text t = UIKit.Label(box.transform, title, 30, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            t.rectTransform.Place(0, 0.78f, 1, 1).Inset(36, 26, 36, 0);
            Text b = UIKit.Label(box.transform, body, 22, Theme.TextDim);
            b.rectTransform.Place(0, 0.26f, 1, 0.78f).Inset(36, 6, 36, 0);
            RectTransform row = UIKit.Rect(box.transform, "Buttons").Place(0, 0, 1, 0.24f).Inset(36, 0, 36, 26);
            UIKit.HBox(row, 14, 0, TextAnchor.MiddleRight);
            if (cancelLabel != null)
            {
                UIKit.Button(row, cancelLabel, () =>
                {
                    Destroy(shade.gameObject);
                    onCancel?.Invoke();
                }, ButtonStyle.Ghost).Pref(56, 180);
            }
            UIKit.Button(row, okLabel, () =>
            {
                Destroy(shade.gameObject);
                onOk?.Invoke();
            }, ButtonStyle.Primary).Pref(56, 200);
        }

        public void Alert(string title, string body, Action? onOk = null) => Confirm(title, body, "了解", onOk, null);

        public void Toast(string text, Color? color = null, float seconds = 3.2f)
        {
            Image bg = UIKit.Panel(_toastLayer, "Toast", new Color(0.05f, 0.08f, 0.16f, 0.94f));
            int index = _toastLayer.childCount - 1;
            bg.rectTransform.anchorMin = new Vector2(0.5f, 1);
            bg.rectTransform.anchorMax = new Vector2(0.5f, 1);
            bg.rectTransform.pivot = new Vector2(0.5f, 1);
            bg.rectTransform.sizeDelta = new Vector2(760, 64);
            bg.rectTransform.anchoredPosition = new Vector2(0, -120 - index * 72);
            Image bar = UIKit.Block(bg.transform, "Bar", color ?? Theme.Accent);
            bar.rectTransform.Place(0, 0, 0, 1).Inset(0, 10, 0, 10);
            bar.rectTransform.sizeDelta = new Vector2(5, -20);
            bar.rectTransform.anchoredPosition = new Vector2(16, 0);
            Text t = UIKit.Label(bg.transform, text, 22, Theme.Text, TextAnchor.MiddleLeft);
            t.rectTransform.Fill().Inset(34, 4, 20, 4);
            Destroy(bg.gameObject, seconds);
        }

        public void Busy(bool on, string text = "連線中…")
        {
            if (!on)
            {
                if (_busy != null)
                    _busy.SetActive(false);
                return;
            }
            if (_busy == null)
            {
                Image shade = UIKit.Block(_modalLayer.parent, "Busy", new Color(0, 0, 0, 0.35f), true);
                shade.rectTransform.Fill();
                Image box = UIKit.Panel(shade.transform, "Box", Theme.Panel);
                box.rectTransform.Place(0.40f, 0.45f, 0.60f, 0.55f);
                _busyText = UIKit.Label(box.transform, text, 24, Theme.Text, TextAnchor.MiddleCenter);
                _busyText.rectTransform.Fill();
                box.gameObject.AddComponent<Pulse>();
                _busy = shade.gameObject;
            }
            _busyText!.text = text;
            _busy.transform.SetAsLastSibling();
            _busy.SetActive(true);
        }

        /// <summary>Shows a server error the way the 主畫面 error states describe it.</summary>
        public void ShowError(EdenApiError error, string context = "")
        {
            string title = ErrorTitle(error);
            string body = ErrorBody(error);
            Log("Error " + context + ": " + error);
            Alert(title, body);
        }

        public static string ErrorTitle(EdenApiError e)
        {
            switch (e.Code)
            {
                case "network": return "連不上伺服器";
                case "no_session": return "尚未建立遊戲";
                case "no_coins": return "EP 不足";
                case "insufficient_ap": return "行動點不足";
                case "insufficient_coins": return "EP 不足";
                case "blocked_input": return "這句話說不出口";
                case "pending_choice": return "有事件等你回應";
                case "rate_limited": case "ip_concurrent_limited": return "請稍候再試";
                case "actor_busy": case "actor_timeout": return "伺服器忙碌中";
                default: return "發生錯誤";
            }
        }

        public static string ErrorBody(EdenApiError e)
        {
            switch (e.Code)
            {
                case "network": return "請確認伺服器位址（設定 › 帳號與連線）或網路狀態。\n" + e.Message;
                case "no_coins": return "每一回合對話會消耗 1 EP。EP 用完時暫時無法繼續對話。";
                case "rate_limited": case "ip_concurrent_limited": return "請求太頻繁，請等幾秒再試。";
                case "actor_busy": case "actor_timeout": return "上一個動作還在處理，請稍後再送出。";
            }
            return string.IsNullOrEmpty(e.Message) ? e.ToString() : e.Message;
        }

        // ---- settings / platform ------------------------------------------------------

        public void ApplyDisplaySettings()
        {
            QualitySettings.SetQualityLevel(Mathf.Clamp(Settings.qualityLevel * 2, 0, QualitySettings.names.Length - 1), true);
            QualitySettings.shadowDistance = Settings.qualityLevel == 0 ? 20 : 60;
            Resolution[] res = Screen.resolutions;
            if (Settings.resolutionIndex >= 0 && Settings.resolutionIndex < res.Length && !Application.isEditor)
            {
                Resolution r = res[Settings.resolutionIndex];
                Screen.SetResolution(r.width, r.height, Settings.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            }
            else if (!Application.isEditor)
            {
                Screen.fullScreenMode = Settings.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            }
        }

        public void OpenUserFolder(string? dir = null)
        {
            string path = dir ?? GameSettings.UserDataDir;
            Application.OpenURL("file:///" + path.Replace('\\', '/'));
        }

        public string Screenshot(string prefix)
        {
            string file = Path.Combine(GameSettings.PhotosDir, prefix + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
            ScreenCapture.CaptureScreenshot(file);
            return file;
        }

        public void Log(string line)
        {
            string stamped = DateTime.Now.ToString("HH:mm:ss.fff") + " " + line;
            Debug.Log("[EDEN] " + line);
            try
            {
                _log?.WriteLine(stamped);
                _log?.Flush();
            }
            catch (IOException)
            {
            }
        }

        private void OpenLog()
        {
            try
            {
                _log = new StreamWriter(Path.Combine(GameSettings.LogsDir, "client.log"), append: false);
            }
            catch (IOException)
            {
                _log = null;
            }
        }

        public static string? Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : string.Empty;
            }
            return null;
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.layer = 5;
            canvasGo.transform.SetParent(transform, false);
            Canvas = canvasGo.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var root = (RectTransform)canvasGo.transform;
            _screenLayer = UIKit.Rect(root, "Screens").Fill();
            _overlayLayer = UIKit.Rect(root, "Overlays").Fill();
            _modalLayer = UIKit.Rect(root, "Modals").Fill();
            _toastLayer = UIKit.Rect(root, "Toasts").Fill();

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(transform, false);
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
        }
    }

    /// <summary>Breathing alpha for "waiting" indicators.</summary>
    public sealed class Pulse : MonoBehaviour
    {
        public float Speed = 3f;
        private CanvasGroup _group = null!;

        private void Awake() => _group = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

        private void Update() => _group.alpha = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * Speed));
    }

    /// <summary>A soft generated pad so the BGM slider has something to control until real music exists.</summary>
    public sealed class AmbientMusic : MonoBehaviour
    {
        private AudioSource _source = null!;

        public static AmbientMusic Create(Transform parent, float volume)
        {
            var go = new GameObject("AmbientMusic");
            go.transform.SetParent(parent, false);
            var m = go.AddComponent<AmbientMusic>();
            m._source = go.AddComponent<AudioSource>();
            m._source.clip = MakePad();
            m._source.loop = true;
            m._source.volume = volume * 0.35f;
            m._source.Play();
            return m;
        }

        public void SetVolume(float v) => _source.volume = v * 0.35f;

        private static AudioClip MakePad()
        {
            const int rate = 22050;
            const float seconds = 16f;
            int n = (int)(rate * seconds);
            var data = new float[n];
            float[][] chords =
            {
                new[] { 220.00f, 277.18f, 329.63f, 440.00f },
                new[] { 196.00f, 246.94f, 293.66f, 392.00f },
                new[] { 174.61f, 220.00f, 261.63f, 349.23f },
                new[] { 196.00f, 246.94f, 329.63f, 392.00f }
            };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                int c = (int)(t / (seconds / 4f)) % 4;
                float local = t % (seconds / 4f);
                float env = Mathf.Clamp01(local / 1.2f) * Mathf.Clamp01((seconds / 4f - local) / 1.2f);
                float v = 0;
                foreach (float f in chords[c])
                    v += Mathf.Sin(2 * Mathf.PI * f * t) * 0.12f + Mathf.Sin(2 * Mathf.PI * f * 2.002f * t) * 0.03f;
                data[i] = v * env * (0.8f + 0.2f * Mathf.Sin(t * 0.7f));
            }
            var clip = AudioClip.Create("Pad", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
