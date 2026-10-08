#nullable enable
using System.Collections.Generic;
using ForbiddenEden.Core;
using ForbiddenEden.Net;
using ForbiddenEden.UI;
using ForbiddenEden.World;
using UnityEngine;
using UnityEngine.UI;

namespace ForbiddenEden.Screens
{
    /// <summary>01 客戶端啟動器 — the HS2 launcher layout, in-game, plus a server check.</summary>
    public sealed class LauncherScreen : GameScreen
    {
        private Text _server = null!;
        private Text _account = null!;
        private Text _res = null!;
        private Text _lang = null!;
        private Text _display = null!;
        private readonly List<Button> _quality = new List<Button>();

        public override void Build()
        {
            Image shade = Backdrop(0.35f);

            // Left: title image and notice.
            Image title = UIKit.Panel(Root, "TitleArt", new Color(0.07f, 0.12f, 0.24f, 0.88f));
            title.rectTransform.PlaceTL(0.06f, 0.08f, 0.50f, 0.36f);
            Text logo = UIKit.Label(title.transform, "超禁忌學園", 84, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            logo.rectTransform.Place(0, 0.38f, 1, 0.92f);
            Text sub = UIKit.Label(title.transform, "～ E D E N ～", 34, Theme.Gold, TextAnchor.MiddleCenter);
            sub.rectTransform.Place(0, 0.16f, 1, 0.40f);
            Text ver = UIKit.Label(title.transform, "Unity 客戶端 v0.1　原型", 18, Theme.TextMuted, TextAnchor.LowerRight);
            ver.rectTransform.Fill().Inset(0, 0, 20, 14);

            Box(Root, "公告", 0.06f, 0.47f, 0.50f, 0.40f, out RectTransform notice);
            UIKit.FlowLabel(notice,
                "・本作品所有登場人物皆為 18 歲以上的成年人。\n" +
                "・本作品為虛構故事，與現實人物、團體無關。\n" +
                "・每一回合對話會消耗 1 EP；目前連線的伺服器顯示在右下。\n" +
                "・這是第一版原型：3D 角色與場景皆為暫代美術，正式美術待定。\n" +
                "・畫面、存檔、外觀卡、照片都放在「開啟紀錄資料夾」裡。", 21, Theme.TextDim);

            // Right column: settings + start.
            RectTransform col = UIKit.Rect(Root, "Right").PlaceTL(0.60f, 0.08f, 0.34f, 0.79f);
            UIKit.VBox(col, 12);
            _lang = Row(col, "語言", () =>
            {
                int i = System.Array.IndexOf(GameSettings.LangCodes, Settings.lang);
                Settings.lang = GameSettings.LangCodes[(i + 1) % GameSettings.LangCodes.Length];
                Settings.Save();
                Refresh();
            });
            _res = Row(col, "解析度", () =>
            {
                Resolution[] res = Screen.resolutions;
                if (res.Length == 0)
                    return;
                Settings.resolutionIndex = (Settings.resolutionIndex + 1) % res.Length;
                Settings.Save();
                App.ApplyDisplaySettings();
                Refresh();
            });
            UIKit.Toggle(col, "全螢幕", Settings.fullscreen, v =>
            {
                Settings.fullscreen = v;
                Settings.Save();
                App.ApplyDisplaySettings();
            }).Pref(44);
            RectTransform q = UIKit.Rect(col, "Quality");
            q.Pref(56);
            UIKit.HBox(q, 8, 0, TextAnchor.MiddleLeft, true);
            for (int i = 0; i < GameSettings.QualityNames.Length; i++)
            {
                int level = i;
                Button b = UIKit.Button(q, GameSettings.QualityNames[i], () =>
                {
                    Settings.qualityLevel = level;
                    Settings.Save();
                    App.ApplyDisplaySettings();
                    Refresh();
                });
                _quality.Add(b);
            }
            _display = Row(col, "顯示器", () => App.Toast(Display.displays.Length > 1 ? "多螢幕：請在 Windows 顯示設定選擇主螢幕" : "只偵測到一個顯示器"));

            UIKit.Button(col, "開始遊戲", () => App.Show<TitleScreen>(), ButtonStyle.Primary, 30).Pref(78);
            RectTransform two = UIKit.Rect(col, "Two");
            two.Pref(56);
            UIKit.HBox(two, 8, 0, TextAnchor.MiddleLeft, true);
            UIKit.Button(two, "手冊", () => App.Alert("操作說明",
                "滑鼠左鍵：選擇、翻頁\n滑鼠右鍵拖曳：旋轉鏡頭（外觀自訂、拍照）\n滾輪：縮放\nEsc：返回／遊戲選單\nF12：截圖\nEnter：送出自己打的字"));
            UIKit.Button(two, "開啟紀錄資料夾", () => App.OpenUserFolder());
            UIKit.Button(col, "結束", Quit, ButtonStyle.Ghost).Pref(52);

            Image status = UIKit.Panel(Root, "Status", Theme.PanelSoft);
            status.rectTransform.PlaceTL(0.60f, 0.89f, 0.34f, 0.075f);
            _account = UIKit.Label(status.transform, string.Empty, 18, Theme.TextDim, TextAnchor.MiddleLeft);
            _account.rectTransform.Place(0, 0.5f, 1, 1).Inset(18, 2, 18, 0);
            _server = UIKit.Label(status.transform, string.Empty, 18, Theme.TextDim, TextAnchor.MiddleLeft);
            _server.rectTransform.Place(0, 0, 1, 0.5f).Inset(18, 0, 18, 2);
        }

        private Text Row(RectTransform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            RectTransform row = UIKit.Rect(parent, label);
            row.Pref(56);
            Text l = UIKit.Label(row, label, 22, Theme.TextDim, TextAnchor.MiddleLeft);
            l.rectTransform.Place(0, 0, 0.32f, 1);
            Button b = UIKit.Button(row, "", onClick);
            ((RectTransform)b.transform).Place(0.32f, 0, 1, 1);
            return b.ButtonText();
        }

        public override void OnShow()
        {
            App.Stage.ClearCast();
            App.Stage.HidePlayer();
            App.Stage.Build(SetKind.SkyPort);
            App.Rig.FreeControl = false;
            App.Rig.Shot(new Vector3(0, 3.5f, 14), 16, 8, 4, 40, true);
            App.Rig.SetSway(1f);
            Refresh();
            _server.text = "伺服器：" + Settings.serverUrl + "　檢查中…";
            Api.BaseUrl = Settings.serverUrl;
            Api.GetObject("/api/version", false, json =>
            {
                _server.text = "伺服器：" + Settings.serverUrl + "　<color=#7fe6a8>連線正常</color>（" + json.GetString("world_id") + " " + json.GetString("api_version") + "）";
            }, e =>
            {
                _server.text = "伺服器：" + Settings.serverUrl + "　<color=#ff7a7a>無法連線</color>";
            });
        }

        private void Refresh()
        {
            _lang.text = GameSettings.LangName(Settings.lang) + "　›";
            Resolution[] res = Screen.resolutions;
            _res.text = Settings.resolutionIndex >= 0 && Settings.resolutionIndex < res.Length
                ? res[Settings.resolutionIndex].width + " × " + res[Settings.resolutionIndex].height + "　›"
                : Screen.width + " × " + Screen.height + "（目前）　›";
            _display.text = "顯示器 1 / " + Display.displays.Length;
            for (int i = 0; i < _quality.Count; i++)
                _quality[i].GetComponent<Image>().color = i == Settings.qualityLevel ? new Color(0.24f, 0.50f, 0.86f, 0.96f) : Theme.PanelLight;
            _account.text = "帳號：" + (Settings.accountMode == "member" ? "會員" : "匿名裝置") + "　" + Settings.deviceUid;
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public override bool HandleEscape()
        {
            Quit();
            return true;
        }
    }

    /// <summary>02 標題畫面 — 天空港 3D, logo, right-bottom vertical menu (HS2 layout).</summary>
    public sealed class TitleScreen : GameScreen
    {
        private Button _continue = null!;
        private Text _account = null!;
        private bool _hasSession;
        private float _rotateAt;
        private int _rotateIndex;

        public override void Build()
        {
            Image shade = UIKit.Block(Root, "Shade", Color.white, false);
            shade.sprite = UIKit.Vignette;
            shade.color = new Color(0, 0, 0, 0.55f);
            shade.rectTransform.Fill();

            Text logo = UIKit.Label(Root, "超禁忌學園", 108, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            logo.rectTransform.PlaceTL(0.06f, 0.10f, 0.6f, 0.16f);
            logo.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3, -3);
            Text sub = UIKit.Label(Root, "～ E D E N ～", 40, Theme.Gold, TextAnchor.UpperLeft);
            sub.rectTransform.PlaceTL(0.065f, 0.255f, 0.5f, 0.07f);
            Text tag = UIKit.Label(Root, "天空都市・浮島學園　你從何處醒來？", 24, Theme.TextDim, TextAnchor.UpperLeft);
            tag.rectTransform.PlaceTL(0.067f, 0.325f, 0.5f, 0.05f);

            RectTransform menu = UIKit.Rect(Root, "Menu").PlaceTL(0.70f, 0.50f, 0.25f, 0.42f);
            UIKit.VBox(menu, 10, 0, TextAnchor.LowerRight);
            UIKit.Button(menu, "開始新生活", StartNewLife, ButtonStyle.Menu, 30).Pref(70);
            _continue = UIKit.Button(menu, "繼續", Continue, ButtonStyle.Menu, 30);
            _continue.Pref(70);
            UIKit.Button(menu, "讀取紀錄", () => App.Push<SaveLoadScreen>(), ButtonStyle.Menu, 30).Pref(70);
            UIKit.Button(menu, "設定", () => App.Push<SettingsScreen>(), ButtonStyle.Menu, 30).Pref(70);
            UIKit.Button(menu, "結束", () => App.Show<LauncherScreen>(), ButtonStyle.Menu, 30).Pref(70);

            Image acct = UIKit.Panel(Root, "Account", Theme.PanelSoft);
            acct.rectTransform.PlaceTL(0.03f, 0.90f, 0.36f, 0.065f);
            _account = UIKit.Label(acct.transform, string.Empty, 20, Theme.TextDim, TextAnchor.MiddleLeft);
            _account.rectTransform.Fill().Inset(18, 0, 120, 0);
            Button sw = UIKit.Button(acct.transform, "切換", () => App.Show<LoginScreen>().ShowLoginOnly(), ButtonStyle.Ghost, 20);
            ((RectTransform)sw.transform).Place(1, 0, 1, 1).Inset(-110, 8, 10, 8);

            SystemButtons();
        }

        public override void OnShow()
        {
            App.Stage.Build(SetKind.SkyPort);
            App.Stage.HidePlayer();
            App.Rig.FreeControl = false;
            App.Rig.SetSway(1f);
            App.Rig.Shot(new Vector3(-0.25f, 1.0f, 0.2f), 4.6f, -10, 4, 34, true);
            _account.text = (Settings.accountMode == "member" ? "會員" : "匿名裝置") + "　" + Settings.deviceUid;
            _rotateAt = 0;
            _continue.interactable = false;
            Api.BaseUrl = Settings.serverUrl;
            LoadRosters(App);
            Api.AutoLogin(Settings.deviceUid, json =>
            {
                _hasSession = json.GetBool("has_session");
                _continue.interactable = _hasSession;
                if (_hasSession)
                    _continue.ButtonText().text = "繼續　<size=20><color=#a8b8d8>" + json.GetString("player_name") + "</color></size>";
            }, _ => _continue.interactable = false);
        }

        private void Update()
        {
            // 標題角色：預設輪替核心人物。
            if (Time.time < _rotateAt)
                return;
            _rotateAt = Time.time + 9f;
            List<Dictionary<string, object?>> roster = Session.NpcRoster;
            var cast = new List<(string, string, string)>();
            if (roster.Count > 0)
            {
                Dictionary<string, object?> npc = roster[_rotateIndex++ % Mathf.Min(12, roster.Count)];
                cast.Add((npc.GetString("npc_id"), npc.GetString("name"), npc.GetString("gender")));
            }
            else
            {
                cast.Add(("title_" + (_rotateIndex++ % 4), "", _rotateIndex % 2 == 0 ? "女" : "男"));
            }
            App.Stage.ClearCast();
            App.Stage.SetCast(cast, cast[0].Item1);
        }

        /// <summary>Caches /api/npc/roster and /api/scene/roster (no session needed).</summary>
        public static void LoadRosters(GameApp app, System.Action? onNpcs = null)
        {
            if (app.Session.NpcRoster.Count == 0)
            {
                app.Api.GetList("/api/npc/roster", false, list =>
                {
                    app.Session.NpcRoster.Clear();
                    foreach (object? o in list)
                    {
                        if (o is Dictionary<string, object?> d)
                            app.Session.NpcRoster.Add(d);
                    }
                    onNpcs?.Invoke();
                }, _ => { });
            }
            if (app.Session.SceneRoster.Count == 0)
            {
                app.Api.GetList("/api/scene/roster", false, list =>
                {
                    app.Session.SceneRoster.Clear();
                    foreach (object? o in list)
                    {
                        if (o is Dictionary<string, object?> d)
                            app.Session.SceneRoster.Add(d);
                    }
                }, _ => { });
            }
        }

        private void StartNewLife()
        {
            if (_hasSession)
            {
                App.Confirm("開始新生活", "這個裝置已有一個進行中的生活。開始新的生活會建立新的存檔，舊的仍可從「讀取紀錄」接回。", "開始新生活",
                    () => App.Show<LoginScreen>().ShowFromStart());
                return;
            }
            App.Show<LoginScreen>().ShowFromStart();
        }

        private void Continue()
        {
            App.Busy(true, "接回進行中的生活…");
            Api.AutoLogin(Settings.deviceUid, json =>
            {
                App.Busy(false);
                if (!json.GetBool("has_session"))
                {
                    App.Toast("沒有進行中的生活，請從「讀取紀錄」選擇存檔。", Theme.Warn);
                    return;
                }
                Settings.lastSessionId = Api.SessionId;
                Settings.Save();
                MainScreen.Enter(App, resumed: true);
            }, e => Fail(e, "auto_login"));
        }

        public override bool HandleEscape()
        {
            App.Show<LauncherScreen>();
            return true;
        }
    }

    /// <summary>03 登入 — 成年確認 first, then the opening poem, then the login choices.</summary>
    public sealed class LoginScreen : GameScreen
    {
        private RectTransform _age = null!;
        private RectTransform _poem = null!;
        private RectTransform _login = null!;
        private Text _poemText = null!;

        public override void Build()
        {
            Backdrop(0.45f);
            SystemButtons();

            // 成年確認
            _age = UIKit.Rect(Root, "AgeGate").Fill();
            Image ageBox = UIKit.Panel(_age, "Box", Theme.Panel);
            ageBox.rectTransform.Place(0.30f, 0.30f, 0.70f, 0.72f);
            Text h = UIKit.Label(ageBox.transform, "成年確認", 38, Theme.Text, TextAnchor.UpperCenter, FontStyle.Bold);
            h.rectTransform.Place(0, 0.78f, 1, 1).Inset(30, 30, 30, 0);
            Text body = UIKit.Label(ageBox.transform,
                "本作品含有成人向的戀愛與人際題材。\n本作品所有登場人物皆為 18 歲以上的成年人。\n\n請確認你已年滿 18 歲（或你所在地區的成年年齡）。",
                24, Theme.TextDim, TextAnchor.UpperCenter);
            body.rectTransform.Place(0, 0.30f, 1, 0.78f).Inset(40, 0, 40, 0);
            RectTransform ageRow = UIKit.Rect(ageBox.transform, "Row").Place(0, 0, 1, 0.26f).Inset(60, 0, 60, 36);
            UIKit.HBox(ageRow, 20, 0, TextAnchor.MiddleCenter, true);
            UIKit.Button(ageRow, "未滿 18 歲，離開", () => App.Show<TitleScreen>(), ButtonStyle.Ghost);
            UIKit.Button(ageRow, "我已年滿 18 歲", () =>
            {
                Settings.ageConfirmed = true;
                Settings.Save();
                ShowPoem();
            }, ButtonStyle.Primary);

            // 開場詩卡
            _poem = UIKit.Rect(Root, "Poem").Fill();
            Image poemBox = UIKit.Panel(_poem, "Card", new Color(0.05f, 0.08f, 0.16f, 0.80f));
            poemBox.rectTransform.Place(0.30f, 0.22f, 0.70f, 0.80f);
            _poemText = UIKit.Label(poemBox.transform, string.Empty, 30, Theme.Text, TextAnchor.MiddleCenter);
            _poemText.rectTransform.Fill().Inset(50, 50, 50, 90);
            _poemText.lineSpacing = 1.6f;
            Text skip = UIKit.Label(poemBox.transform, "輕觸以略過", 20, Theme.TextMuted, TextAnchor.LowerCenter);
            skip.rectTransform.Fill().Inset(0, 0, 0, 30);
            skip.gameObject.AddComponent<Pulse>().Speed = 2f;
            var poemButton = poemBox.gameObject.AddComponent<Button>();
            poemButton.transition = Selectable.Transition.None;
            poemButton.onClick.AddListener(ShowLogin);

            // 登入方式
            _login = UIKit.Rect(Root, "Login").Fill();
            Heading("タイトル › 開始新生活", "登入");
            Box(_login, "登入方式", 0.30f, 0.26f, 0.40f, 0.42f, out RectTransform ways);
            UIKit.VBox(ways, 14);
            Button member = UIKit.Button(ways, "會員帳號登入", () => App.Alert("會員登入", "會員登入需要 HTTPS 網域與 PF-001 OIDC 回呼，Unity 版尚未接上。\n目前請先用「先逛逛（匿名）」，進度會綁定這台裝置。"));
            member.Pref(64);
            Button google = UIKit.Button(ways, "Google 登入（尚未開放）", null);
            google.interactable = false;
            google.Pref(64);
            UIKit.Button(ways, "先逛逛（匿名）", Anonymous, ButtonStyle.Primary, 26).Pref(72);
            UIKit.FlowLabel(ways, "匿名進度綁定在這台裝置：" + Settings.deviceUid, 18, Theme.TextMuted);

            Box(_login, "裝置配對（Unity）", 0.72f, 0.26f, 0.22f, 0.42f, out RectTransform pair);
            UIKit.FlowLabel(pair, "在手機或網頁輸入配對碼，把會員進度帶到這台電腦。\n\n<color=#ffc766>伺服器尚未提供配對端點，此功能為未來項目。</color>", 20, Theme.TextDim);

            BackButton(() => App.Show<TitleScreen>());
        }

        public void ShowFromStart()
        {
            if (Settings.ageConfirmed)
                ShowPoem();
            else
                Only(_age);
        }

        public void ShowLoginOnly()
        {
            if (!Settings.ageConfirmed)
            {
                Only(_age);
                return;
            }
            ShowLogin();
        }

        private void Only(RectTransform which)
        {
            _age.gameObject.SetActive(which == _age);
            _poem.gameObject.SetActive(which == _poem);
            _login.gameObject.SetActive(which == _login);
        }

        private void ShowPoem()
        {
            Only(_poem);
            _poemText.text = "天空沒有盡頭。\n\n在雲層之上，有一座不屬於任何地圖的學園。\n\n你在鐘聲裡醒來——";
            OnboardingCache.Load(App, cfg =>
            {
                var lines = new List<string>();
                List<object?>? poem = cfg.GetList("poem");
                if (poem != null)
                {
                    foreach (object? o in poem)
                    {
                        if (o is Dictionary<string, object?> line)
                            lines.Add(line.GetString("type") == "gap" ? string.Empty : line.GetString("text"));
                    }
                }
                if (lines.Count > 0)
                    _poemText.text = string.Join("\n", lines);
            }, null);
        }

        private void ShowLogin() => Only(_login);

        private void Anonymous()
        {
            Settings.accountMode = "anon";
            Settings.Save();
            Session.Draft = new NewLifeDraft();
            App.Show<QuestionnaireScreen>();
        }

        public override bool HandleEscape()
        {
            App.Show<TitleScreen>();
            return true;
        }
    }

    /// <summary>Language picker (語 button / 設定). Changes the server output language when a game is running.</summary>
    public sealed class LanguageScreen : GameScreen
    {
        public override void Build()
        {
            Image shade = UIKit.Block(Root, "Shade", new Color(0, 0, 0, 0.5f), true);
            shade.rectTransform.Fill();
            Box(Root, "語言", 0.38f, 0.22f, 0.24f, 0.56f, out RectTransform body);
            UIKit.VBox(body, 10);
            for (int i = 0; i < GameSettings.LangCodes.Length; i++)
            {
                string code = GameSettings.LangCodes[i];
                UIKit.Button(body, GameSettings.LangNames[i], () => Pick(code)).Pref(58);
            }
            UIKit.FlowLabel(body, "介面目前只有繁體中文；切換後改變的是故事的輸出語言。", 17, Theme.TextMuted);
        }

        private void Pick(string code)
        {
            Settings.lang = code;
            Settings.Save();
            App.Pop();
            if (!Api.HasSession)
            {
                App.Toast("語言已設為 " + GameSettings.LangName(code) + "，開局後套用。");
                return;
            }
            App.Busy(true, "切換語言…");
            Api.Post("/api/lang", new Dictionary<string, object?> { ["lang"] = code }, true, reply =>
            {
                App.Busy(false);
                App.Toast("故事語言：" + GameSettings.LangName(code));
                if (reply is Dictionary<string, object?> json && json.GetObject("translated") is Dictionary<string, object?> tr)
                    App.Find<MainScreen>()?.ApplyTranslation(tr.GetString("narrative"), tr.GetStringList("options"));
            }, e => Fail(e, "lang"));
        }
    }

    /// <summary>GET /api/onboarding/config, fetched once.</summary>
    public static class OnboardingCache
    {
        private static Dictionary<string, object?>? _config;

        public static void Load(GameApp app, System.Action<Dictionary<string, object?>> onOk, System.Action<EdenApiError>? onError)
        {
            if (_config != null)
            {
                onOk(_config);
                return;
            }
            app.Api.GetObject("/api/onboarding/config", false, cfg =>
            {
                _config = cfg;
                onOk(cfg);
            }, onError);
        }
    }
}
