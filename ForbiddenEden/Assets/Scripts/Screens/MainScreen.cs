#nullable enable
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ForbiddenEden.Core;
using ForbiddenEden.Net;
using ForbiddenEden.UI;
using ForbiddenEden.World;
using UnityEngine;
using UnityEngine.UI;

namespace ForbiddenEden.Screens
{
    /// <summary>
    /// 08 主畫面. One turn runs: 送出 → 等待 → 串流 → 選項／待決 → (錯誤). The layout keeps the
    /// lower third for the dialogue box; options only appear when the server sent some, and
    /// free typing replaces them instead of stacking under them.
    /// No affection numbers, gauges or 攻略 words are shown anywhere on this screen.
    /// </summary>
    public sealed class MainScreen : GameScreen
    {
        private enum TurnState { Idle, Waiting, Streaming, Error }

        private const int PageChars = 150;

        // HUD
        private RectTransform _people = null!;
        private Text _story = null!;
        private RectTransform _beatDots = null!;
        private Text _resources = null!;
        private RectTransform _notices = null!;
        private RectTransform _commands = null!;
        private Image _contest = null!;
        private Text _contestText = null!;
        private Image _letterTop = null!;
        private Image _letterBottom = null!;

        // Dialogue
        private Image _dialogue = null!;
        private Image _nameTag = null!;
        private Text _nameText = null!;
        private Text _body = null!;
        private Text _status = null!;
        private Text _more = null!;

        // Choices / input
        private RectTransform _options = null!;
        private RectTransform _inputRow = null!;
        private InputField _input = null!;
        private Button _send = null!;
        private Button _sayButton = null!;
        private Button _backToOptions = null!;
        private bool _typing;

        // Turn
        private TurnState _state = TurnState.Idle;
        private readonly StringBuilder _stream = new StringBuilder();
        private string _fullText = string.Empty;
        private List<string> _pages = new List<string> { string.Empty };
        private int _page;
        private float _reveal;
        private bool _streamDone = true;
        private EdenPendingChoice? _pendingAfterTurn;
        private string _playerLine = string.Empty;
        private bool _eventsOpen;

        public static void Enter(GameApp app, bool resumed)
        {
            MainScreen main = app.Show<MainScreen>();
            main.Begin(resumed);
        }

