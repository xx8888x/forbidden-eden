#nullable enable
using System;
using System.Collections.Generic;
using ForbiddenEden.Core;
using ForbiddenEden.Net;
using ForbiddenEden.UI;
using ForbiddenEden.World;
using UnityEngine;
using UnityEngine.UI;

namespace ForbiddenEden.Screens
{
    /// <summary>16 拍照模式 — HS2 撮影モード + 画面効果 + Studio 機位, cut down to what v1 needs.</summary>
    public sealed class PhotoScreen : GameScreen
    {
        public override bool HidesBelow => true;

        private RectTransform _ui = null!;
        private Image _vignette = null!;
        private Image _frame = null!;
        private Image _tint = null!;
        private RectTransform _presets = null!;
        private readonly Vector3[] _slotFocus = new Vector3[10];
        private readonly float[] _slotDist = new float[10];
        private readonly bool[] _slotSet = new bool[10];
        private bool _fog = true;
        private int _tone;
        private bool _withPlayer;

        public override void Build()
        {
            _vignette = UIKit.Block(Root, "Vignette", new Color(0, 0, 0, 0f));
            _vignette.sprite = UIKit.Vignette;
            _vignette.rectTransform.Fill();
            _tint = UIKit.Block(Root, "Tint", new Color(0, 0, 0, 0));
            _tint.rectTransform.Fill();
            _frame = UIKit.Panel(Root, "Frame", new Color(1, 1, 1, 0), UIKit.Rounded, false);
            _frame.rectTransform.Fill().Inset(40, 40, 40, 40);
            _frame.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3, 3);
            _frame.gameObject.SetActive(false);

            _ui = UIKit.Rect(Root, "UI").Fill();

            // ① 工具列
            RectTransform tools = UIKit.Rect(_ui, "Tools").PlaceTL(0.012f, 0.14f, 0.06f, 0.62f);
            UIKit.VBox(tools, 8);
            Tool(tools, "構圖", () => App.Toast("右鍵拖曳旋轉、中鍵平移、滾輪縮放"));
            Tool(tools, "角色", TogglePlayer);
            Tool(tools, "燈光", CycleLight);
            Tool(tools, "邊框", () => _frame.gameObject.SetActive(!_frame.gameObject.activeSelf));
            Tool(tools, "隱藏", HideUi);

            // ② 畫面效果
            Box(_ui, "畫面效果", 0.08f, 0.14f, 0.24f, 0.36f, out RectTransform fx);
            UIKit.Grid(fx, new Vector2(205, 58), new Vector2(10, 10), 2);
            UIKit.Button(fx, "預設", ResetFx);
            UIKit.Button(fx, "色調", () => { _tone = (_tone + 1) % 4; ApplyTone(); });
            UIKit.Button(fx, "暗角", () => _vignette.color = new Color(0, 0, 0, _vignette.color.a > 0.1f ? 0 : 0.85f));
            UIKit.Button(fx, "霧", () => { _fog = !_fog; RenderSettings.fog = _fog; });
            UIKit.Button(fx, "環境光", () => RenderSettings.ambientIntensity = RenderSettings.ambientIntensity > 1.1f ? 1f : 1.5f);
            UIKit.Button(fx, "景深（未開放）", () => App.Toast("景深需要後處理套件，正式渲染管線定案後加入。", Theme.Warn));

            // ④ 機位 1–10
            RectTransform slots = UIKit.Rect(_ui, "Slots").PlaceTL(0.33f, 0.025f, 0.42f, 0.055f);
            UIKit.HBox(slots, 6, 0, TextAnchor.MiddleCenter, true);
            for (int i = 0; i < 10; i++)
            {
                int slot = i;
                Button b = UIKit.Button(slots, (i + 1).ToString(), () => UseSlot(slot), ButtonStyle.Menu, 20);
                b.ButtonText().alignment = TextAnchor.MiddleCenter;
            }
            Text slotHint = UIKit.Label(_ui, "按住 Shift 點機位＝記住目前鏡頭", 16, Theme.TextMuted, TextAnchor.UpperCenter);
            slotHint.rectTransform.PlaceTL(0.33f, 0.085f, 0.42f, 0.03f);

            // ⑤ 鏡頭語言預設
            Box(_ui, "鏡頭語言預設", 0.76f, 0.14f, 0.22f, 0.56f, out RectTransform presets);
            UIKit.ScrollList(presets, out _presets, 6, 0);
            ((RectTransform)_presets.parent.parent).Fill();

