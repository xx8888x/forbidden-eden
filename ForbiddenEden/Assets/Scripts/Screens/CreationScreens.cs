#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ForbiddenEden.Core;
using ForbiddenEden.Net;
using ForbiddenEden.UI;
using ForbiddenEden.World;
using UnityEngine;
using UnityEngine.UI;

namespace ForbiddenEden.Screens
{
    /// <summary>04 登錄問卷 — four questions, three answers each; scoring happens on the server.</summary>
    public sealed class QuestionnaireScreen : GameScreen
    {
        private readonly List<(string question, List<(int seq, string text)> answers)> _questions = new List<(string, List<(int, string)>)>();
        private readonly List<int> _answers = new List<int>();
        private RectTransform _dots = null!;
        private Text _question = null!;
        private RectTransform _choices = null!;
        private Button _back = null!;

        public override void Build()
        {
            Heading("開始新生活 › 登錄問卷", "登錄問卷");
            Image hint = UIKit.Panel(Root, "Asker", Theme.PanelSoft);
            hint.rectTransform.PlaceTL(0.06f, 0.20f, 0.30f, 0.09f);
            Text asker = UIKit.Label(hint.transform, "新生登錄中心\n<size=18><color=#a8b8d8>「回答沒有對錯。照你心裡的第一個念頭就好。」</color></size>", 24, Theme.Gold, TextAnchor.MiddleLeft);
            asker.rectTransform.Fill().Inset(20, 6, 16, 6);

            _dots = UIKit.Rect(Root, "Dots").PlaceTL(0.52f, 0.20f, 0.42f, 0.05f);
            UIKit.HBox(_dots, 14, 0, TextAnchor.MiddleLeft, false, false);

            Box(Root, "", 0.52f, 0.26f, 0.42f, 0.18f, out RectTransform q);
            _question = UIKit.Label(q, "讀取題目中…", 30, Theme.Text, TextAnchor.MiddleLeft);
            _question.rectTransform.Fill();

            _choices = UIKit.Rect(Root, "Choices").PlaceTL(0.52f, 0.47f, 0.42f, 0.38f);
            UIKit.VBox(_choices, 14);

            _back = BackButton(Back, "‹ 上一步");
        }

        public override void OnShow()
        {
            App.Stage.Build(SetKind.Registry);
            App.Stage.HidePlayer();
            App.Stage.ClearCast();
            App.Stage.SetCast(new List<(string, string, string)> { ("registry_clerk", "許文玲", "女") }, "registry_clerk");
            App.Rig.SetSway(0.3f);
            App.Rig.Shot(new Vector3(0.9f, 1.35f, 0.2f), 3.2f, 8, 3, 34, true);
            _answers.Clear();
            _questions.Clear();
            OnboardingCache.Load(App, cfg =>
            {
                ParseQuestions(cfg);
                if (_questions.Count == 0)
                {
                    SkipBecause("伺服器沒有設定問卷。");
                    return;
                }
                ShowQuestion();
            }, e => SkipBecause(GameApp.ErrorTitle(e) + "：" + GameApp.ErrorBody(e)));
        }

        private void SkipBecause(string why)
        {
            _question.text = "問卷暫時無法讀取";
            UIKit.Clear(_choices);
            UIKit.FlowLabel(_choices, why, 20, Theme.TextDim);
            UIKit.Button(_choices, "略過問卷，直接取名", () =>
            {
                Session.Draft.Questionnaire.Clear();
                App.Show<ResultNameScreen>();
            }, ButtonStyle.Primary).Pref(64);
        }

        private void ParseQuestions(Dictionary<string, object?> cfg)
        {
            var byNo = new SortedDictionary<int, (string q, List<(int, string)> a)>();
            foreach (object? o in cfg.GetList("questions") ?? new List<object?>())
            {
                if (!(o is Dictionary<string, object?> row))
                    continue;
                int no = row.GetInt("no");
                if (!byNo.TryGetValue(no, out var entry))
                    entry = (row.GetString("question"), new List<(int, string)>());
                entry.a.Add((row.GetInt("seq"), row.GetString("text")));
                byNo[no] = entry;
            }
            foreach (var kv in byNo)
                _questions.Add((kv.Value.q, kv.Value.a.OrderBy(x => x.Item1).ToList()));
        }

        private void ShowQuestion()
        {
            int i = _answers.Count;
            UIKit.Clear(_dots);
            for (int d = 0; d < _questions.Count; d++)
            {
                Image dot = UIKit.Panel(_dots, "Dot", d < i ? Theme.Accent : d == i ? Theme.Gold : new Color(1, 1, 1, 0.2f), UIKit.Circle, false);
                dot.Pref(22, 22);
            }
            _back.ButtonText().text = i == 0 ? "‹ 返回" : "‹ 上一步";
            if (i >= _questions.Count)
            {
                Score();
                return;
            }
            _question.text = "<size=22><color=#6bbdff>第 " + (i + 1) + " 題</color></size>\n" + _questions[i].question;
            UIKit.Clear(_choices);
            char letter = 'A';
            foreach ((int seq, string text) in _questions[i].answers)
            {
                int pick = seq;
                Button b = UIKit.Button(_choices, letter++ + "　" + text, () =>
                {
                    _answers.Add(pick);
                    ShowQuestion();
                }, ButtonStyle.Normal, 24);
                b.ButtonText().alignment = TextAnchor.MiddleLeft;
                b.Pref(84);
            }
        }