        public override void Build()
        {
            // Cinematic letterbox (hidden unless state.cinematic).
            _letterTop = UIKit.Block(Root, "LetterTop", Color.black);
            _letterTop.rectTransform.Place(0, 0.91f, 1, 1);
            _letterBottom = UIKit.Block(Root, "LetterBottom", Color.black);
            _letterBottom.rectTransform.Place(0, 0, 1, 0.04f);

            // ① 劇情條
            Image story = UIKit.Panel(Root, "StoryBar", Theme.Panel);
            story.rectTransform.PlaceTL(0.30f, 0.022f, 0.40f, 0.062f);
            _story = UIKit.Label(story.transform, "", 22, Theme.Text, TextAnchor.MiddleLeft);
            _story.rectTransform.Fill().Inset(22, 0, 150, 0);
            _beatDots = UIKit.Rect(story.transform, "Beat").Place(1, 0, 1, 1);
            _beatDots.offsetMin = new Vector2(-140, 0);
            _beatDots.offsetMax = new Vector2(-18, 0);
            UIKit.HBox(_beatDots, 6, 0, TextAnchor.MiddleRight, false, false);

            // ② 在場人物
            _people = UIKit.Rect(Root, "People").PlaceTL(0.02f, 0.020f, 0.27f, 0.07f);
            UIKit.HBox(_people, 10, 0, TextAnchor.MiddleLeft, false, false);

            // ⑨ 資源 + ③ 系統鈕
            Image res = UIKit.Panel(Root, "Resources", Theme.Panel);
            res.rectTransform.PlaceTL(0.715f, 0.022f, 0.13f, 0.062f);
            _resources = UIKit.Label(res.transform, "", 19, Theme.Text, TextAnchor.MiddleCenter);
            _resources.rectTransform.Fill().Inset(8, 0, 8, 0);
            SystemButtons(0.026f);

            // ⑪ 新訊息
            _notices = UIKit.Rect(Root, "Notices").PlaceTL(0.80f, 0.11f, 0.185f, 0.30f);
            UIKit.VBox(_notices, 8, 0, TextAnchor.UpperRight);

            // 排名戰列（story_phase == contest）
            _contest = UIKit.Panel(Root, "Contest", new Color(0.30f, 0.10f, 0.14f, 0.88f));
            _contest.rectTransform.PlaceTL(0.27f, 0.105f, 0.46f, 0.075f);
            _contestText = UIKit.Label(_contest.transform, "", 24, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            _contestText.rectTransform.Fill();

            // ⑧ 指令列（左側直列）
            _commands = UIKit.Rect(Root, "Commands").PlaceTL(0.012f, 0.16f, 0.07f, 0.52f);
            UIKit.VBox(_commands, 8);
            Command("行囊", () => App.Push<InventoryScreen>());
            Command("移動", () => App.Push<CampusMapScreen>());
            Command("任務", () => App.Push<QuestScreen>());
            Command("紀錄", () => App.Push<BacklogScreen>());
            Command("拍照", () => App.Push<PhotoScreen>());
            Command("共鳴", () => App.Toast("「共鳴」的伺服器端點尚未開放。", Theme.Warn), false);
            Command("同伴", () => App.Toast("此世界目前沒有同伴資料（adventure 模式關閉）。", Theme.Warn), false);
            Command("買賣", () => App.Toast("商店以 credits 計價，經濟設計待定。", Theme.Warn), false);
            Command("選單", () => App.Push<PauseScreen>());

            // ⑥ 選項（只在有選項時出現）
            _options = UIKit.Rect(Root, "Options").PlaceTL(0.15f, 0.44f, 0.70f, 0.18f);
            UIKit.Grid(_options, new Vector2(660, 76), new Vector2(18, 14), 2).childAlignment = TextAnchor.LowerCenter;

            // ⑤ 對話框
            _dialogue = UIKit.Panel(Root, "Dialogue", new Color(0.04f, 0.06f, 0.13f, 0.86f));
            _dialogue.rectTransform.PlaceTL(0.10f, 0.675f, 0.80f, 0.235f);
            var click = _dialogue.gameObject.AddComponent<Button>();
            click.transition = Selectable.Transition.None;
            click.onClick.AddListener(Advance);
            _nameTag = UIKit.Panel(_dialogue.transform, "NameTag", Theme.AccentDark);
            _nameTag.rectTransform.Place(0, 1, 0, 1);
            _nameTag.rectTransform.pivot = new Vector2(0, 0);
            _nameTag.rectTransform.sizeDelta = new Vector2(260, 52);
            _nameTag.rectTransform.anchoredPosition = new Vector2(28, -16);
            _nameText = UIKit.Label(_nameTag.transform, "", 25, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _nameText.rectTransform.Fill();
            _body = UIKit.Label(_dialogue.transform, "", 27, Theme.Text, TextAnchor.UpperLeft);
            _body.rectTransform.Fill().Inset(44, 46, 44, 26);
            _body.lineSpacing = 1.3f;
            _status = UIKit.Label(_dialogue.transform, "", 19, Theme.TextMuted, TextAnchor.LowerLeft);
            _status.rectTransform.Fill().Inset(44, 0, 300, 12);
            _status.gameObject.AddComponent<Pulse>();
            _more = UIKit.Label(_dialogue.transform, "▼", 22, Theme.Gold, TextAnchor.LowerRight);
            _more.rectTransform.Fill().Inset(0, 0, 30, 12);
            _more.gameObject.AddComponent<Pulse>().Speed = 4f;

            // ⑦ 直接打字回應 / ⑩ 紀錄
            _inputRow = UIKit.Rect(Root, "InputRow").PlaceTL(0.10f, 0.918f, 0.80f, 0.058f);
            UIKit.HBox(_inputRow, 10, 0, TextAnchor.MiddleLeft);
            _sayButton = UIKit.Button(_inputRow, "✎ 自己說…", () => SetTyping(true), ButtonStyle.Ghost, 21);
            _sayButton.Pref(-1, 220);
            _backToOptions = UIKit.Button(_inputRow, "‹ 選項", () => SetTyping(false), ButtonStyle.Ghost, 21);
            _backToOptions.Pref(-1, 130);
            _input = UIKit.Input(_inputRow, "直接打字回應…（Enter 送出）", false, 23);
            _input.Pref(-1, -1, 1);
            _input.characterLimit = 300;
            _input.onSubmit.AddListener(_ => SendTyped());
            _send = UIKit.Button(_inputRow, "送出 <size=17><color=#a8c8ff>−1 EP</color></size>", SendTyped, ButtonStyle.Primary, 22);
            _send.Pref(-1, 170);
            UIKit.Button(_inputRow, "紀錄｜終端", () => App.Push<BacklogScreen>(), ButtonStyle.Ghost, 20).Pref(-1, 170);

            Session.Changed += RefreshHud;
        }

        private void Command(string label, UnityEngine.Events.UnityAction onClick, bool enabled = true)
        {
            Button b = UIKit.Button(_commands, label, onClick, ButtonStyle.Menu, 22);
            b.ButtonText().alignment = TextAnchor.MiddleCenter;
            b.ButtonText().rectTransform.Inset(4, 2, 4, 2);
            if (!enabled)
                b.ButtonText().color = Theme.TextMuted;
            b.Pref(52);
        }

        // ---- lifecycle -----------------------------------------------------------------

        public void Begin(bool resumed)
        {
            _state = TurnState.Idle;
            _typing = false;
            _stream.Length = 0;
            SetName(Session.SpeakerName.Length > 0 ? Session.SpeakerName : "旁白", Session.SpeakerHue);
            ShowNarrative(Session.LastNarrative.Length > 0 ? Session.LastNarrative : "……", done: true);
            RenderOptions();
            TitleScreen.LoadRosters(App);
            RefreshFullState(() =>
            {
                if (resumed)
                    LoadLastNarrative();
                CheckPending();
            });
            OpenEvents();
        }

        public override void OnShow()
        {
            App.Stage.HidePlayer();
            App.Rig.FreeControl = false;
            App.Rig.SetSway(0.15f);
            RefreshHud();
            UpdateStage(snap: true);
        }

        public override void OnHide()
        {
            if (_eventsOpen)
            {
                Api.CloseEvents();
                _eventsOpen = false;
            }
        }

        public override void OnOverlaysClosed()
        {
            App.Rig.FreeControl = false;
            App.Stage.HidePlayer();
            App.Stage.SetCastVisible(true);
            RefreshHud();
            UpdateStage(snap: false);
        }

        public override bool HandleEscape()
        {
            if (_typing && Session.Options.Count > 0)
            {
                SetTyping(false);
                return true;
            }
            App.Push<PauseScreen>();
            return true;
        }

        private void OpenEvents()
        {
            if (!Api.HasSession)
                return;
            _eventsOpen = true;
            Api.OpenEvents(new EdenStreamHandlers
            {
                OnSystem = Notice,
                OnPendingChoice = p =>
                {
                    if (_state == TurnState.Idle)
                        ShowPending(p);
                    else
                        _pendingAfterTurn = p;
                },
                OnChoiceResolved = r => Notice("事件「" + r.EventName + "」：" + r.ChosenText),
                OnError = e => Notice("事件通道中斷：" + GameApp.ErrorTitle(e))
            });
        }

        // ---- tour / smoke-test hooks ---------------------------------------------------------

        public bool DebugIdle => _state == TurnState.Idle && _streamDone;

        public void DebugSend(string text)
        {
            if (text.Length == 1 && char.IsDigit(text[0]))
                PickOption(text[0] - '1');
            else
                Send(text, text);
        }

        /// <summary>Tour hook: show a given scene with a given speaker without a server turn.</summary>
        public void DebugSetScene(string sceneId, string sceneName, string speaker)
        {
            Session.State["current_scene"] = sceneId;
            Session.State["scene_name"] = sceneName;
            Session.SpeakerName = speaker;
            SetName(speaker, Session.SpeakerHue);
            Session.NotifyChanged();
            UpdateStage(snap: true);
        }

        public void DebugCloseModals()
        {
            foreach (Transform child in Root)
            {
                if (child.name == "Pending")
                    Destroy(child.gameObject);
            }
        }

        // ---- sending ---------------------------------------------------------------------

        private void SendTyped()
        {
            string text = _input.text.Trim();
            if (text.Length == 0)
            {
                _input.ActivateInputField();
                return;
            }
            _input.text = string.Empty;
            Send(text, text);
        }

        private void PickOption(int index)
        {
            if (index < 0 || index >= Session.Options.Count)
                return;
            Send((index + 1).ToString(), Session.Options[index]);
        }

        private void Send(string input, string shown)
        {
            if (_state == TurnState.Waiting || _state == TurnState.Streaming)
                return;
            _playerLine = StripChip(shown);
            _stream.Length = 0;
            _streamDone = false;
            _state = TurnState.Waiting;
            SetTyping(false);
            Session.Options = new List<string>();
            RenderOptions();
            SetName("你", 210);
            ShowNarrative("<color=#a8b8d8>「" + _playerLine + "」</color>", done: false);
            _status.text = "思考中…";
            App.Stage.StopTalking();
            Api.SendChat(input, new EdenStreamHandlers
            {
                OnToken = OnToken,
                OnSystem = Notice,
                OnStateChange = text => App.Log("state_change " + text.Replace("\n", " ")),
                OnPendingChoice = p => _pendingAfterTurn = p,
                OnOptions = opts => Session.Options = opts,
                OnDone = OnDone,
                OnChoiceResolved = OnChoiceResolvedReply,
                OnError = OnTurnError
            });
        }

        private void OnToken(string token)
        {
            if (_state == TurnState.Waiting)
            {
                _state = TurnState.Streaming;
                _status.text = "";
                SetName(Session.SpeakerName.Length > 0 ? Session.SpeakerName : "……", Session.SpeakerHue);
                _page = 0;
                _reveal = 0;
                App.Stage.SetTalking(SpeakerId());
                App.Stage.Gesture(SpeakerId());
            }
            _stream.Append(token);
            SetText(NarrativeText.Clean(_stream.ToString()));
        }

        private void OnDone(EdenTurnDone done)
        {
            _streamDone = true;
            _state = TurnState.Idle;
            _status.text = "";
            if (done.Options.Count > 0)
                Session.Options = done.Options;
            Session.MergeState(done.State?.Raw);
            Session.LastNarrative = NarrativeText.Clean(_stream.ToString());
            if (Session.LastNarrative.Length == 0)
                ShowNarrative("（沒有回應文字）", done: true);
            SetName(Session.SpeakerName.Length > 0 ? Session.SpeakerName : "旁白", Session.SpeakerHue);
            App.Stage.StopTalking();
            Dictionary<string, object?> st = Session.State;
            if (st.GetBool("force_rest"))
                Notice("體力耗盡：你被迫休息了一陣子。");
            Dictionary<string, object?>? checkpoint = st.GetObject("chapter_checkpoint");
            if (checkpoint != null)
                Notice("段落收束：" + checkpoint.GetString("quest_name"));
            if (st.GetBool("player_dead"))
                App.Alert("你倒下了", st.GetString("player_death_reason", "故事在這裡暫停。"));
            UpdateStage(snap: false);
            // The turn's emotion tag shows on the speaker's face; everyone else relaxes.
            App.Stage.SetExpression(SpeakerId(), st.GetString("emotion"));
            RefreshFullState(null);
            if (_pendingAfterTurn != null)
            {
                EdenPendingChoice p = _pendingAfterTurn;
                _pendingAfterTurn = null;
                ShowPending(p);
            }
        }

        private void OnChoiceResolvedReply(EdenChoiceResolved r)
        {
            _streamDone = true;
            _state = TurnState.Idle;
            _status.text = "";
            SetName("事件", 42);
            ShowNarrative("<color=#ffd27a>【" + r.EventName + "】你選擇：" + r.ChosenText + "</color>\n" + r.Narrative, done: true);
            RefreshFullState(null);
        }

        private void OnTurnError(EdenApiError e)
        {
            _streamDone = true;
            _state = TurnState.Idle;
            _status.text = "";
            App.Stage.StopTalking();
            string partial = NarrativeText.Clean(_stream.ToString());
            switch (e.Code)
            {
                case "blocked_input":
                    // An in-world refusal: show it as the scene's reply, not as a system error.
                    SetName(Session.SpeakerName.Length > 0 ? Session.SpeakerName : "……", Session.SpeakerHue);
                    ShowNarrative(e.Message.Length > 0 ? e.Message : "……（對方沒有接話。）", done: true);
                    SetTyping(true);
                    break;
                case "pending_choice":
                    ShowNarrative(e.Message, done: true);
                    if (e.Pending != null)
                        ShowPending(e.Pending);
                    break;
                case "no_coins":
                case "insufficient_coins":
                    ShowNarrative(partial.Length > 0 ? partial : "<color=#ffc766>EP 已用盡。</color>", done: true);
                    App.Confirm("EP 不足", "每一回合對話會消耗 1 EP，目前 EP 已用完。\n\nEP 的取得方式（遊戲內賺取或儲值）尚未決定，這裡之後會連到「學園許可」或儲值頁。", "了解", null, null);
                    break;
                case "insufficient_ap":
                    ShowNarrative(partial, done: true);
                    App.Alert("行動點不足", e.Message.Length > 0 ? e.Message : "先休息或使用行囊裡的道具恢復行動點。");
                    break;
                default:
                    ShowNarrative(partial.Length > 0 ? partial + "\n<color=#ff7a7a>（回應中斷）</color>" : "<color=#ff7a7a>（沒有收到回應）</color>", done: true);
                    App.Confirm(GameApp.ErrorTitle(e), GameApp.ErrorBody(e), "重新取得狀態", () => RefreshFullState(null), "關閉");
                    break;
            }
            if (Session.Options.Count == 0)
                RefreshFullState(null);
        }

        // ---- dialogue ------------------------------------------------------------------

        private void ShowNarrative(string text, bool done)
        {
            _streamDone = done;
            _page = 0;
            _reveal = 0;
            SetText(text);
        }

        private void SetText(string text)
        {
            _fullText = text;
            _pages = PlainPaginate(text);
            if (_page >= _pages.Count)
                _page = _pages.Count - 1;
        }

        /// <summary>Pages on the visible text; rich-text tags stay inside one page.</summary>
        private static List<string> PlainPaginate(string text)
        {
            if (text.IndexOf('<') >= 0 && text.Length < PageChars * 2)
                return new List<string> { text };
            return EdenText.Paginate(text, PageChars);
        }

        private void SetName(string name, int hue)
        {
            _nameText.text = name;
            _nameTag.color = Theme.FromHue(hue, 0.55f, 0.55f);
            _nameTag.gameObject.SetActive(name.Length > 0);
        }

        private void Advance()
        {
            string page = _pages[_page];
            if (_reveal < page.Length)
            {
                _reveal = page.Length;
                return;
            }
            if (_page < _pages.Count - 1)
            {
                _page++;
                _reveal = 0;
            }
        }

        private void Update()
        {
            string page = _pages.Count > 0 ? _pages[_page] : string.Empty;
            bool richText = page.IndexOf('<') >= 0;
            float speed = Settings.textSpeed <= 0 ? 100000 : Settings.textSpeed;
            if (_reveal < page.Length)
                _reveal = Mathf.Min(page.Length, _reveal + speed * Time.unscaledDeltaTime);
            int shown = richText ? page.Length : Mathf.FloorToInt(_reveal);
            if (richText)
                _reveal = page.Length;
            _body.text = page.Substring(0, Mathf.Clamp(shown, 0, page.Length));

            bool pageDone = _reveal >= page.Length;
            bool lastPage = _page >= _pages.Count - 1;
            _more.gameObject.SetActive(pageDone && !lastPage);
            if (_more.gameObject.activeSelf)
                _more.text = "▼ " + (_page + 1) + "/" + _pages.Count;

            bool ready = _state == TurnState.Idle && _streamDone && pageDone && lastPage;
            bool showOptions = ready && !_typing && Session.Options.Count > 0;
            _options.gameObject.SetActive(showOptions);
            _sayButton.gameObject.SetActive(ready && !_typing && !Session.Cinematic);
            _backToOptions.gameObject.SetActive(ready && _typing && Session.Options.Count > 0);
            _input.gameObject.SetActive(ready && (_typing || Session.Options.Count == 0) && !Session.Cinematic);
            _send.gameObject.SetActive(_input.gameObject.activeSelf);
            if (_state == TurnState.Streaming && Time.frameCount % 20 == 0)
                App.Stage.SetTalking(SpeakerId());

            if (Input.GetKeyDown(KeyCode.Space) && !_input.isFocused)
                Advance();
            if (showOptions && !_input.isFocused)
            {
                for (int i = 0; i < Mathf.Min(9, Session.Options.Count); i++)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                        PickOption(i);
                }
            }
        }

        private void SetTyping(bool on)
        {
            _typing = on;
            if (on)
                _input.ActivateInputField();
        }

        // ---- options -------------------------------------------------------------------

        private static readonly Regex Chip = new Regex(@"^\s*【([^】]{1,8})】\s*");

        private static string StripChip(string option) => Chip.Replace(option, string.Empty);

        private void RenderOptions()
        {
            UIKit.Clear(_options);
            List<string> opts = Session.Options;
            if (Session.Cinematic && opts.Count <= 1)
            {
                AddOption(0, opts.Count > 0 ? opts[0] : "▶ 繼續", null);
                return;
            }
            Color[] tints =
            {
                new Color(0.20f, 0.42f, 0.70f, 0.94f), new Color(0.22f, 0.50f, 0.45f, 0.94f),
                new Color(0.45f, 0.36f, 0.62f, 0.94f), new Color(0.58f, 0.32f, 0.30f, 0.94f)
            };
            for (int i = 0; i < opts.Count && i < 6; i++)
                AddOption(i, opts[i], tints[i % tints.Length]);
        }

        private void AddOption(int index, string option, Color? tint)
        {
            Match m = Chip.Match(option);
            string chip = m.Success ? m.Groups[1].Value : string.Empty;
            string text = m.Success ? option.Substring(m.Length) : option;
            string label = (index + 1) + "　" + (chip.Length > 0 ? "<color=#ffd27a>【" + chip + "】</color>" : "") + text;
            Button b = UIKit.Button(_options, label, () => PickOption(index), ButtonStyle.Normal, 22);
            b.ButtonText().alignment = TextAnchor.MiddleLeft;
            b.ButtonText().rectTransform.Inset(22, 4, 16, 4);
            if (tint.HasValue)
                b.GetComponent<Image>().color = tint.Value;
        }

        public void ApplyTranslation(string narrative, List<string> options)
        {
            if (narrative.Length > 0)
            {
                Session.LastNarrative = NarrativeText.Clean(narrative);
                ShowNarrative(Session.LastNarrative, done: true);
            }
            if (options.Count > 0)
            {
                Session.Options = options;
                RenderOptions();
            }
        }

        // ---- pending choice --------------------------------------------------------------

        private void ShowPending(EdenPendingChoice p)
        {
            Image shade = UIKit.Block(Root, "Pending", new Color(0, 0, 0, 0.55f), true);
            shade.rectTransform.Fill();
            Image box = UIKit.Panel(shade.transform, "Box", new Color(0.12f, 0.08f, 0.16f, 0.96f));
            box.rectTransform.Place(0.27f, 0.22f, 0.73f, 0.80f);
            Text tag = UIKit.Label(box.transform, "突發事件", 20, Theme.Warn, TextAnchor.UpperLeft, FontStyle.Bold);
            tag.rectTransform.Place(0, 0.88f, 1, 1).Inset(36, 24, 36, 0);
            Text title = UIKit.Label(box.transform, p.EventName, 36, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            title.rectTransform.Place(0, 0.76f, 1, 0.90f).Inset(36, 0, 36, 0);
            Text body = UIKit.Label(box.transform, p.PromptInject, 23, Theme.TextDim);
            body.rectTransform.Place(0, 0.52f, 1, 0.76f).Inset(36, 0, 36, 0);
            RectTransform list = UIKit.Rect(box.transform, "Options").Place(0, 0, 1, 0.52f).Inset(36, 0, 36, 30);
            UIKit.VBox(list, 10);
            foreach (EdenChoiceOption o in p.Options)
            {
                string id = o.Id;
                Button b = UIKit.Button(list, o.Icon + "　" + o.Text + (o.IsDefault ? "　<size=16><color=#a8b8d8>（不回應時的預設）</color></size>" : ""), () =>
                {
                    Destroy(shade.gameObject);
                    App.Busy(true, "回應事件…");
                    Api.Choose(id, r =>
                    {
                        App.Busy(false);
                        OnChoiceResolvedReply(r);
                    }, e => Fail(e, "events/choose"));
                }, ButtonStyle.Normal, 24);
                b.ButtonText().alignment = TextAnchor.MiddleLeft;
                b.Pref(64);
            }
        }

        private void CheckPending()
        {
            Api.GetPendingChoice(p =>
            {
                if (p != null)
                    ShowPending(p);
            }, _ => { });
        }

        // ---- state / HUD ---------------------------------------------------------------

        private void RefreshFullState(System.Action? then)
        {
            Api.GetObject("/api/state", true, json =>
            {
                if (!json.GetBool("exists"))
                {
                    App.Alert("找不到遊戲", "伺服器上沒有這個裝置的進行中生活（可能已過期）。請從「讀取紀錄」接回。", () => App.Show<TitleScreen>());
                    return;
                }
                Session.MergeState(json);
                if (Session.Options.Count == 0)
                    Session.Options = json.GetStringList("options");
                if (_state == TurnState.Idle)
                    RenderOptions();
                Dictionary<string, object?>? clear = json.GetObject("stage_clear");
                if (clear != null)
                {
                    string stageId = clear.GetString("stage_id");
                    App.Confirm("關卡完成", "「" + clear.GetString("title") + "」已經收束。", "繼續", () =>
                        Api.Post("/api/stage/clear-ack/" + System.Uri.EscapeDataString(stageId), null, true, _ => { }, _ => { }), null);
                }
                if (json.GetBool("chapter_end_ready"))
                    Notice("章末已就緒：" + json.GetString("chapter_end_reason", "本章的故事可以收尾了。"));
                UpdateStage(snap: false);
                then?.Invoke();
            }, e =>
            {
                Notice("狀態更新失敗：" + GameApp.ErrorTitle(e));
                then?.Invoke();
            });
        }

        private void LoadLastNarrative()
        {
            Api.GetHistory(8, (messages, raw) =>
            {
                for (int i = messages.Count - 1; i >= 0; i--)
                {
                    if (messages[i].Role != "assistant")
                        continue;
                    string text = NarrativeText.Clean(messages[i].Content);
                    if (text.Length == 0)
                        continue;
                    Session.LastNarrative = text;
                    if (messages[i].SpeakerName.Length > 0)
                    {
                        Session.SpeakerName = messages[i].SpeakerName;
                        Session.SpeakerHue = messages[i].Raw.GetInt("speaker_hue", Session.SpeakerHue);
                    }
                    SetName(Session.SpeakerName.Length > 0 ? Session.SpeakerName : "旁白", Session.SpeakerHue);
                    ShowNarrative(text, done: true);
                    return;
                }
                string opening = raw.GetString("initial_event");
                if (opening.Length > 0)
                    ShowNarrative(NarrativeText.Clean(opening), done: true);
            }, _ => { });
        }

        private void RefreshHud()
        {
            if (_story == null)
                return;
            string phase = Session.StoryPhaseLabel;
            _story.text = Session.ClockLabel + "　<color=#6bbdff>｜</color>　" + Session.SceneName +
                          (phase.Length > 0 ? "　<color=#6bbdff>｜</color>　<color=#ffd27a>" + phase + "</color>" : "");
            UIKit.Clear(_beatDots);
            (int seq, int total, string _) = Session.Beat;
            for (int i = 0; i < Mathf.Min(total, 8); i++)
            {
                Image dot = UIKit.Panel(_beatDots, "Dot", i < seq ? Theme.Gold : new Color(1, 1, 1, 0.22f), UIKit.Circle, false);
                dot.Pref(16, 16);
            }
            _resources.text = "EP <b>" + Session.Coins + "</b>　行動 " + Session.Ap + "　體力 " + Session.Stamina;

            UIKit.Clear(_people);
            var seen = new HashSet<string>();
            foreach (string id in Session.PresentNpcIds())
                AddPerson(id, seen);
            string speakerId = SpeakerId();
            if (speakerId.Length > 0)
                AddPerson(speakerId, seen);

            bool contest = Session.InContest;
            _contest.gameObject.SetActive(contest);
            if (contest)
            {
                int round = Session.ContestRound;
                _contestText.text = "排名戰　<color=#ffd27a>" + Session.PlayerName + "</color>　對　<color=#ff9a9a>" +
                                    (Session.Opponent.Length > 0 ? Session.Opponent : "對手") + "</color>" +
                                    (round > 0 ? "　第 " + round + " 回合" : "") +
                                    "\n<size=18><color=#d8c8d8>" + ContestMood(Session.LastTurnDelta) + "</color></size>";
            }

            bool cinematic = Session.Cinematic;
            _letterTop.gameObject.SetActive(cinematic);
            _letterBottom.gameObject.SetActive(cinematic);
            _commands.gameObject.SetActive(!cinematic);
            if (_state == TurnState.Idle)
                RenderOptions();
        }

        /// <summary>局勢只用一句話描述，不顯示分數。</summary>
        private static string ContestMood(string delta)
        {
            if (delta.Contains("勝")) return "你拿下了這一局。";
            if (delta.Contains("負")) return "這一局對方佔了上風。";
            return delta.Length > 0 ? "雙方還在試探彼此的節奏。" : "對峙開始。";
        }

        private void AddPerson(string id, HashSet<string> seen)
        {
            if (id.Length == 0 || !seen.Add(id) || _people.childCount >= 5)
                return;
            string name = Session.NpcName(id);
            Dictionary<string, object?>? npc = Session.FindNpc(id);
            AvatarData look = AvatarData.ForNpc(id, npc.GetString("gender"));
            Image c = UIKit.Panel(_people, "Person:" + name, look.HairColor, UIKit.Circle);
            c.Pref(64, 64);
            var b = c.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => App.Push<PersonScreen>().Open(id));
            Text t = UIKit.Label(c.transform, name.Length > 0 ? name.Substring(0, 1) : "?", 28, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.rectTransform.Fill();
            t.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.5f);
        }

        private string SpeakerId()
        {
            string name = Session.SpeakerName;
            if (name.Length == 0)
                return string.Empty;
            Dictionary<string, object?>? npc = Session.FindNpc(name);
            return npc != null ? npc.GetString("npc_id") : "speaker:" + name;
        }

        private void UpdateStage(bool snap)
        {
            App.Stage.Build(Stage.KindFor(Session.SceneId, Session.SceneName));
            var cast = new List<(string, string, string)>();
            string speaker = SpeakerId();
            if (speaker.Length > 0)
            {
                Dictionary<string, object?>? npc = Session.FindNpc(Session.SpeakerName);
                cast.Add((speaker, Session.SpeakerName, npc.GetString("gender", "女")));
            }
            foreach (string id in Session.PresentNpcIds())
            {
                if (id == speaker)
                    continue;
                Dictionary<string, object?>? npc = Session.FindNpc(id);
                cast.Add((id, Session.NpcName(id), npc.GetString("gender")));
            }
            App.Stage.SetCast(cast, speaker);
            string focusId = cast.Count > 0 ? cast[0].Item1 : string.Empty;
            if (App.Stage.ActorTransform(focusId) != null)
                App.Rig.Shot(new Vector3(0.0f, App.Stage.EyeHeightOf(focusId) * 0.82f, 0.2f), 3.4f, 4, 2, 34, snap);
            else
                App.Rig.Shot(new Vector3(0, 1.6f, 4), 9f, 0, 6, 38, snap);
        }

        private void Notice(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Contains("token") || text.StartsWith("[mock]"))
            {
                App.Log("system " + text);
                return;
            }
            Image card = UIKit.Panel(_notices, "Notice", new Color(0.10f, 0.12f, 0.26f, 0.92f));
            card.Pref(84);
            Text t = UIKit.Label(card.transform, "<color=#ffd27a>新訊息</color>\n" + text, 18, Theme.Text, TextAnchor.UpperLeft);
            t.rectTransform.Fill().Inset(14, 8, 12, 8);
            t.verticalOverflow = VerticalWrapMode.Truncate;
            card.transform.SetAsFirstSibling();
            while (_notices.childCount > 3)
                DestroyImmediate(_notices.GetChild(_notices.childCount - 1).gameObject);
            Destroy(card.gameObject, 12f);
        }
    }
}