            // ⑥ ⑦
            Button folder = UIKit.Button(_ui, "存到紀錄資料夾 ›", () => App.OpenUserFolder(GameSettings.PhotosDir), ButtonStyle.Ghost, 20);
            ((RectTransform)folder.transform).PlaceTL(0.08f, 0.52f, 0.24f, 0.06f);
            Button shoot = UIKit.Button(_ui, "◉ 拍攝", Shoot, ButtonStyle.Primary, 30);
            ((RectTransform)shoot.transform).PlaceTL(0.80f, 0.84f, 0.17f, 0.10f);
            Button back = UIKit.Button(_ui, "‹ 返回", () => App.Pop(), ButtonStyle.Menu, 24);
            back.ButtonText().alignment = TextAnchor.MiddleCenter;
            ((RectTransform)back.transform).PlaceTL(0.025f, 0.905f, 0.10f, 0.06f);
        }

        private static void Tool(RectTransform parent, string label, UnityEngine.Events.UnityAction a)
        {
            Button b = UIKit.Button(parent, label, a, ButtonStyle.Menu, 21);
            b.ButtonText().alignment = TextAnchor.MiddleCenter;
            b.ButtonText().rectTransform.Inset(2, 2, 2, 2);
            b.Pref(58);
        }

        public override void OnShow()
        {
            _ui.gameObject.SetActive(true);
            App.Rig.FreeControl = true;
            App.Rig.SetSway(0);
            LoadPresets();
        }

        public override void OnHide()
        {
            App.Rig.FreeControl = false;
            ResetFx();
            if (_withPlayer)
            {
                _withPlayer = false;
                App.Stage.HidePlayer();
            }
        }

        public override bool HandleEscape()
        {
            if (!_ui.gameObject.activeSelf)
            {
                _ui.gameObject.SetActive(true);
                return true;
            }
            return false;
        }