        private void Score()
        {
            _question.text = "整理你的回答…";
            UIKit.Clear(_choices);
            Session.Draft.Questionnaire = new List<int>(_answers);
            App.Busy(true, "新生登錄中心正在看你的回答…");
            Api.Post("/api/onboarding/score", new Dictionary<string, object?> { ["questionnaire"] = _answers }, false, reply =>
            {
                App.Busy(false);
                var json = reply as Dictionary<string, object?>;
                NewLifeDraft d = Session.Draft;
                d.Mark = json.GetString("mark");
                d.MarkName = json.GetString("mark_name");
                d.MarkGlyph = json.GetString("mark_glyph");
                d.MarkEcho = json.GetString("mark_echo");
                App.Show<ResultNameScreen>();
            }, e =>
            {
                Fail(e, "onboarding/score");
                _answers.RemoveAt(_answers.Count - 1);
                ShowQuestion();
            });
        }

        private void Back()
        {
            if (_answers.Count == 0)
            {
                App.Show<TitleScreen>();
                return;
            }
            _answers.RemoveAt(_answers.Count - 1);
            ShowQuestion();
        }

        public override bool HandleEscape()
        {
            Back();
            return true;
        }
    }

    /// <summary>05 結果卡・取名 — the poetic mark, a name (with random + 擋詞), and gender.</summary>
    public sealed class ResultNameScreen : GameScreen
    {
        private Text _glyph = null!;
        private Text _markName = null!;
        private Text _echo = null!;
        private InputField _name = null!;
        private Button _male = null!;
        private Button _female = null!;
        private Text _nameNote = null!;
        private static List<string>? _blacklist;

