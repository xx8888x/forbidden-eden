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
    /// <summary>09 移動・校園地圖 — HS2 館内を回る layout; icons show what people are doing, closed places say 需要許可.</summary>
    public sealed class CampusMapScreen : GameScreen
    {
        public override bool HidesBelow => true;

        private RectTransform _list = null!;
        private Text _sceneName = null!;
        private Text _sceneDesc = null!;
        private RectTransform _people = null!;
        private Button _go = null!;
        private string _selected = string.Empty;
        private readonly HashSet<string> _movable = new HashSet<string>();

        public override void Build()
        {
            Image shade = UIKit.Block(Root, "Shade", new Color(0.02f, 0.04f, 0.09f, 0.35f), true);
            shade.rectTransform.Fill();
            Heading("遊戲中 › 移動", "移動・校園地圖");
            Box(Root, "地點清單", 0.03f, 0.17f, 0.25f, 0.66f, out RectTransform listBody);
            UIKit.ScrollList(listBody, out _list, 6, 0);
            ((RectTransform)_list.parent.parent).Fill();

            Image name = UIKit.Panel(Root, "SceneName", new Color(0.04f, 0.06f, 0.13f, 0.75f));
            name.rectTransform.PlaceTL(0.58f, 0.70f, 0.39f, 0.12f);
            _sceneName = UIKit.Label(name.transform, "", 46, Theme.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            _sceneName.rectTransform.Fill().Inset(20, 0, 26, 0);

            Box(Root, "在場的人", 0.72f, 0.17f, 0.25f, 0.30f, out _people);
            UIKit.VBox(_people, 8);

            Box(Root, "傳聞", 0.31f, 0.86f, 0.40f, 0.10f, out RectTransform rumor);
            _sceneDesc = UIKit.Label(rumor, "", 19, Theme.TextDim);
            _sceneDesc.rectTransform.Fill();

            _go = UIKit.Button(Root, "前往", Go, ButtonStyle.Primary, 30);
            ((RectTransform)_go.transform).PlaceTL(0.75f, 0.86f, 0.22f, 0.10f);
            CloseButton();
            BackButton(() => App.Pop());
        }

        public override void OnShow()
        {
            _selected = Session.SceneId;
            App.Stage.SetCastVisible(false);
            Render();
            Api.GetObject("/api/scene/movable", true, json =>
            {
                _movable.Clear();
                foreach (object? o in json.GetList("movable") ?? new List<object?>())
                {
                    if (o is Dictionary<string, object?> m)
                        _movable.Add(m.GetString("scene_id"));
                }
                Render();
            }, e => App.Toast("無法取得可前往地點：" + GameApp.ErrorTitle(e), Theme.Warn));
        }

        private void Render()
        {
            UIKit.Clear(_list);
            List<Dictionary<string, object?>> scenes = Session.SceneRoster;
            if (scenes.Count == 0)
            {
                UIKit.FlowLabel(_list, "場景清單讀取中…", 20, Theme.TextMuted);
                return;
            }
            foreach (Dictionary<string, object?> s in scenes)
            {
                string id = s.GetString("scene_id");
                bool here = id == Session.SceneId;
                bool reachable = here || _movable.Count == 0 || _movable.Contains(id);
                int people = s.GetStringList("active_npcs").Count;
                string icon = here ? "◆" : people >= 2 ? "●" : people == 1 ? "◐" : "○";
                string label = icon + "　" + s.GetString("name", id) + (here ? "　<size=16><color=#ffd27a>目前位置</color></size>" : "") +
                               (!reachable ? "　<size=16><color=#8a95ab>（需經由其他地點）</color></size>" : "");
                Button b = UIKit.Button(_list, label, () => Select(id), ButtonStyle.Ghost, 21);
                b.ButtonText().alignment = TextAnchor.MiddleLeft;
                b.ButtonText().color = reachable ? Theme.Text : Theme.TextMuted;
                b.GetComponent<Image>().color = id == _selected ? new Color(0.24f, 0.50f, 0.86f, 0.55f) : new Color(1, 1, 1, 0.04f);
                b.Pref(52);
            }
            UIKit.FlowLabel(_list, "◆ 目前位置　● 有人交談　◐ 一人　○ 無人", 16, Theme.TextMuted);
            Select(_selected, rebuildList: false);
        }

        private void Select(string id, bool rebuildList = true)
        {
            _selected = id;
            Dictionary<string, object?>? s = Session.FindScene(id);
            _sceneName.text = s.GetString("name", id);
            _sceneDesc.text = s.GetString("normal_description", "這裡最近沒有什麼傳聞。");
            UIKit.Clear(_people);
            List<string> npcs = s.GetStringList("active_npcs");
            if (npcs.Count == 0)
                UIKit.FlowLabel(_people, "現在沒有人。", 20, Theme.TextMuted);
            foreach (string npc in npcs)
                UIKit.FlowLabel(_people, "<b>" + Session.NpcName(npc) + "</b>　<color=#a8b8d8>在這裡</color>", 21, Theme.Text);
            bool here = id == Session.SceneId;
            _go.interactable = !here && (_movable.Count == 0 || _movable.Contains(id));
            _go.ButtonText().text = here ? "你在這裡" : _go.interactable ? "前往" : "無法直達";
            App.Stage.Build(Stage.KindFor(id, s.GetString("name")));
            App.Rig.Shot(new Vector3(0, 1.8f, 4), 9f, 0, 8, 40);
            if (rebuildList)
                Render();
        }

        private void Go()
        {
            string target = _selected;
            App.Busy(true, "移動中…");
            Api.Post("/api/scene/move", new Dictionary<string, object?> { ["scene_id"] = target }, true, reply =>
            {
                App.Busy(false);
                var json = reply as Dictionary<string, object?>;
                App.Toast(json.GetString("message", "已移動"), Theme.Ok);
                Session.State["current_scene"] = json.GetString("scene", target);
                Session.State["scene_name"] = json.GetString("scene_name", target);
                Session.NotifyChanged();
                App.Pop();
            }, e =>
            {
                App.Busy(false);
                if (e.Code == "not_adjacent")
                    App.Alert("無法直達", "這裡沒有路直接通往那裡，先經過相鄰的地點吧。");
                else
                    App.ShowError(e, "scene/move");
            });
        }
    }

    /// <summary>10 人物手帳 — text only: who they are and what you remember; no numbers or gauges.</summary>
    public sealed class PersonScreen : GameScreen
    {
        private Text _who = null!;
        private Text _impression = null!;
        private Text _recent = null!;
        private Text _memories = null!;
        private Text _together = null!;
        private Text _clues = null!;

        public override void Build()
        {
            Image shade = UIKit.Block(Root, "Shade", new Color(0, 0, 0, 0.45f), true);
            shade.rectTransform.Fill();
            Image book = UIKit.Panel(Root, "Book", new Color(0.08f, 0.10f, 0.18f, 0.97f));
            book.rectTransform.PlaceTL(0.20f, 0.08f, 0.60f, 0.84f);
            RectTransform b = book.rectTransform;
            _who = UIKit.Label(b, "", 30, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            _who.rectTransform.PlaceTL(0.04f, 0.03f, 0.80f, 0.08f);
            Section(b, "你的第一印象", 0.33f, 0.14f, 0.63f, 0.16f, out _impression);
            Section(b, "最近", 0.33f, 0.32f, 0.63f, 0.14f, out _recent);
            Section(b, "你們記得的事", 0.04f, 0.50f, 0.92f, 0.20f, out _memories);
            Section(b, "一起做過的事", 0.04f, 0.73f, 0.44f, 0.22f, out _together);
            Section(b, "已知線索", 0.52f, 0.73f, 0.44f, 0.22f, out _clues);
            Image portrait = UIKit.Panel(b, "Portrait", new Color(1, 1, 1, 0.05f));
            portrait.rectTransform.PlaceTL(0.04f, 0.14f, 0.26f, 0.32f);
            Text p = UIKit.Label(portrait.transform, "半身像\n<size=16>（正式立繪待美術）</size>", 22, Theme.TextMuted, TextAnchor.MiddleCenter);
            p.rectTransform.Fill();
            Button close = UIKit.Button(b, "✕", () => App.Pop(), ButtonStyle.Ghost, 26);
            ((RectTransform)close.transform).PlaceTL(0.92f, 0.02f, 0.06f, 0.07f);
        }

        private void Section(RectTransform parent, string title, float l, float t, float w, float h, out Text body)
        {
            Image box = UIKit.Panel(parent, title, new Color(1, 1, 1, 0.04f));
            box.rectTransform.PlaceTL(l, t, w, h);
            Text head = UIKit.Label(box.transform, title, 20, Theme.Gold, TextAnchor.UpperLeft, FontStyle.Bold);
            head.rectTransform.Fill().Inset(16, 10, 16, 0);
            body = UIKit.Label(box.transform, "", 20, Theme.TextDim, TextAnchor.UpperLeft);
            body.rectTransform.Fill().Inset(16, 42, 16, 10);
        }

        public void Open(string npcId)
        {
            Dictionary<string, object?>? npc = Session.FindNpc(npcId);
            string name = npc.GetString("name", Session.NpcName(npcId));
            string cls = npc.GetString("class");
            string identity = npc.GetString("surface_identity");
            _who.text = name + (cls.Length > 0 ? "　<size=22><color=#a8b8d8>" + cls + "</color></size>" : "");
            _impression.text = identity.Length > 0 ? "「" + identity + "」" : "還沒有留下明確的印象。";
            const string pending = "<color=#8a95ab>（伺服器尚未提供這部分資料，v2 開放）</color>";
            _recent.text = npc.GetString("speech_style").Length > 0 ? "說話方式：" + npc.GetString("speech_style") : pending;
            _memories.text = pending;
            _together.text = pending;
            _clues.text = pending;
        }
    }

    /// <summary>11 任務與章節 — chapter 1 五關, task cards with 接／棄／完成, and closure records.</summary>
    public sealed class QuestScreen : GameScreen
    {
        public override bool HidesBelow => true;

        private static readonly string[] Stages = { "登錄日", "招募月", "排名戰", "學園祭之日", "初鳴之夜" };
        private RectTransform _stages = null!;
        private RectTransform _active = null!;
        private RectTransform _available = null!;
        private Text _world = null!;

        public override void Build()
        {
            Backdrop(0.6f);
            Heading("遊戲中 › 任務", "任務與章節");
            CloseButton();
            Box(Root, "第一章　入城", 0.03f, 0.17f, 0.22f, 0.50f, out _stages);
            UIKit.VBox(_stages, 10);
            Box(Root, "進行中", 0.27f, 0.17f, 0.36f, 0.66f, out RectTransform activeBody);
            UIKit.ScrollList(activeBody, out _active, 10, 0);
            ((RectTransform)_active.parent.parent).Fill();
            Box(Root, "可接受", 0.65f, 0.17f, 0.32f, 0.66f, out RectTransform availBody);
            UIKit.ScrollList(availBody, out _available, 10, 0);
            ((RectTransform)_available.parent.parent).Fill();
            Box(Root, "世界的變化", 0.03f, 0.70f, 0.22f, 0.13f, out RectTransform world);
            _world = UIKit.Label(world, "", 19, Theme.TextDim);
            _world.rectTransform.Fill();
            BackButton(() => App.Pop());
        }

        public override void OnShow()
        {
            RenderStages();
            _world.text = Session.LastTurnDelta.Length > 0 ? "「" + Session.LastTurnDelta + "」" : "（這一回合沒有明顯的變化）";
            Load();
        }

        private void RenderStages()
        {
            UIKit.Clear(_stages);
            string chapter = Session.State.GetString("chapter_title");
            (int seq, int total, string goal) = Session.Beat;
            string currentQuest = Session.State.GetObject("beat").GetString("quest_name");
            for (int i = 0; i < Stages.Length; i++)
            {
                bool now = currentQuest.Contains(Stages[i]);
                UIKit.FlowLabel(_stages, (now ? "<color=#ffd27a>▶ " : "　 ") + "第" + "一二三四五"[i] + "關・" + Stages[i] + (now ? "　進行中</color>" : ""), 21, now ? Theme.Text : Theme.TextDim);
            }
            if (goal.Length > 0)
                UIKit.FlowLabel(_stages, "\n目前節拍 " + seq + "/" + total + "：" + goal, 18, Theme.TextMuted);
        }

        private void Load()
        {
            UIKit.Clear(_active);
            UIKit.Clear(_available);
            UIKit.FlowLabel(_active, "讀取中…", 20, Theme.TextMuted);
            Api.GetObject("/api/quest/player/active", true, json =>
            {
                UIKit.Clear(_active);
                List<object?> quests = json.GetList("quests") ?? new List<object?>();
                if (quests.Count == 0)
                    UIKit.FlowLabel(_active, "沒有進行中的任務。", 20, Theme.TextMuted);
                foreach (object? o in quests)
                {
                    if (o is Dictionary<string, object?> q)
                        ActiveCard(q);
                }
            }, e =>
            {
                UIKit.Clear(_active);
                UIKit.FlowLabel(_active, "<color=#ff7a7a>" + GameApp.ErrorTitle(e) + "</color>：" + GameApp.ErrorBody(e), 18, Theme.TextDim);
            });
            Api.GetObject("/api/quest/player/available", true, json =>
            {
                UIKit.Clear(_available);
                int shown = 0;
                foreach (object? o in json.GetList("items") ?? new List<object?>())
                {
                    if (o is Dictionary<string, object?> q && q.GetString("status") == "AVAILABLE")
                    {
                        AvailableCard(q);
                        shown++;
                    }
                }
                if (shown == 0)
                    UIKit.FlowLabel(_available, "目前沒有可接的任務。", 20, Theme.TextMuted);
            }, e =>
            {
                UIKit.Clear(_available);
                UIKit.FlowLabel(_available, GameApp.ErrorTitle(e), 18, Theme.TextMuted);
            });
        }

        private static string QuestName(Dictionary<string, object?> q)
        {
            Dictionary<string, object?>? display = q.GetObject("display");
            return q.GetString("quest_name", display.GetString("name_zh", display.GetString("name", q.GetString("description", q.GetString("quest_id")))));
        }

        private void ActiveCard(Dictionary<string, object?> q)
        {
            string id = q.GetString("quest_id");
            Image card = UIKit.Panel(_active, "Quest", Theme.PanelSoft);
            RectTransform c = card.rectTransform;
            UIKit.VBox(c, 6, 16);
            UIKit.FlowLabel(c, QuestName(q), 24, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            string desc = q.GetString("description");
            if (desc.Length > 0)
                UIKit.FlowLabel(c, desc, 19, Theme.TextDim);
            foreach (object? o in q.GetList("step_progress") ?? new List<object?>())
            {
                if (o is Dictionary<string, object?> step)
                {
                    bool done = step.GetString("status") == "DONE";
                    UIKit.FlowLabel(c, (done ? "<color=#7fe6a8>✔</color> " : "・") + step.GetString("title"), 18, done ? Theme.TextMuted : Theme.TextDim);
                }
            }
            RectTransform row = UIKit.Rect(c, "Row");
            row.Pref(48);
            UIKit.HBox(row, 10, 0, TextAnchor.MiddleRight);
            UIKit.Button(row, "放棄", () => Act("abandon", id, "放棄任務"), ButtonStyle.Ghost, 19).Pref(-1, 110);
            UIKit.Button(row, "完成", () => Act("complete", id, "回報完成"), ButtonStyle.Primary, 19).Pref(-1, 110);
        }

        private void AvailableCard(Dictionary<string, object?> q)
        {
            string id = q.GetString("quest_id");
            Image card = UIKit.Panel(_available, "Quest", Theme.PanelSoft);
            RectTransform c = card.rectTransform;
            UIKit.VBox(c, 6, 16);
            UIKit.FlowLabel(c, QuestName(q), 22, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            string giver = q.GetString("giver_npc_id");
            if (giver.Length > 0)
                UIKit.FlowLabel(c, "委託：" + Session.NpcName(giver), 18, Theme.TextDim);
            UIKit.Button(c, "接受", () => Act("accept", id, "接受任務"), ButtonStyle.Normal, 19).Pref(46);
        }

        private void Act(string verb, string id, string label)
        {
            App.Busy(true, label + "…");
            Api.Post("/api/quest/player/" + verb + "/" + System.Uri.EscapeDataString(id), null, true, reply =>
            {
                App.Busy(false);
                var json = reply as Dictionary<string, object?>;
                if (verb == "complete")
                    App.Toast("任務完成" + (json.GetString("grade").Length > 0 ? "（評等 " + json.GetString("grade") + "）" : ""), Theme.Ok);
                else
                    App.Toast(label + "：完成", Theme.Ok);
                Load();
            }, e =>
            {
                App.Busy(false);
                if (e.Code == "objectives_not_met")
                    App.Alert("還不能回報", e.Message);
                else
                    App.ShowError(e, "quest/" + verb);
            });
        }
    }

    /// <summary>對話回顧（紀錄）— GET /api/history with control tags stripped.</summary>
    public sealed class BacklogScreen : GameScreen
    {
        public override bool HidesBelow => true;

        private RectTransform _list = null!;
        private ScrollRect _scroll = null!;

        public override void Build()
        {
            Backdrop(0.75f);
            Heading("遊戲中 › 紀錄", "對話回顧");
            CloseButton();
            Image panel = UIKit.Panel(Root, "Log", Theme.Panel);
            panel.rectTransform.PlaceTL(0.12f, 0.16f, 0.76f, 0.72f);
            _scroll = UIKit.ScrollList(panel.transform, out _list, 14, 24);
            ((RectTransform)_scroll.transform).Fill();
            BackButton(() => App.Pop());
        }

        public override void OnShow()
        {
            UIKit.Clear(_list);
            UIKit.FlowLabel(_list, "讀取中…", 20, Theme.TextMuted);
            Api.GetHistory(60, (messages, raw) =>
            {
                UIKit.Clear(_list);
                string opening = NarrativeText.Clean(raw.GetString("initial_event"));
                if (opening.Length > 0)
                    Entry("開場", opening, 42);
                foreach (EdenHistoryMessage m in messages)
                {
                    if (m.Role == "user")
                        Entry("你", NarrativeText.Clean(m.Content), 210);
                    else
                        Entry(m.SpeakerName.Length > 0 ? m.SpeakerName : "旁白", NarrativeText.Clean(m.Content), m.Raw.GetInt("speaker_hue", 260));
                }
                if (messages.Count == 0 && opening.Length == 0)
                    UIKit.FlowLabel(_list, "還沒有對話。", 20, Theme.TextMuted);
                Canvas.ForceUpdateCanvases();
                _scroll.verticalNormalizedPosition = 0;
            }, e =>
            {
                UIKit.Clear(_list);
                UIKit.FlowLabel(_list, GameApp.ErrorTitle(e) + "：" + GameApp.ErrorBody(e), 20, Theme.Danger);
            });
        }

        private void Entry(string who, string text, int hue)
        {
            if (text.Length == 0)
                return;
            string color = ColorUtility.ToHtmlStringRGB(Theme.FromHue(hue, 0.45f, 0.95f));
            UIKit.FlowLabel(_list, "<b><color=#" + color + ">" + who + "</color></b>\n" + text, 22, who == "你" ? Theme.TextDim : Theme.Text);
            UIKit.Divider(_list);
        }
    }

    /// <summary>行囊 — GET /api/inventory/live, POST /api/inventory/use (by name).</summary>
    public sealed class InventoryScreen : GameScreen
    {
        public override bool HidesBelow => true;

        private RectTransform _grid = null!;
        private Text _detail = null!;
        private Text _cap = null!;
        private Button _use = null!;
        private string _selected = string.Empty;

        public override void Build()
        {
            Backdrop(0.6f);
            Heading("遊戲中 › 行囊", "行囊");
            CloseButton();
            Box(Root, "道具", 0.05f, 0.17f, 0.56f, 0.70f, out RectTransform items);
            UIKit.ScrollList(items, out _grid, 8, 0);
            ((RectTransform)_grid.parent.parent).Fill();
            Box(Root, "說明", 0.64f, 0.17f, 0.31f, 0.52f, out RectTransform detail);
            _detail = UIKit.Label(detail, "選一個道具看說明。", 21, Theme.TextDim);
            _detail.rectTransform.Fill();
            _cap = UIKit.Label(Root, "", 20, Theme.TextMuted, TextAnchor.MiddleLeft);
            _cap.rectTransform.PlaceTL(0.64f, 0.71f, 0.31f, 0.05f);
            _use = UIKit.Button(Root, "使用", Use, ButtonStyle.Primary, 26);
            ((RectTransform)_use.transform).PlaceTL(0.64f, 0.78f, 0.31f, 0.08f);
            BackButton(() => App.Pop());
        }

        public override void OnShow()
        {
            _use.interactable = false;
            UIKit.Clear(_grid);
            UIKit.FlowLabel(_grid, "讀取中…", 20, Theme.TextMuted);
            Api.GetObject("/api/inventory/live", true, json =>
            {
                UIKit.Clear(_grid);
                Dictionary<string, object?>? cap = json.GetObject("capacity");
                _cap.text = cap != null ? "容量 " + cap.GetInt("used") + " / " + cap.GetInt("max") : "";
                List<object?> items = json.GetList("items") ?? new List<object?>();
                if (items.Count == 0)
                    UIKit.FlowLabel(_grid, "行囊是空的。", 20, Theme.TextMuted);
                foreach (object? o in items)
                {
                    if (!(o is Dictionary<string, object?> item))
                        continue;
                    string name = item.GetString("name");
                    Button b = UIKit.Button(_grid, item.GetString("icon") + "　" + name + "　<color=#a8b8d8>×" + item.GetInt("qty") + "</color>", () => Select(item), ButtonStyle.Ghost, 21);
                    b.ButtonText().alignment = TextAnchor.MiddleLeft;
                    b.Pref(52);
                }
            }, e =>
            {
                UIKit.Clear(_grid);
                UIKit.FlowLabel(_grid, GameApp.ErrorTitle(e) + "：" + GameApp.ErrorBody(e), 20, Theme.Danger);
            });
        }

        private void Select(Dictionary<string, object?> item)
        {
            _selected = item.GetString("name");
            _detail.text = "<b>" + _selected + "</b>\n" + item.GetString("desc") +
                           (item.GetString("effect").Length > 0 ? "\n\n<color=#7fe6a8>效果：" + item.GetString("effect") + "</color>" : "");
            _use.interactable = item.GetStringList("ops").Contains("use");
        }

        private void Use()
        {
            App.Busy(true, "使用中…");
            Api.Post("/api/inventory/use", new Dictionary<string, object?> { ["name"] = _selected }, true, reply =>
            {
                App.Busy(false);
                var json = reply as Dictionary<string, object?>;
                App.Toast(json.GetString("message", "已使用"), Theme.Ok);
                Session.State["player_ap"] = (long)json.GetInt("player_ap", Session.Ap);
                Session.State["player_stamina"] = (long)json.GetInt("player_stamina", Session.Stamina);
                Session.NotifyChanged();
                OnShow();
            }, e => Fail(e, "inventory/use"));
        }
    }

    /// <summary>遊戲中選單 (Esc).</summary>
    public sealed class PauseScreen : GameScreen
    {
        public override void Build()
        {
            Image shade = UIKit.Block(Root, "Shade", new Color(0, 0, 0, 0.55f), true);
            shade.rectTransform.Fill();
            Box(Root, "選單", 0.38f, 0.20f, 0.24f, 0.60f, out RectTransform body);
            UIKit.VBox(body, 12);
            UIKit.Button(body, "回到遊戲", () => App.Pop(), ButtonStyle.Primary, 26).Pref(62);
            UIKit.Button(body, "讀取紀錄", () => App.Push<SaveLoadScreen>(), ButtonStyle.Normal, 24).Pref(58);
            UIKit.Button(body, "設定", () => App.Push<SettingsScreen>(), ButtonStyle.Normal, 24).Pref(58);
            UIKit.Button(body, "開啟紀錄資料夾", () => App.OpenUserFolder(), ButtonStyle.Normal, 24).Pref(58);
            UIKit.Button(body, "回標題", () => App.Confirm("回標題", "進度每回合都已存在伺服器上，可以從「繼續」接回。", "回標題", () => App.Show<TitleScreen>()), ButtonStyle.Ghost, 24).Pref(58);
            UIKit.FlowLabel(body, "每一回合的進度都會自動存在伺服器上。", 17, Theme.TextMuted, TextAnchor.UpperCenter);
        }
    }
}