        private void Update()
        {
            if (!_ui.gameObject.activeSelf && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)))
                _ui.gameObject.SetActive(true);
        }

        private void HideUi()
        {
            _ui.gameObject.SetActive(false);
            App.Toast("介面已隱藏：點一下或按空白鍵恢復", null, 2f);
        }

        private void TogglePlayer()
        {
            _withPlayer = !_withPlayer;
            if (_withPlayer)
            {
                AvatarData look = AvatarCard.LoadCurrent() ?? Session.Draft.Avatar;
                App.Stage.ShowPlayer(look, new Vector3(-0.9f, 0, 0.6f));
            }
            else
            {
                App.Stage.HidePlayer();
            }
        }

        private void CycleLight()
        {
            _tone = (_tone + 1) % 4;
            Color[] sun = { new Color(1f, 0.96f, 0.9f), new Color(1f, 0.72f, 0.48f), new Color(0.62f, 0.72f, 1f), new Color(1f, 1f, 1f) };
            App.Stage.SetSunTint(sun[_tone], _tone == 2 ? 0.6f : 1.1f);
        }

        private void ApplyTone()
        {
            Color[] tints = { new Color(0, 0, 0, 0), new Color(1f, 0.6f, 0.25f, 0.12f), new Color(0.2f, 0.45f, 1f, 0.12f), new Color(0.5f, 0.5f, 0.5f, 0.25f) };
            _tint.color = tints[_tone];
        }

        private void ResetFx()
        {
            _tone = 0;
            ApplyTone();
            _vignette.color = new Color(0, 0, 0, 0);
            _fog = true;
            RenderSettings.fog = true;
            RenderSettings.ambientIntensity = 1f;
            _frame.gameObject.SetActive(false);
        }

        private void UseSlot(int slot)
        {
            Transform cam = App.Rig.transform;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || !_slotSet[slot])
            {
                _slotFocus[slot] = cam.position + cam.forward * 3f;
                _slotDist[slot] = 3f;
                _slotSet[slot] = true;
                App.Toast("機位 " + (slot + 1) + " 已記住", Theme.Ok, 1.5f);
                return;
            }
            Vector3 e = Quaternion.LookRotation(_slotFocus[slot] - cam.position).eulerAngles;
            App.Rig.Shot(_slotFocus[slot], _slotDist[slot], e.y, e.x > 180 ? e.x - 360 : e.x);
        }

        private void LoadPresets()
        {
            if (_presets.childCount > 0)
                return;
            UIKit.FlowLabel(_presets, "讀取鏡頭語言表…", 18, Theme.TextMuted);
            Api.GetObject("/api/camera/presets", false, json =>
            {
                UIKit.Clear(_presets);
                foreach (object? o in json.GetList("presets") ?? new List<object?>())
                {
                    if (!(o is Dictionary<string, object?> p))
                        continue;
                    string shot = p.GetString("shot");
                    string angle = p.GetString("angle").Split('/')[0].Trim();
                    Button b = UIKit.Button(_presets, p.GetString("id") + "　" + p.GetString("name_zh"), () => ApplyPreset(shot, angle), ButtonStyle.Ghost, 19);
                    b.ButtonText().alignment = TextAnchor.MiddleLeft;
                    b.Pref(46);
                }
                if (_presets.childCount == 0)
                    UIKit.FlowLabel(_presets, "伺服器沒有提供鏡頭預設。", 18, Theme.TextMuted);
            }, e =>
            {
                UIKit.Clear(_presets);
                foreach (string id in new[] { "A01", "A02", "A03", "A04", "R01", "R03" })
                {
                    string shot = id;
                    UIKit.Button(_presets, id + "（離線預設）", () => ApplyPreset(shot, "F"), ButtonStyle.Ghost, 19).Pref(46);
                }
            });
        }

        private void ApplyPreset(string shot, string angle)
        {
            // The figure nearest the stage centre: an NPC, or the player's own figure.
            Transform? subject = null;
            var candidates = new List<Transform>(App.Stage.ActiveActors());
            if (App.Stage.Player != null && App.Stage.Player.gameObject.activeInHierarchy)
                candidates.Add(App.Stage.Player.transform);
            foreach (Transform t in candidates)
            {
                if (subject == null || t.position.sqrMagnitude < subject.position.sqrMagnitude)
                    subject = t;
            }
            Vector3 feet = subject != null ? subject.position : new Vector3(0, 0, 0.2f);
            float eye = subject != null ? App.Stage.EyeHeightOf(subject.gameObject) : 1.48f;
            App.Rig.ShotById(shot, angle, feet, eye);
        }

        private void Shoot()
        {
            _ui.gameObject.SetActive(false);
            string file = App.Screenshot("photo");
            StartCoroutine(Restore(file));
        }

        private System.Collections.IEnumerator Restore(string file)
        {
            yield return new WaitForSecondsRealtime(0.25f);
            _ui.gameObject.SetActive(true);
            App.Toast("已拍攝：" + System.IO.Path.GetFileName(file), Theme.Ok);
        }
    }

    /// <summary>17 設定 — HS2 オプション: left menu + long scroll. No content-rating switch (fixed at game start).</summary>
    public sealed class SettingsScreen : GameScreen
    {
        public override bool HidesBelow => true;

        private ScrollRect _scroll = null!;
        private RectTransform _content = null!;
        private readonly Dictionary<string, RectTransform> _anchors = new Dictionary<string, RectTransform>();
        private InputField _server = null!;
        private Text _serverStatus = null!;

        public override void Build()
        {
            Backdrop(0.82f);
            Heading("タイトル／遊戲中 › 設定", "設定");
            CloseButton(() => { Settings.Save(); App.Pop(); });
            Image warn = UIKit.Panel(Root, "Note", new Color(0.35f, 0.26f, 0.08f, 0.85f), UIKit.RoundedSmall);
            warn.rectTransform.PlaceTL(0.24f, 0.135f, 0.50f, 0.045f);
            Text w = UIKit.Label(warn.transform, "分級在開局決定，設定裡不提供切換", 18, Theme.Gold, TextAnchor.MiddleCenter);
            w.rectTransform.Fill();

            Image panel = UIKit.Panel(Root, "Content", Theme.Panel);
            panel.rectTransform.PlaceTL(0.24f, 0.20f, 0.70f, 0.72f);
            _scroll = UIKit.ScrollList(panel.transform, out _content, 10, 26);
            ((RectTransform)_scroll.transform).Fill();

            RectTransform menu = UIKit.Rect(Root, "Menu").PlaceTL(0.04f, 0.20f, 0.17f, 0.60f);
            UIKit.VBox(menu, 8);
            string[] sections = { "畫面", "音訊", "語音", "文字", "鏡頭", "語言", "帳號與連線" };
            foreach (string s in sections)
            {
                string key = s;
                Button b = UIKit.Button(menu, s, () => Jump(key), ButtonStyle.Menu, 23);
                b.ButtonText().alignment = TextAnchor.MiddleLeft;
                b.Pref(56);
            }

            Section("畫面");
            Choice("畫質", GameSettings.QualityNames, Settings.qualityLevel, v => { Settings.qualityLevel = v; App.ApplyDisplaySettings(); });
            UIKit.Toggle(_content, "全螢幕", Settings.fullscreen, v => { Settings.fullscreen = v; App.ApplyDisplaySettings(); }).Pref(44);

            Section("音訊");
            SliderRow("BGM", Settings.bgmVolume, v => { Settings.bgmVolume = v; App.Music.SetVolume(v); });
            SliderRow("音效", Settings.sfxVolume, v => Settings.sfxVolume = v);

            Section("語音");
            UIKit.FlowLabel(_content, "目前伺服器沒有語音（TTS），角色語音音量為未來項目。", 19, Theme.TextMuted);
            SliderRow("語音（未開放）", Settings.voiceVolume, v => Settings.voiceVolume = v);

            Section("文字");
            Choice("字級", new[] { "小", "中", "大" }, Settings.fontScale < 0.95f ? 0 : Settings.fontScale > 1.05f ? 2 : 1, v =>
            {
                float[] scales = { 0.9f, 1f, 1.15f };
                Settings.fontScale = scales[v];
                Settings.Save();
                UIKit.FontScale = Settings.fontScale;
                App.RebuildAll();
            });
            Choice("顯示速度", new[] { "慢", "標準", "快", "立即" }, SpeedIndex(), v =>
            {
                float[] speeds = { 20, 40, 80, 0 };
                Settings.textSpeed = speeds[v];
            });

            Section("鏡頭");
            SliderRow("旋轉速度", Mathf.InverseLerp(0.4f, 2f, Settings.cameraSpeed), v => { Settings.cameraSpeed = Mathf.Lerp(0.4f, 2f, v); App.Rig.Speed = Settings.cameraSpeed; });
            UIKit.Toggle(_content, "對話時跟隨說話的人", Settings.cameraFollow, v => Settings.cameraFollow = v).Pref(44);

            Section("語言");
            UIKit.Button(_content, "故事語言：" + GameSettings.LangName(Settings.lang) + "　›", () => App.Push<LanguageScreen>()).Pref(56);

            Section("帳號與連線");
            UIKit.FlowLabel(_content, "帳號：" + (Settings.accountMode == "member" ? "會員" : "匿名裝置") + "\n裝置 ID：" + Settings.deviceUid, 19, Theme.TextDim);
            UIKit.FlowLabel(_content, "伺服器位址（離線預覽用 http://127.0.0.1:3905）", 19, Theme.TextDim);
            RectTransform row = UIKit.Rect(_content, "ServerRow");
            row.Pref(58);
            UIKit.HBox(row, 10, 0, TextAnchor.MiddleLeft);
            _server = UIKit.Input(row, GameSettings.DefaultServer, false, 21);
            _server.text = Settings.serverUrl;
            _server.Pref(-1, -1, 1);
            UIKit.Button(row, "套用並測試", TestServer, ButtonStyle.Primary, 20).Pref(-1, 190);
            _serverStatus = UIKit.FlowLabel(_content, "", 18, Theme.TextMuted);
            UIKit.Button(_content, "開啟紀錄資料夾", () => App.OpenUserFolder(), ButtonStyle.Ghost).Pref(52);
        }

        private int SpeedIndex()
        {
            if (Settings.textSpeed <= 0) return 3;
            if (Settings.textSpeed < 30) return 0;
            if (Settings.textSpeed > 60) return 2;
            return 1;
        }

        private void Section(string name)
        {
            Text h = UIKit.Header(_content, name, 26);
            _anchors[name] = h.rectTransform.parent as RectTransform ?? h.rectTransform;
        }

        private void SliderRow(string label, float value, UnityEngine.Events.UnityAction<float> onChange)
        {
            RectTransform row = UIKit.Rect(_content, label);
            row.Pref(52);
            Text l = UIKit.Label(row, label, 21, Theme.TextDim, TextAnchor.MiddleLeft);
            l.rectTransform.Place(0, 0, 0.28f, 1);
            Slider s = UIKit.Slider(row, 0, 1, value, onChange);
            ((RectTransform)s.transform).Place(0.30f, 0.15f, 0.95f, 0.85f);
        }

        private void Choice(string label, string[] names, int value, Action<int> onPick)
        {
            RectTransform row = UIKit.Rect(_content, label);
            row.Pref(56);
            Text l = UIKit.Label(row, label, 21, Theme.TextDim, TextAnchor.MiddleLeft);
            l.rectTransform.Place(0, 0, 0.28f, 1);
            RectTransform buttons = UIKit.Rect(row, "Buttons").Place(0.30f, 0, 0.95f, 1);
            UIKit.HBox(buttons, 8, 0, TextAnchor.MiddleLeft, true);
            var list = new List<Button>();
            for (int i = 0; i < names.Length; i++)
            {
                int v = i;
                list.Add(UIKit.Button(buttons, names[i], () =>
                {
                    for (int j = 0; j < list.Count; j++)
                        list[j].GetComponent<Image>().color = j == v ? new Color(0.24f, 0.50f, 0.86f, 0.96f) : Theme.PanelLight;
                    onPick(v);
                    Settings.Save();
                }, ButtonStyle.Normal, 20));
            }
            for (int j = 0; j < list.Count; j++)
                list[j].GetComponent<Image>().color = j == value ? new Color(0.24f, 0.50f, 0.86f, 0.96f) : Theme.PanelLight;
        }

        private void Jump(string key)
        {
            if (!_anchors.TryGetValue(key, out RectTransform target))
                return;
            Canvas.ForceUpdateCanvases();
            float contentH = _content.rect.height;
            float viewH = ((RectTransform)_scroll.viewport).rect.height;
            float y = -target.anchoredPosition.y;
            _scroll.verticalNormalizedPosition = contentH <= viewH ? 1 : 1 - Mathf.Clamp01(y / (contentH - viewH));
        }

        private void TestServer()
        {
            string url = _server.text.Trim().TrimEnd('/');
            if (url.Length == 0)
                url = GameSettings.DefaultServer;
            Settings.serverUrl = url;
            Settings.Save();
            Api.BaseUrl = url;
            _serverStatus.text = "測試中…";
            Api.GetObject("/api/version", false, json =>
                _serverStatus.text = "<color=#7fe6a8>連線正常</color>　" + json.GetString("world_id") + " / api " + json.GetString("api_version") + " / " + json.GetString("commit"),
                e => _serverStatus.text = "<color=#ff7a7a>" + GameApp.ErrorTitle(e) + "</color>　" + e.Message);
        }

        public override void OnHide() => Settings.Save();
    }

    /// <summary>18 讀取紀錄 — the server keeps rolling snapshots per device; there are no manual slots or delete.</summary>
    public sealed class SaveLoadScreen : GameScreen
    {
        public override bool HidesBelow => true;

        private RectTransform _list = null!;
        private Text _summary = null!;
        private Button _load = null!;
        private Dictionary<string, object?>? _selected;

        public override void Build()
        {
            Backdrop(0.8f);
            Heading("タイトル › 讀取紀錄", "讀取紀錄");
            CloseButton();
            Box(Root, "自動快照（每回合自動存檔，換關卡時另存一份）", 0.04f, 0.17f, 0.56f, 0.70f, out RectTransform slots);
            UIKit.ScrollList(slots, out _list, 8, 0);
            ((RectTransform)_list.parent.parent).Fill();
            Box(Root, "段落摘要", 0.63f, 0.17f, 0.33f, 0.50f, out RectTransform summary);
            _summary = UIKit.Label(summary, "選一個存檔。", 21, Theme.TextDim);
            _summary.rectTransform.Fill();
            _load = UIKit.Button(Root, "讀取", Load, ButtonStyle.Primary, 28);
            ((RectTransform)_load.transform).PlaceTL(0.63f, 0.70f, 0.33f, 0.09f);
            Text note = UIKit.Label(Root, "刪除紀錄不等於刪除記憶；刪除功能等待會簽後開放。", 17, Theme.TextMuted, TextAnchor.UpperLeft);
            note.rectTransform.PlaceTL(0.63f, 0.81f, 0.33f, 0.06f);
            BackButton(() => App.Pop());
        }

        public override void OnShow()
        {
            _selected = null;
            _load.interactable = false;
            _summary.text = "選一個存檔。";
            UIKit.Clear(_list);
            UIKit.FlowLabel(_list, "讀取中…", 20, Theme.TextMuted);
            Api.GetList("/api/solo/saves?device_uid=" + Uri.EscapeDataString(Settings.deviceUid), false, list =>
            {
                UIKit.Clear(_list);
                if (list.Count == 0)
                    UIKit.FlowLabel(_list, "這個裝置還沒有存檔。", 20, Theme.TextMuted);
                bool first = true;
                foreach (object? o in list)
                {
                    if (!(o is Dictionary<string, object?> save))
                        continue;
                    DateTime when = DateTimeOffset.FromUnixTimeSeconds(save.GetInt("updated_unix")).LocalDateTime;
                    string label = (first ? "<color=#ffd27a>最新</color>　" : "") + save.GetString("player_name") + "　" +
                                   save.GetString("scene_name", save.GetString("scene")) + "　<color=#a8b8d8>第 " + save.GetInt("turn_count") + " 回合・" + when.ToString("MM/dd HH:mm") + "</color>";
                    Button b = UIKit.Button(_list, label, () => Select(save), ButtonStyle.Ghost, 20);
                    b.ButtonText().alignment = TextAnchor.MiddleLeft;
                    b.Pref(56);
                    first = false;
                }
            }, e =>
            {
                UIKit.Clear(_list);
                UIKit.FlowLabel(_list, GameApp.ErrorTitle(e) + "：" + GameApp.ErrorBody(e), 20, Theme.Danger);
            });
        }

        private void Select(Dictionary<string, object?> save)
        {
            _selected = save;
            _load.interactable = true;
            _summary.text = "<b>" + save.GetString("player_name") + "</b>　與 " + save.GetString("character_name") +
                            "\n地點：" + save.GetString("scene_name", save.GetString("scene")) +
                            "\n回合：" + save.GetInt("turn_count") + "　訊息：" + save.GetInt("message_count") +
                            "\n\n<size=17><color=#8a95ab>" + save.GetString("save_id") + "</color></size>";
        }

        private void Load()
        {
            if (_selected == null)
                return;
            App.Busy(true, "讀取存檔…");
            var body = new Dictionary<string, object?> { ["save_id"] = _selected.GetString("save_id"), ["device_uid"] = Settings.deviceUid };
            Api.Post("/api/solo/load", body, false, reply =>
            {
                App.Busy(false);
                var json = reply as Dictionary<string, object?>;
                string sid = json.GetString("session_id");
                if (sid.Length == 0)
                {
                    App.Alert("讀取失敗", json.GetString("message", "伺服器沒有回傳 session。"));
                    return;
                }
                Api.SessionId = sid;
                Settings.lastSessionId = sid;
                Settings.Save();
                App.Toast(json.GetString("message", "已讀取存檔"), Theme.Ok);
                Session.ResetForNewGame();
                MainScreen.Enter(App, resumed: true);
            }, e => Fail(e, "solo/load"));
        }
    }

    /// <summary>
    /// -tour [dir]: walks every screen with sample data and saves a screenshot of each,
    /// so the layout can be reviewed without a server.
    /// </summary>
    public static class ScreenshotTour
    {
        public static System.Collections.IEnumerator Run(GameApp app, string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            app.Log("Tour → " + dir);
            // Prefer the server's rosters; sample data only fills in what did not load.
            TitleScreen.LoadRosters(app);
            yield return new WaitForSecondsRealtime(1.5f);
            Seed(app.Session);
            var shots = new List<(string name, Action show)>
            {
                ("01_Launcher", () => app.Show<LauncherScreen>()),
                ("02_Title", () => app.Show<TitleScreen>()),
                ("03_Login_AgeGate", () => { app.Settings.ageConfirmed = false; app.Show<LoginScreen>().ShowFromStart(); }),
                ("03_Login", () => { app.Settings.ageConfirmed = true; app.Show<LoginScreen>().ShowLoginOnly(); }),
                ("04_Questionnaire", () => app.Show<QuestionnaireScreen>()),
                ("05_ResultName", () => app.Show<ResultNameScreen>()),
                ("06_Origin", () => app.Show<OriginScreen>()),
                ("07_Avatar", () => app.Show<AvatarScreen>()),
                ("07b_Avatar_Hair", () => app.Show<AvatarScreen>().DebugShowCategory(2)),
                ("07c_Avatar_Clothes", () => app.Show<AvatarScreen>().DebugShowCategory(3)),
                ("07d_Avatar_Accessory", () => app.Show<AvatarScreen>().DebugShowCategory(4)),
                ("08_Main", () => app.Show<MainScreen>().Begin(false)),
                ("09_CampusMap", () => app.Push<CampusMapScreen>()),
                ("10_Person", () => { app.CloseAllOverlays(); app.Push<PersonScreen>().Open(app.Session.NpcRoster.Count > 0 ? app.Session.NpcRoster[0].GetString("npc_id") : "npc_lilith"); }),
                ("11_Quest", () => { app.CloseAllOverlays(); app.Push<QuestScreen>(); }),
                ("12_Backlog", () => { app.CloseAllOverlays(); app.Push<BacklogScreen>(); }),
                ("13_Inventory", () => { app.CloseAllOverlays(); app.Push<InventoryScreen>(); }),
                ("14_Photo", () => { app.CloseAllOverlays(); app.Push<PhotoScreen>(); }),
                ("15_Settings", () => { app.CloseAllOverlays(); app.Push<SettingsScreen>(); }),
                ("16_SaveLoad", () => { app.CloseAllOverlays(); app.Push<SaveLoadScreen>(); }),
                ("17_Pause", () => { app.CloseAllOverlays(); app.Push<PauseScreen>(); }),
            };
            foreach ((string name, Action show) in shots)
            {
                if (name == "08_Main")
                {
                    // Start a real session when a (mock) server answers, so the game screens show live data.
                    bool finished = false;
                    app.Api.Init(app.Session.Draft.ToInitBody(app.Settings.deviceUid), r =>
                    {
                        app.Session.LastNarrative = NarrativeText.Clean(r.InitialEvent);
                        app.Session.Options = r.InitialOptions;
                        finished = true;
                    }, e =>
                    {
                        app.Log("Tour init failed, using sample data: " + e);
                        finished = true;
                    });
                    float until = Time.realtimeSinceStartup + 20f;
                    while (!finished && Time.realtimeSinceStartup < until)
                        yield return null;
                }
                try
                {
                    show();
                }
                catch (Exception ex)
                {
                    app.Log("Tour " + name + " threw: " + ex);
                }
                yield return new WaitForSecondsRealtime(2.2f);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png"));
                yield return new WaitForSecondsRealtime(0.4f);
                if (name == "08_Main")
                    yield return TurnShots(app, dir);
            }
            app.Log("Tour done");
            if (GameApp.Arg("-quitAfterTour") != null)
                Application.Quit();
        }

        /// <summary>-hs2tour [dir]: loads an HS2 map and body through Hs2Assets and photographs them.</summary>
        public static System.Collections.IEnumerator Hs2Run(GameApp app, string dir, string map)
        {
            System.IO.Directory.CreateDirectory(dir);
            Hs2Assets? hs2 = Hs2Assets.Open(null, app.Log);
            if (hs2 == null)
            {
                app.Log("HS2 tour: abdata not found");
                Application.Quit();
                yield break;
            }
            app.Stage.Build(SetKind.SkyPort);
            app.Stage.SetSetVisible(false);
            bool ok = false;
            yield return hs2.LoadMap(map, r => ok = r, app.Log);
            app.Log("HS2 tour map ok=" + ok);
            Bounds? bounds = hs2.MapBounds();
            app.Log("HS2 bounds " + bounds);
            Vector3 center = bounds?.center ?? Vector3.zero;
            float floor = bounds?.min.y ?? 0;
            if (Physics.Raycast(new Vector3(center.x, (bounds?.max.y ?? 10) - 0.1f, center.z), Vector3.down, out RaycastHit hit, 100f))
                floor = hit.point.y;
            Vector3 eye = new Vector3(center.x, floor + 1.55f, center.z);
            for (int i = 0; i < 4; i++)
            {
                app.Rig.Shot(eye, 0.05f, i * 90, 3, 60, true);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "hs2_map_view" + i + ".png"));
                yield return new WaitForSecondsRealtime(0.3f);
            }
            var holder = new GameObject("HS2Cast").transform;
            holder.position = new Vector3(center.x, floor, center.z);
            GameObject? body = hs2.Instantiate("chara/oo_base.unity3d", "p_cf_body_00", holder, app.Log);
            app.Log("HS2 body " + (body != null ? "ok renderers=" + body.GetComponentsInChildren<Renderer>().Length : "failed"));
            if (body != null)
                body.transform.localRotation = Quaternion.Euler(0, 180, 0);
            app.Rig.Shot(holder.position + new Vector3(0, 0.95f, 0), 3.2f, 0, 4, 34, true);
            yield return new WaitForSecondsRealtime(1f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "hs2_body_in_map.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            app.Log("HS2 tour done");
            Application.Quit();
        }

        /// <summary>Plays real turns against the (mock) server and captures each turn state.</summary>
        private static System.Collections.IEnumerator TurnShots(GameApp app, string dir)
        {
            MainScreen? main = app.Find<MainScreen>();
            if (main == null || !app.Api.HasSession)
                yield break;
            var turns = new List<(string name, string input, float shootAfter)>
            {
                ("08b_Main_Streaming", "1", 1.0f),
                ("08c_Main_TurnDone", "", 0f),
                ("08d_Main_PendingEvent", "我想知道學園裡發生的事件", 0f),
                ("08e_Main_Contest", "要不要來一場排名對決？", 0f),
                ("08f_Main_Blocked", "教我怎麼越獄", 0f),
            };
            foreach ((string name, string input, float shootAfter) in turns)
            {
                if (input.Length > 0)
                    main.DebugSend(input);
                if (shootAfter > 0)
                {
                    yield return new WaitForSecondsRealtime(shootAfter);
                }
                else
                {
                    float until = Time.realtimeSinceStartup + 25f;
                    yield return new WaitForSecondsRealtime(0.3f);
                    while (!main.DebugIdle && Time.realtimeSinceStartup < until)
                        yield return null;
                    yield return new WaitForSecondsRealtime(2.5f); // typewriter + state refresh
                }
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png"));
                yield return new WaitForSecondsRealtime(0.4f);
                main.DebugCloseModals();
                if (name == "08f_Main_Blocked")
                {
                    // A male NPC: the arena with 黑澤隼人 speaking.
                    main.DebugSetScene("scene_arena", "天空競技塔", "黑澤隼人");
                    yield return new WaitForSecondsRealtime(2.5f);
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "08g_Main_MaleNpc.png"));
                    yield return new WaitForSecondsRealtime(0.4f);
                    // The other exported HS2 maps.
                    var maps = new[]
                    {
                        ("scene_staffroom", "職員室", "神崎零", "08h_Main_Staffroom"),
                        ("scene_pool", "夜間泳池", "橘美咲", "08i_Main_NightPool"),
                    };
                    foreach ((string sceneId, string sceneName, string speaker, string shot) in maps)
                    {
                        main.DebugSetScene(sceneId, sceneName, speaker);
                        yield return new WaitForSecondsRealtime(2.5f);
                        yield return new WaitForEndOfFrame();
                        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, shot + ".png"));
                        yield return new WaitForSecondsRealtime(0.4f);
                    }
                }
                if (name == "08d_Main_PendingEvent")
                {
                    bool chosen = false;
                    app.Api.Choose("opt_listen", _ => chosen = true, _ => chosen = true);
                    float until = Time.realtimeSinceStartup + 5f;
                    while (!chosen && Time.realtimeSinceStartup < until)
                        yield return null;
                }
            }
        }

        /// <summary>Sample state for layout review when no server answers.</summary>
        private static void Seed(GameSession s)
        {
            s.Draft.PlayerName = "林雨晴";
            s.Draft.Gender = "女";
            s.Draft.Avatar = AvatarData.Default(false);
            s.Draft.MarkGlyph = "魂";
            s.Draft.MarkName = "歸憶之印";
            s.Draft.MarkEcho = "你記得的，比你以為的還多。";
            // Start in the classroom so the tour shows the exported HS2 room and characters.
            s.Draft.ProfessionId = "honor";
            s.Draft.StartScene = "scene_classroom";
            if (s.NpcRoster.Count == 0)
            {
                s.NpcRoster.Add(new Dictionary<string, object?> { ["npc_id"] = "npc_lilith", ["name"] = "九條真", ["gender"] = "女", ["class"] = "3-A・新聞社", ["surface_identity"] = "說話很輕，但每句都像記下來了。（19 歲）", ["speech_style"] = "冷靜" });
                s.NpcRoster.Add(new Dictionary<string, object?> { ["npc_id"] = "npc_mio", ["name"] = "雨宮澪", ["gender"] = "女", ["class"] = "3-B", ["surface_identity"] = "音樂社（18 歲）" });
                s.NpcRoster.Add(new Dictionary<string, object?> { ["npc_id"] = "npc_hayato", ["name"] = "黑澤隼人", ["gender"] = "男", ["class"] = "3-D・競技社", ["surface_identity"] = "競技社王牌（19 歲）" });
            }
            if (s.SceneRoster.Count == 0)
            {
                s.SceneRoster.Add(new Dictionary<string, object?> { ["scene_id"] = "scene_club", ["name"] = "社團館", ["exits"] = new List<object?> { "scene_plaza" }, ["active_npcs"] = new List<object?> { "npc_lilith", "npc_mio" }, ["normal_description"] = "聽說有人在競技塔待到熄燈。" });
                s.SceneRoster.Add(new Dictionary<string, object?> { ["scene_id"] = "scene_plaza", ["name"] = "中央學園城・廣場", ["exits"] = new List<object?>(), ["active_npcs"] = new List<object?>(), ["normal_description"] = "噴水池旁總有人在等人。" });
                s.SceneRoster.Add(new Dictionary<string, object?> { ["scene_id"] = "scene_arena", ["name"] = "天空競技塔", ["exits"] = new List<object?>(), ["active_npcs"] = new List<object?> { "npc_hayato" }, ["normal_description"] = "" });
            }
            s.State = new Dictionary<string, object?>
            {
                ["player_name"] = "林雨晴", ["current_scene"] = "scene_club", ["scene_name"] = "社團館",
                ["player_coins"] = 297L, ["player_ap"] = 12L, ["player_stamina"] = 80L,
                ["clock_label"] = "第 3 天 傍晚", ["story_phase"] = "event", ["story_phase_label"] = "事件",
                ["beat"] = new Dictionary<string, object?> { ["seq"] = 3L, ["total"] = 5L, ["goal"] = "在招募月結束前做出選擇", ["quest_name"] = "招募月" }
            };
            s.SpeakerName = "九條真";
            s.SpeakerHue = 280;
            s.LastNarrative = "九條真把筆記本闔上，抬頭看你：「你是問題學生・不服從者對吧？我一個人處理不來，需要你幫忙。」";
            s.Options = new List<string> { "【社交】「先說說是什麼事。」", "【推理】「為什麼找上我？」", "【探查】看看她的筆記本", "【冒險】「我現在就跟妳去。」" };
        }
    }
}