        public override void Build()
        {
            Heading("登錄問卷 › 結果", "結果卡・取名");
            Image card = UIKit.Panel(Root, "Card", new Color(0.09f, 0.12f, 0.24f, 0.92f));
            card.rectTransform.PlaceTL(0.05f, 0.20f, 0.30f, 0.62f);
            Image ring = UIKit.Panel(card.transform, "Ring", new Color(0.98f, 0.81f, 0.47f, 0.18f), UIKit.Circle, false);
            ring.rectTransform.Place(0.5f, 0.62f, 0.5f, 0.62f);
            ring.rectTransform.sizeDelta = new Vector2(220, 220);
            _glyph = UIKit.Label(card.transform, "印", 120, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            _glyph.rectTransform.Place(0, 0.45f, 1, 0.80f);
            _markName = UIKit.Label(card.transform, "", 40, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            _markName.rectTransform.Place(0, 0.30f, 1, 0.44f);
            _echo = UIKit.Label(card.transform, "", 22, Theme.TextDim, TextAnchor.UpperCenter);
            _echo.rectTransform.Place(0, 0.04f, 1, 0.30f).Inset(36, 0, 36, 0);

            Box(Root, "取名", 0.38f, 0.20f, 0.36f, 0.22f, out RectTransform nameBody);
            RectTransform nameRow = UIKit.Rect(nameBody, "Row").Place(0, 0.45f, 1, 1);
            UIKit.HBox(nameRow, 12, 0, TextAnchor.MiddleLeft);
            _name = UIKit.Input(nameRow, "輸入名字（最多 12 字）", false, 28);
            _name.characterLimit = 12;
            _name.Pref(64, -1, 1);
            UIKit.Button(nameRow, "隨機", RandomName).Pref(64, 130);
            _nameNote = UIKit.Label(nameBody, "名字會出現在所有人的台詞裡。", 18, Theme.TextMuted, TextAnchor.UpperLeft);
            _nameNote.rectTransform.Place(0, 0, 1, 0.42f);

            Box(Root, "性別", 0.38f, 0.45f, 0.36f, 0.15f, out RectTransform genderBody);
            UIKit.HBox(genderBody, 14, 0, TextAnchor.MiddleLeft, true);
            _male = UIKit.Button(genderBody, "男", () => SetGender("男"), ButtonStyle.Normal, 26);
            _female = UIKit.Button(genderBody, "女", () => SetGender("女"), ButtonStyle.Normal, 26);

            Button avatar = UIKit.Button(Root, "調整外觀　›", () => { Save(); App.Show<AvatarScreen>(); }, ButtonStyle.Normal, 26);
            ((RectTransform)avatar.transform).PlaceTL(0.38f, 0.63f, 0.36f, 0.08f);

            Button next = UIKit.Button(Root, "下一步　›", Next, ButtonStyle.Primary, 28);
            ((RectTransform)next.transform).PlaceTL(0.78f, 0.86f, 0.16f, 0.08f);
            BackButton(() => App.Show<QuestionnaireScreen>(), "‹ 上一步");
        }

        public override void OnShow()
        {
            App.Stage.Build(SetKind.Registry);
            App.Stage.ClearCast();
            NewLifeDraft d = Session.Draft;
            App.Stage.ShowPlayer(d.Avatar, new Vector3(0.2f, 0, 0.6f));
            App.Rig.SetSway(0.2f);
            App.Rig.Shot(new Vector3(-1.25f, 0.95f, 0.6f), 3.6f, 0, 2, 34, true); // figure stands right of the panels
            _glyph.text = d.MarkGlyph.Length > 0 ? d.MarkGlyph : "新";
            _markName.text = d.MarkName.Length > 0 ? d.MarkName : "新生";
            _echo.text = d.MarkEcho.Length > 0 ? d.MarkEcho : "（沒有問卷結果：你的印記會在故事裡慢慢顯現。）";
            _name.text = d.PlayerName;
            SetGender(d.Gender, rebuildAvatar: false);
            if (_blacklist == null)
            {
                Api.GetObject("/api/solo/blacklist_names", false, json => _blacklist = json.GetStringList("names"), _ => _blacklist = new List<string>());
            }
        }

        private void SetGender(string g, bool rebuildAvatar = true)
        {
            NewLifeDraft d = Session.Draft;
            bool changed = d.Gender != g;
            d.Gender = g;
            _male.GetComponent<Image>().color = g == "男" ? new Color(0.24f, 0.50f, 0.86f, 0.96f) : Theme.PanelLight;
            _female.GetComponent<Image>().color = g == "女" ? new Color(0.24f, 0.50f, 0.86f, 0.96f) : Theme.PanelLight;
            if (changed && rebuildAvatar)
            {
                d.Avatar = AvatarData.Default(g == "男");
            }
            if (d.Avatar.male != (g == "男"))
                d.Avatar = AvatarData.Default(g == "男");
            App.Stage.ShowPlayer(d.Avatar, new Vector3(0.2f, 0, 0.6f));
        }

        private void RandomName()
        {
            string gender = Session.Draft.Gender == "女" ? "female" : "male";
            Api.GetObject("/api/solo/random_name?gender=" + gender + "&lang=zh-Hant", false, json =>
            {
                _name.text = json.GetString("name");
                _nameNote.text = "隨機名字，可以再改。";
            }, e => App.Toast(GameApp.ErrorTitle(e) + "：無法取得隨機名字", Theme.Warn));
        }

        private void Save() => Session.Draft.PlayerName = _name.text.Trim();

        private void Next()
        {
            Save();
            string name = Session.Draft.PlayerName;
            if (name.Length == 0)
            {
                _nameNote.text = "<color=#ff7a7a>請先取一個名字。</color>";
                return;
            }
            if (_blacklist != null)
            {
                foreach (string bad in _blacklist)
                {
                    if (bad.Length > 0 && (name.Contains(bad) || bad.Contains(name)))
                    {
                        _nameNote.text = "<color=#ff7a7a>這個名字和學園裡的人重複了，換一個吧。</color>";
                        return;
                    }
                }
            }
            App.Show<OriginScreen>();
        }

        public override bool HandleEscape()
        {
            App.Show<QuestionnaireScreen>();
            return true;
        }
    }

    /// <summary>06 起點選擇 — six origins; picking one previews its scene and first-meet NPC in 3D.</summary>
    public sealed class OriginScreen : GameScreen
    {
        private RectTransform _grid = null!;
        private Text _preview = null!;
        private Button _confirm = null!;
        private readonly List<Dictionary<string, object?>> _origins = new List<Dictionary<string, object?>>();
        private readonly List<Image> _cards = new List<Image>();
        private int _selected = -1;

        public override void Build()
        {
            Heading("開始新生活 › 起點", "你從何處醒來？");
            _grid = UIKit.Rect(Root, "Cards").PlaceTL(0.04f, 0.18f, 0.42f, 0.66f);
            UIKit.Grid(_grid, new Vector2(390, 220), new Vector2(18, 18), 2);

            Image prev = UIKit.Panel(Root, "Preview", Theme.Panel);
            prev.rectTransform.PlaceTL(0.62f, 0.62f, 0.34f, 0.22f);
            _preview = UIKit.Label(prev.transform, "選一張起點卡，先看看你會在哪裡醒來、先遇見誰。", 21, Theme.TextDim);
            _preview.rectTransform.Fill().Inset(22, 18, 22, 18);

            _confirm = UIKit.Button(Root, "就這樣", Confirm, ButtonStyle.Primary, 30);
            ((RectTransform)_confirm.transform).PlaceTL(0.78f, 0.86f, 0.18f, 0.08f);
            BackButton(() => App.Show<ResultNameScreen>(), "‹ 上一步");
        }

        public override void OnShow()
        {
            _confirm.interactable = false;
            _selected = -1;
            App.Stage.HidePlayer();
            App.Stage.ClearCast();
            App.Stage.Build(SetKind.Campus);
            App.Rig.Shot(new Vector3(0.9f, 1.5f, 1.5f), 6.5f, 0, 6, 36, true);
            // First-meet NPCs are ids; the roster turns them into names.
            TitleScreen.LoadRosters(App, () => { if (_origins.Count > 0) Render(); });
            if (_origins.Count > 0)
            {
                Render();
                return;
            }
            App.Busy(true, "讀取起點…");
            Api.GetList("/api/solo/professions", false, list =>
            {
                App.Busy(false);
                foreach (object? o in list)
                {
                    if (o is Dictionary<string, object?> d)
                        _origins.Add(d);
                }
                Render();
            }, e =>
            {
                Fail(e, "professions");
                _preview.text = "<color=#ff7a7a>起點清單讀取失敗。</color>可以按「就這樣」用伺服器預設起點開局。";
                _confirm.interactable = true;
            });
        }

        private void Render()
        {
            UIKit.Clear(_grid);
            _cards.Clear();
            for (int i = 0; i < _origins.Count; i++)
            {
                int index = i;
                Dictionary<string, object?> o = _origins[i];
                Image card = UIKit.Panel(_grid, "Origin", Theme.PanelSoft);
                var b = card.gameObject.AddComponent<Button>();
                b.onClick.AddListener(() => Select(index));
                // The server's icon is an emoji the OS font cannot draw; use the origin's first character as a seal.
                string originName = o.GetString("name");
                Image seal = UIKit.Panel(card.transform, "Seal", new Color(0.98f, 0.81f, 0.47f, 0.18f), UIKit.Circle, false);
                seal.type = Image.Type.Simple;
                seal.rectTransform.Place(0, 1, 0, 1);
                seal.rectTransform.sizeDelta = new Vector2(54, 54);
                seal.rectTransform.anchoredPosition = new Vector2(46, -46);
                Text icon = UIKit.Label(seal.transform, originName.Length > 0 ? originName.Substring(0, 1) : "?", 30, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                icon.rectTransform.Fill();
                Text name = UIKit.Label(card.transform, o.GetString("name"), 28, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
                name.rectTransform.Fill().Inset(84, 22, 16, 0);
                string npc = o.GetString("npc");
                string detail = "地點　" + o.GetString("scene_name", o.GetString("scene")) +
                                (npc.Length > 0 ? "\n初遇　" + Session.NpcName(npc) : string.Empty) +
                                (o.GetString("event_name").Length > 0 ? "\n事件　" + o.GetString("event_name") : string.Empty);
                Text t = UIKit.Label(card.transform, detail, 19, Theme.TextDim, TextAnchor.UpperLeft);
                t.rectTransform.Fill().Inset(22, 84, 16, 10);
                _cards.Add(card);
            }
        }

        private void Select(int index)
        {
            _selected = index;
            for (int i = 0; i < _cards.Count; i++)
                _cards[i].color = i == index ? new Color(0.20f, 0.36f, 0.62f, 0.95f) : Theme.PanelSoft;
            Dictionary<string, object?> o = _origins[index];
            string npc = o.GetString("npc");
            string npcName = Session.NpcName(npc);
            _preview.text = "<b><color=#ffd27a>" + o.GetString("name") + "</color></b>\n" +
                            o.GetString("scene_name", o.GetString("scene")) +
                            (npc.Length > 0 ? "，初遇 " + npcName + (o.GetString("npc_role").Length > 0 ? "（" + o.GetString("npc_role") + "）" : "") : "") +
                            (o.GetString("event_desc").Length > 0 ? "\n<size=18>" + o.GetString("event_desc") + "</size>" : "");
            _confirm.interactable = true;

            // 起點預覽：場景與初遇的人（3D）。
            App.Stage.Build(Stage.KindFor(o.GetString("scene"), o.GetString("scene_name")));
            App.Stage.ClearCast();
            if (npc.Length > 0)
            {
                Dictionary<string, object?>? info = Session.FindNpc(npc);
                App.Stage.SetCast(new List<(string, string, string)> { (npc, npcName, info.GetString("gender")) }, npc);
            }
            App.Rig.Shot(new Vector3(0.9f, 1.25f, 0.2f), 3.4f, 10, 3, 34);
        }

        private void Confirm()
        {
            NewLifeDraft d = Session.Draft;
            if (_selected >= 0)
            {
                Dictionary<string, object?> o = _origins[_selected];
                d.ProfessionId = o.GetString("id");
                d.ProfessionName = o.GetString("name");
                d.StartScene = o.GetString("scene");
                d.FirstNpc = Session.NpcName(o.GetString("npc"));
            }
            App.Busy(true, "正在建立你的新生活…\n<size=18>開場敘事由伺服器生成，可能需要一點時間</size>");
            Api.Init(d.ToInitBody(Settings.deviceUid), result =>
            {
                App.Busy(false);
                Settings.lastSessionId = result.SessionId;
                Settings.Save();
                AvatarCard.SaveCurrent(d.Avatar, d.PlayerName);
                Session.ResetForNewGame();
                Session.LastNarrative = NarrativeText.Clean(result.InitialEvent);
                Session.Options = result.InitialOptions;
                if (Settings.lang != "zh-TW")
                    Api.Post("/api/lang", new Dictionary<string, object?> { ["lang"] = Settings.lang }, true, _ => { }, _ => { });
                MainScreen.Enter(App, resumed: false);
            }, e => Fail(e, "init"));
        }

        public override bool HandleEscape()
        {
            App.Show<ResultNameScreen>();
            return true;
        }
    }

    /// <summary>07 外觀自訂 — HS2 キャラメイク layout: 5 categories, item list, floating sliders, 360° preview.</summary>
    public sealed class AvatarScreen : GameScreen
    {
        private static readonly string[] Categories = { "臉", "身體", "髮型", "服裝", "配件" };
        private RectTransform _panel = null!;
        private Text _panelTitle = null!;
        private readonly List<Button> _tabs = new List<Button>();
        private AvatarData _work = AvatarData.Default(true);
        private Text _name = null!;
        private int _bg;
        private int _light;

        public override void Build()
        {
            Heading("結果卡 › 調整外觀", "外觀自訂");
            Image warn = UIKit.Panel(Root, "Rule", new Color(0.35f, 0.26f, 0.08f, 0.85f), UIKit.RoundedSmall);
            warn.rectTransform.PlaceTL(0.03f, 0.155f, 0.30f, 0.045f);
            Text w = UIKit.Label(warn.transform, "滑桿已設成年下限，無法調出未成年外觀", 18, Theme.Gold, TextAnchor.MiddleCenter);
            w.rectTransform.Fill();

            Image nameBox = UIKit.Panel(Root, "Name", Theme.Panel);
            nameBox.rectTransform.PlaceTL(0.03f, 0.215f, 0.22f, 0.06f);
            _name = UIKit.Label(nameBox.transform, "", 24, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            _name.rectTransform.Fill().Inset(18, 0, 10, 0);

            RectTransform tabs = UIKit.Rect(Root, "Tabs").PlaceTL(0.03f, 0.285f, 0.22f, 0.06f);
            UIKit.HBox(tabs, 6, 0, TextAnchor.MiddleLeft, true);
            for (int i = 0; i < Categories.Length; i++)
            {
                int c = i;
                _tabs.Add(UIKit.Button(tabs, Categories[i], () => ShowCategory(c), ButtonStyle.Normal, 22));
            }

            Image panel = UIKit.Panel(Root, "Adjust", Theme.Panel);
            panel.rectTransform.PlaceTL(0.03f, 0.355f, 0.30f, 0.50f);
            _panelTitle = UIKit.Label(panel.transform, "", 22, Theme.Gold, TextAnchor.UpperLeft, FontStyle.Bold);
            _panelTitle.rectTransform.Place(0, 1, 1, 1);
            _panelTitle.rectTransform.offsetMin = new Vector2(18, -46);
            _panelTitle.rectTransform.offsetMax = new Vector2(-18, -12);
            UIKit.ScrollList(panel.transform, out _panel, 6, 4);
            ((RectTransform)_panel.parent.parent).Fill().Inset(14, 52, 14, 14);

            Box(Root, "預覽工具", 0.78f, 0.20f, 0.19f, 0.36f, out RectTransform tools);
            UIKit.VBox(tools, 8);
            UIKit.Button(tools, "姿勢：站立", () => App.Toast("動作資源尚未製作，暫時只有站姿。")).Pref(48);
            UIKit.Button(tools, "燈光切換", () =>
            {
                _light = (_light + 1) % 3;
                Color[] c = { new Color(1f, 0.96f, 0.9f), new Color(1f, 0.82f, 0.62f), new Color(0.75f, 0.85f, 1f) };
                App.Stage.SetSunTint(c[_light], 1.05f);
            }).Pref(48);
            UIKit.Button(tools, "背景切換", () =>
            {
                SetKind[] kinds = { SetKind.Registry, SetKind.Campus, SetKind.Classroom, SetKind.Rooftop };
                _bg = (_bg + 1) % kinds.Length;
                App.Stage.Build(kinds[_bg]);
            }).Pref(48);
            UIKit.Button(tools, Stage.HasHs2Parts ? "換回制服" : "制服／便服", () =>
            {
                if (Stage.HasHs2Parts)
                    _work.look = Hs2Looks.Uniform(!_work.male);
                else
                    _work.outfit = 1 - _work.outfit;
                ApplyWork();
                ShowCategory(_category);
            }).Pref(48);
            UIKit.Button(tools, "鏡頭重設", ResetCamera).Pref(48);

            Box(Root, "外觀卡", 0.78f, 0.59f, 0.19f, 0.20f, out RectTransform card);
            UIKit.VBox(card, 8);
            UIKit.Button(card, "存外觀卡", SaveCard).Pref(48);
            UIKit.Button(card, "讀外觀卡", LoadCard).Pref(48);

            Text help = UIKit.Label(Root, "右鍵拖曳旋轉　滾輪縮放　中鍵平移", 18, Theme.TextMuted, TextAnchor.LowerCenter);
            help.rectTransform.PlaceTL(0.35f, 0.92f, 0.40f, 0.04f);

            Button done = UIKit.Button(Root, "完成", Done, ButtonStyle.Primary, 28);
            ((RectTransform)done.transform).PlaceTL(0.78f, 0.86f, 0.19f, 0.08f);
            BackButton(Cancel, "‹ 放棄調整");
        }

        public override void OnShow()
        {
            _work = Session.Draft.Avatar.Clone();
            _name.text = Session.Draft.PlayerName.Length > 0 ? Session.Draft.PlayerName : "（未命名）";
            App.Stage.ClearCast();
            App.Stage.Build(SetKind.Registry);
            ApplyWork();
            ResetCamera();
            App.Rig.FreeControl = true;
            App.Rig.SetSway(0);
            ShowCategory(0);
        }

        public override void OnHide()
        {
            App.Rig.FreeControl = false;
        }

        private void ResetCamera()
        {
            App.Stage.ShowPlayer(_work, Vector3.zero);
            App.Rig.Shot(new Vector3(0.35f, App.Stage.PlayerEyeHeight * 0.62f, 0), 3.2f, 0, 4, 34, true);
        }

        private void ApplyWork()
        {
            _work.Clamp();
            App.Stage.ShowPlayer(_work, Vector3.zero);
        }

        private void ShowCategory(int c)
        {
            for (int i = 0; i < _tabs.Count; i++)
                _tabs[i].GetComponent<Image>().color = i == c ? new Color(0.24f, 0.50f, 0.86f, 0.96f) : Theme.PanelLight;
            _panelTitle.text = "▍" + Categories[c];
            UIKit.Clear(_panel);
            _category = c;
            if (Stage.HasHs2Parts)
            {
                ShowHs2Category(c);
                return;
            }
            switch (c)
            {
                case 0:
                    SliderRow("臉寬", 0.92f, 1.08f, _work.faceWidth, v => _work.faceWidth = v);
                    SliderRow("下巴", 0.92f, 1.08f, _work.jaw, v => _work.jaw = v);
                    SliderRow("頭部大小", AvatarData.HeadMin, AvatarData.HeadMax, _work.head, v => _work.head = v, "成年頭身比上限");
                    SliderRow("膚色", 0, 1, _work.skin, v => _work.skin = v);
                    SliderRow("瞳色", 0, 1, _work.eyeHue, v => _work.eyeHue = v);
                    break;
                case 1:
                    SliderRow("身高", AvatarData.HeightMin, AvatarData.HeightMax, _work.height, v => _work.height = v, "成年身高下限");
                    SliderRow("體格", AvatarData.BuildMin, AvatarData.BuildMax, _work.build, v => _work.build = v);
                    SliderRow("肩寬", AvatarData.ShoulderMin, AvatarData.ShoulderMax, _work.shoulder, v => _work.shoulder = v);
                    break;
                case 2:
                    ChoiceRow("樣式", AvatarData.HairStyleNames, _work.hairStyle, v => _work.hairStyle = v);
                    SliderRow("髮色", 0, 1, _work.hairHue, v => _work.hairHue = v);
                    SliderRow("明暗", 0, 1, _work.hairValue, v => _work.hairValue = v);
                    break;
                case 3:
                    ChoiceRow("款式", AvatarData.OutfitNames, _work.outfit, v => _work.outfit = v);
                    SliderRow("主色", 0, 1, _work.outfitHue, v => _work.outfitHue = v);
                    break;
                case 4:
                    ChoiceRow("配件", AvatarData.AccessoryNames, _work.accessory, v => _work.accessory = v);
                    break;
            }
        }

        // ---- HS2 parts (when the catalogue was exported) -------------------------------------------

        private static readonly (string slot, string label)[] HairSlots = { ("hair_b", "後髮"), ("hair_f", "前髮"), ("hair_s", "側髮"), ("hair_o", "附加髮") };
        private static readonly (string slot, string label)[] ClothSlots = { ("top", "上衣"), ("bot", "下身"), ("socks", "襪子"), ("shoes", "鞋子"), ("gloves", "手套"), ("panst", "褲襪") };
        private static readonly (string slot, string label)[] AccSlots =
        {
            ("acc_head", "頭"), ("acc_ear", "耳"), ("acc_glasses", "眼鏡"), ("acc_face", "臉"), ("acc_neck", "頸"), ("acc_shoulder", "肩"),
            ("acc_waist", "腰"), ("acc_back", "背"), ("acc_arm", "手臂"), ("acc_hand", "手"), ("acc_leg", "腿"),
        };

        private static readonly Color[] Swatches =
        {
            new Color(0.96f, 0.96f, 0.97f), new Color(0.75f, 0.76f, 0.79f), new Color(0.45f, 0.46f, 0.50f), new Color(0.16f, 0.16f, 0.18f),
            new Color(0.13f, 0.17f, 0.33f), new Color(0.22f, 0.36f, 0.72f), new Color(0.50f, 0.70f, 0.90f), new Color(0.16f, 0.50f, 0.50f),
            new Color(0.25f, 0.45f, 0.25f), new Color(0.82f, 0.74f, 0.58f), new Color(0.42f, 0.28f, 0.18f), new Color(0.45f, 0.12f, 0.18f),
            new Color(0.72f, 0.12f, 0.16f), new Color(0.95f, 0.66f, 0.74f), new Color(0.70f, 0.62f, 0.86f), new Color(0.95f, 0.85f, 0.40f),
        };

        private static readonly (string key, string label, int from, int to)[] FaceGroups =
        {
            ("shape", "輪廓", 0, 18), ("eye", "眼", 19, 31), ("nose", "鼻", 32, 46), ("mouth", "口", 47, 53), ("ear", "耳", 54, 58),
        };

        private int _category;
        private readonly string[] _subSlot = { "shape", "", "hair_b", "top", "acc_head" };

        private void ShowHs2Category(int c)
        {
            switch (c)
            {
                case 0:
                {
                    SubTabs(FaceGroups.Select(g => (g.key, g.label)).ToArray(), c);
                    if (_subSlot[c] == "")
                        _subSlot[c] = FaceGroups[0].key;
                    (string key, string label, int from, int to) group = FaceGroups.First(g => g.key == _subSlot[c]);
                    if (group.key == "eye")
                        SliderRow("瞳色", 0, 1, _work.eyeHue, v => _work.eyeHue = v);
                    for (int i = group.from; i <= group.to; i++)
                    {
                        int index = i;
                        // Eye width / height keep an adult ceiling (oversized eyes read as child-like).
                        float max = index == 22 || index == 23 ? 0.70f : 1f;
                        SliderRow(Hs2FaceShape.Names[index], 0, max, Mathf.Min(_work.look.Face[index], max), v => _work.look.Face[index] = v,
                            index == 22 ? "成年比例上限" : null);
                    }
                    break;
                }
                case 1:
                    SliderRow("身高", AvatarData.HeightMin, AvatarData.HeightMax, _work.height, v => _work.height = v, "成年身高下限");
                    break;
                case 2:
                    SubTabs(HairSlots, c);
                    SliderRow("髮色", 0, 1, _work.hairHue, v => _work.hairHue = v);
                    SliderRow("明暗", 0, 1, _work.hairValue, v => _work.hairValue = v);
                    ItemGrid(_subSlot[c]);
                    break;
                case 3:
                    SubTabs(ClothSlots, c);
                    ColorRows(_subSlot[c]);
                    ItemGrid(_subSlot[c]);
                    break;
                case 4:
                    SubTabs(AccSlots, c);
                    ColorRows(_subSlot[c]);
                    ItemGrid(_subSlot[c]);
                    break;
            }
        }

        private void SubTabs((string slot, string label)[] slots, int category)
        {
            RectTransform row = UIKit.Rect(_panel, "SubTabs");
            UIKit.Grid(row, new Vector2(84, 44), new Vector2(6, 6), 6);
            foreach ((string slot, string label) in slots)
            {
                string s = slot;
                Button b = UIKit.Button(row, label, () =>
                {
                    _subSlot[category] = s;
                    ShowCategory(category);
                }, ButtonStyle.Normal, 19);
                b.GetComponent<Image>().color = s == _subSlot[category] ? new Color(0.24f, 0.50f, 0.86f, 0.96f) : Theme.PanelLight;
            }
        }

        /// <summary>A part may be left empty unless that would leave the figure undressed.</summary>
        private bool MayBeEmpty(string slot)
        {
            if (slot == "bot")
            {
                Hs2Avatar? avatar = App.Stage.Player != null ? App.Stage.Player.GetComponent<Hs2Avatar>() : null;
                Hs2Part? top = avatar != null ? avatar.PartIn("top") : null;
                return top != null && top.FullBody;
            }
            return !(slot == "top" && !_work.male);
        }

        private void ItemGrid(string slot)
        {
            List<Hs2CatalogItem> items = Hs2Catalog.Load().For(slot, !_work.male);
            RectTransform grid = UIKit.Rect(_panel, "Items");
            UIKit.Grid(grid, new Vector2(98, 124), new Vector2(6, 6), 5);
            int current = _work.look.Get(slot);
            if (MayBeEmpty(slot))
                ItemCell(grid, null, "無", current < 0, () => Choose(slot, -1));
            foreach (Hs2CatalogItem item in items)
            {
                Hs2CatalogItem it = item;
                ItemCell(grid, item.Thumb.Length > 0 ? Resources.Load<Texture2D>(item.Thumb) : null, item.Name, item.Id == current, () => Choose(slot, it.Id));
            }
        }

        private void ItemCell(RectTransform parent, Texture2D? thumb, string name, bool selected, UnityEngine.Events.UnityAction onClick)
        {
            Image bg = UIKit.Panel(parent, name, selected ? new Color(0.24f, 0.50f, 0.86f, 0.96f) : Theme.PanelLight, UIKit.RoundedSmall);
            Button b = bg.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            b.onClick.AddListener(onClick);
            if (thumb != null)
            {
                var raw = new GameObject("Thumb", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                raw.transform.SetParent(bg.transform, false);
                raw.texture = thumb;
                raw.raycastTarget = false;
                raw.rectTransform.Place(0.06f, 0.26f, 0.94f, 0.96f);
            }
            Text label = UIKit.Label(bg.transform, name, 15, Theme.Text, thumb != null ? TextAnchor.LowerCenter : TextAnchor.MiddleCenter);
            label.rectTransform.Fill().Inset(4, 2, 4, 4);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.raycastTarget = false;
        }

        private void Choose(string slot, int id)
        {
            _work.look.Set(slot, id);
            _work.look.SetColors(slot, null); // a new part starts from its own HS2 colours
            ApplyWork();
            ShowCategory(_category);
        }

        /// <summary>Swatch rows for each colour the worn part lets the player change (HS2 colours 1–3).</summary>
        private void ColorRows(string slot)
        {
            Hs2Avatar? avatar = App.Stage.Player != null ? App.Stage.Player.GetComponent<Hs2Avatar>() : null;
            Hs2Part? part = avatar != null ? avatar.PartIn(slot) : null;
            if (part == null)
                return;
            for (int i = 0; i < 3; i++)
            {
                if (part.UseColor == null || i >= part.UseColor.Length || !part.UseColor[i])
                    continue;
                int index = i;
                UIKit.FlowLabel(_panel, "顏色 " + (i + 1), 19, Theme.TextDim);
                RectTransform row = UIKit.Rect(_panel, "Swatches" + i);
                UIKit.Grid(row, new Vector2(30, 30), new Vector2(4, 4), 16);
                foreach (Color c in Swatches)
                {
                    Color col = c;
                    Image sw = UIKit.Panel(row, "Swatch", col, UIKit.RoundedSmall);
                    Button b = sw.gameObject.AddComponent<Button>();
                    b.targetGraphic = sw;
                    b.onClick.AddListener(() =>
                    {
                        Color[] colors = (Color[])(_work.look.ColorsFor(slot) ?? part.DefaultColors).Clone();
                        colors[index] = col;
                        _work.look.SetColors(slot, colors);
                        ApplyWork();
                    });
                }
            }
        }

        /// <summary>Screenshot tour: opens one category tab.</summary>
        public void DebugShowCategory(int c) => ShowCategory(c);

        private void SliderRow(string label, float min, float max, float value, Action<float> set, string? note = null)
        {
            RectTransform row = UIKit.Rect(_panel, label);
            row.Pref(note == null ? 62 : 80);
            Text l = UIKit.Label(row, label + (note != null ? "\n<size=15><color=#ffc766>" + note + "</color></size>" : ""), 21, Theme.TextDim, TextAnchor.MiddleLeft);
            l.rectTransform.Place(0, 0, 0.30f, 1);
            Text num = UIKit.Label(row, "", 18, Theme.Text, TextAnchor.MiddleRight);
            num.rectTransform.Place(0.84f, 0, 1, 1);
            Slider s = UIKit.Slider(row, min, max, value, null);
            ((RectTransform)s.transform).Place(0.31f, 0.2f, 0.83f, 0.8f);
            num.text = Mathf.RoundToInt(Mathf.InverseLerp(min, max, value) * 100).ToString();
            s.onValueChanged.AddListener(v =>
            {
                set(v);
                num.text = Mathf.RoundToInt(Mathf.InverseLerp(min, max, v) * 100).ToString();
                ApplyWork();
            });
        }

        private void ChoiceRow(string label, string[] names, int value, Action<int> set)
        {
            UIKit.FlowLabel(_panel, label, 21, Theme.TextDim);
            RectTransform row = UIKit.Rect(_panel, label + "Choices");
            row.Pref(56);
            UIKit.HBox(row, 6, 0, TextAnchor.MiddleLeft, true);
            var buttons = new List<Button>();
            for (int i = 0; i < names.Length; i++)
            {
                int v = i;
                buttons.Add(UIKit.Button(row, names[i], () =>
                {
                    set(v);
                    for (int j = 0; j < buttons.Count; j++)
                        buttons[j].GetComponent<Image>().color = j == v ? new Color(0.24f, 0.50f, 0.86f, 0.96f) : Theme.PanelLight;
                    ApplyWork();
                }, ButtonStyle.Normal, 20));
            }
            for (int j = 0; j < buttons.Count; j++)
                buttons[j].GetComponent<Image>().color = j == value ? new Color(0.24f, 0.50f, 0.86f, 0.96f) : Theme.PanelLight;
        }

        private void SaveCard()
        {
            string file = AvatarCard.Save(_work, Session.Draft.PlayerName, App);
            App.Toast("已存外觀卡：" + Path.GetFileName(file), Theme.Ok);
        }

        private void LoadCard()
        {
            AvatarData? latest = AvatarCard.LoadLatest(out string file);
            if (latest == null)
            {
                App.Alert("讀外觀卡", "紀錄資料夾的 cards 裡還沒有外觀卡。\n" + GameSettings.CardsDir);
                return;
            }
            _work = latest;
            _work.male = Session.Draft.Gender == "男";
            ApplyWork();
            App.Toast("已讀取：" + Path.GetFileName(file), Theme.Ok);
        }

        private void Done()
        {
            Session.Draft.Avatar = _work.Clone();
            App.Show<ResultNameScreen>();
        }

        private void Cancel()
        {
            App.Confirm("放棄調整", "未存的調整會放棄。", "放棄", () => App.Show<ResultNameScreen>());
        }

        public override bool HandleEscape()
        {
            Cancel();
            return true;
        }
    }

    /// <summary>
    /// 外觀卡：a PNG portrait with the appearance JSON appended after the PNG IEND chunk,
    /// so image viewers still show the picture and the game can read the data back.
    /// </summary>
    public static class AvatarCard
    {
        private const string Marker = "\n#EDEN-CARD-V1#";

        public static string Save(AvatarData data, string playerName, GameApp app)
        {
            string file = Path.Combine(GameSettings.CardsDir, "card_" + Safe(playerName) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            app.StartCoroutine(Capture(file, data, playerName));
            return file;
        }

        /// <summary>Remembers the look used for a new game (no image, just the data).</summary>
        public static void SaveCurrent(AvatarData data, string playerName)
        {
            try
            {
                File.WriteAllText(Path.Combine(GameSettings.CardsDir, "current.json"), JsonUtility.ToJson(data, true));
            }
            catch (IOException)
            {
            }
        }

        public static AvatarData? LoadCurrent()
        {
            try
            {
                string path = Path.Combine(GameSettings.CardsDir, "current.json");
                return File.Exists(path) ? JsonUtility.FromJson<AvatarData>(File.ReadAllText(path)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static System.Collections.IEnumerator Capture(string file, AvatarData data, string playerName)
        {
            yield return new WaitForEndOfFrame();
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            int size = Mathf.Min(shot.width, shot.height);
            var crop = new Texture2D(size * 3 / 4, size, TextureFormat.RGB24, false);
            int x0 = (shot.width - crop.width) / 2;
            crop.SetPixels(shot.GetPixels(x0, (shot.height - size) / 2, crop.width, size));
            crop.Apply();
            byte[] png = crop.EncodeToPNG();
            UnityEngine.Object.Destroy(shot);
            UnityEngine.Object.Destroy(crop);
            byte[] payload = Encoding.UTF8.GetBytes(Marker + JsonUtility.ToJson(data));
            using (var fs = new FileStream(file, FileMode.Create))
            {
                fs.Write(png, 0, png.Length);
                fs.Write(payload, 0, payload.Length);
            }
        }

        public static AvatarData? LoadLatest(out string file)
        {
            file = string.Empty;
            var dir = new DirectoryInfo(GameSettings.CardsDir);
            FileInfo? latest = dir.GetFiles("card_*.png").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (latest == null)
                return null;
            file = latest.FullName;
            string text = Encoding.UTF8.GetString(File.ReadAllBytes(file));
            int i = text.LastIndexOf(Marker, StringComparison.Ordinal);
            if (i < 0)
                return null;
            try
            {
                return JsonUtility.FromJson<AvatarData>(text.Substring(i + Marker.Length));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "player";
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }
    }
}
