#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ForbiddenEden.Net
{
    // Shapes follow ai-agent-server/internal/web (see HS6\Unity Api\SourceCode\ServerApi\API_Reference.md).

    public enum EdenEventKind
    {
        Unknown,
        Token,
        System,
        StateChange,
        PendingChoice,
        ChoiceResolved,
        Options,
        Done
    }

    /// <summary>One <c>data: {json}</c> event from <c>POST /api/chat</c> or <c>GET /api/events</c>.</summary>
    public sealed class EdenEvent
    {
        private EdenEvent(EdenEventKind kind, string typeName, Dictionary<string, object?>? json)
        {
            Kind = kind;
            TypeName = typeName;
            Json = json;
        }

        public EdenEventKind Kind { get; }
        public string TypeName { get; }
        public Dictionary<string, object?>? Json { get; }

        /// <summary>token / system text; state_change display text (not JSON); raw JSON string for pending_choice / choice_resolved.</summary>
        public string Content => Json.GetString("content");

        /// <summary>Present on <c>options</c> and <c>done</c>; both carry the array in an <c>options</c> field, not <c>content</c>.</summary>
        public List<string> Options => Json.GetStringList("options");

        public EdenStateSnapshot? State
        {
            get
            {
                Dictionary<string, object?>? state = Json.GetObject("state");
                return state == null ? null : new EdenStateSnapshot(state);
            }
        }

        public EdenPendingChoice? PendingChoice
        {
            get
            {
                Dictionary<string, object?>? obj = EdenJson.TryParseObject(Content);
                return obj == null ? null : new EdenPendingChoice(obj);
            }
        }

        public EdenChoiceResolved? ChoiceResolved
        {
            get
            {
                Dictionary<string, object?>? obj = EdenJson.TryParseObject(Content);
                return obj == null ? null : new EdenChoiceResolved(obj);
            }
        }

        public static EdenEvent FromSse(EdenSseMessage message)
        {
            Dictionary<string, object?>? json = EdenJson.TryParseObject(message.Data);
            string type = json.GetString("type", message.EventName);
            return new EdenEvent(KindOf(type), type, json);
        }

        private static EdenEventKind KindOf(string type)
        {
            switch (type)
            {
                case "token": return EdenEventKind.Token;
                case "system": return EdenEventKind.System;
                case "state_change": return EdenEventKind.StateChange;
                case "pending_choice": return EdenEventKind.PendingChoice;
                case "choice_resolved": return EdenEventKind.ChoiceResolved;
                case "options": return EdenEventKind.Options;
                case "done": return EdenEventKind.Done;
                default: return EdenEventKind.Unknown;
            }
        }
    }

    /// <summary><c>done.state</c> (stateSnapshot) or the <c>GET /api/state</c> payload, which is a superset.</summary>
    public sealed class EdenStateSnapshot
    {
        public EdenStateSnapshot(Dictionary<string, object?> raw)
        {
            Raw = raw;
            Companions = new List<EdenCompanion>();
            List<object?>? companions = raw.GetList("companions");
            if (companions != null)
            {
                foreach (object? item in companions)
                {
                    if (item is Dictionary<string, object?> c)
                        Companions.Add(new EdenCompanion(c));
                }
            }
        }

        public Dictionary<string, object?> Raw { get; }
        public List<EdenCompanion> Companions { get; }

        public string PlayerName => Raw.GetString("player_name");
        public string CharacterName => Raw.GetString("character_name");
        public string CurrentScene => Raw.GetString("current_scene");
        public string SceneName => Raw.GetString("scene_name");
        public string SpeakerName => Raw.GetString("speaker_name");
        public int SpeakerHue => Raw.GetInt("speaker_hue");
        public int Affection => Raw.GetInt("affection");
        public int TurnCount => Raw.GetInt("turn_count");
        public int PlayerCoins => Raw.GetInt("player_coins");
        public int PlayerAP => Raw.GetInt("player_ap");
        public int PlayerAPMax => Raw.GetInt("player_ap_max");
        public string Emotion => Raw.GetString("emotion");
        public string CharacterStatus => Raw.GetString("character_status");
        public string CharacterMood => Raw.GetString("character_mood");
        public string StageTitle => Raw.GetString("stage_title");
        public string ChapterTitle => Raw.GetString("chapter_title");
        public string CurrentPeriod => Raw.GetString("current_period");
        /// <summary>True while a scripted beat plays: lock free input and offer a single "continue".</summary>
        public bool Cinematic => Raw.GetBool("cinematic");
        public bool ForceRest => Raw.GetBool("force_rest");
        public bool PlayerDead => Raw.GetBool("player_dead");
    }

    public sealed class EdenCompanion
    {
        public EdenCompanion(Dictionary<string, object?> raw) => Raw = raw;

        public Dictionary<string, object?> Raw { get; }
        public string Id => Raw.GetString("id");
        public string Name => Raw.GetString("name");
        public int Affection => Raw.GetInt("affection");
        public bool Present => Raw.GetBool("present");
        public bool IsSpeaker => Raw.GetBool("is_speaker");
        public string Status => Raw.GetString("status");
        public string Mood => Raw.GetString("mood");
    }

    public sealed class EdenPendingChoice
    {
        public EdenPendingChoice(Dictionary<string, object?> raw)
        {
            Raw = raw;
            Options = new List<EdenChoiceOption>();
            List<object?>? options = raw.GetList("options");
            if (options != null)
            {
                foreach (object? item in options)
                {
                    if (item is Dictionary<string, object?> o)
                        Options.Add(new EdenChoiceOption(o));
                }
            }
        }

        public Dictionary<string, object?> Raw { get; }
        public List<EdenChoiceOption> Options { get; }
        public string EventId => Raw.GetString("event_id");
        public string EventName => Raw.GetString("event_name");
        public string PromptInject => Raw.GetString("prompt_inject");
    }

    public sealed class EdenChoiceOption
    {
        public EdenChoiceOption(Dictionary<string, object?> raw) => Raw = raw;

        public Dictionary<string, object?> Raw { get; }
        public string Id => Raw.GetString("id");
        public string Text => Raw.GetString("text");
        public string Icon => Raw.GetString("icon");
        public bool IsDefault => Raw.GetBool("is_default");
    }

    public sealed class EdenChoiceResolved
    {
        public EdenChoiceResolved(Dictionary<string, object?> raw) => Raw = raw;

        public Dictionary<string, object?> Raw { get; }
        public bool Ok => Raw.GetBool("ok", true);
        public string EventId => Raw.GetString("event_id");
        public string EventName => Raw.GetString("event_name");
        public string ChosenId => Raw.GetString("chosen_id");
        public string ChosenText => Raw.GetString("chosen_text");
        public string Narrative => Raw.GetString("narrative");
    }

    public sealed class EdenInitRequest
    {
        public string PlayerName = string.Empty;
        public string CharacterName = string.Empty;
        public string StartScene = string.Empty;
        public int ContentRating = 0;
        public bool SoloMode = true;
        public string DeviceUid = string.Empty;
        /// <summary>男 / 女 / empty. Anything else is rejected with 400 message=player_gender.</summary>
        public string PlayerGender = string.Empty;
        public bool SkipLlmInitialEvent = false;
        public string InitialEvent = string.Empty;
        public List<string>? InitialOptions = null;

        public string ToJson()
        {
            var body = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["player_name"] = PlayerName,
                ["character_name"] = CharacterName,
                ["start_scene"] = StartScene,
                ["content_rating"] = ContentRating,
                ["solo_mode"] = SoloMode
            };
            if (DeviceUid.Length > 0)
                body["device_uid"] = DeviceUid;
            if (PlayerGender.Length > 0)
                body["player_gender"] = PlayerGender;
            if (SkipLlmInitialEvent)
                body["skip_llm_initial_event"] = true;
            if (InitialEvent.Length > 0)
                body["initial_event"] = InitialEvent;
            if (InitialOptions != null && InitialOptions.Count > 0)
                body["initial_options"] = InitialOptions;
            return EdenJson.Serialize(body);
        }
    }

    public sealed class EdenInitResult
    {
        public EdenInitResult(Dictionary<string, object?> raw) => Raw = raw;

        public Dictionary<string, object?> Raw { get; }
        public string SessionId => Raw.GetString("session_id");
        public string InitialEvent => Raw.GetString("initial_event");
        public List<string> InitialOptions => Raw.GetStringList("initial_options");
        public string Message => Raw.GetString("message");
    }

    public sealed class EdenHistoryMessage
    {
        public EdenHistoryMessage(Dictionary<string, object?> raw) => Raw = raw;

        public Dictionary<string, object?> Raw { get; }
        /// <summary>user or assistant.</summary>
        public string Role => Raw.GetString("role");
        /// <summary>Raw content; assistant turns still carry &lt;SPEAKER&gt;/&lt;SETUP&gt; tags.</summary>
        public string Content => Raw.GetString("content");
        public string VisibleText => EdenText.VisibleNarrative(Content);
        public string SpeakerName => Raw.GetString("speaker_name");
    }

    /// <summary>Terminal <c>done</c> event of a chat turn.</summary>
    public sealed class EdenTurnDone
    {
        public EdenTurnDone(List<string> options, EdenStateSnapshot? state)
        {
            Options = options;
            State = state;
        }

        public List<string> Options { get; }
        public EdenStateSnapshot? State { get; }
    }

    public sealed class EdenApiError
    {
        public EdenApiError(long httpStatus, string code, string message, Dictionary<string, object?>? raw)
        {
            HttpStatus = httpStatus;
            Code = code;
            Message = message;
            Raw = raw;
        }

        /// <summary>0 for transport failures (no HTTP response).</summary>
        public long HttpStatus { get; }
        /// <summary>Server <c>error</c> field (no_session, no_coins, blocked_input, pending_choice, actor_busy, ...) or a client code (network, bad_json, stream_incomplete).</summary>
        public string Code { get; }
        /// <summary>Server <c>message</c> when present; for blocked_input it is an in-world reply fit for display.</summary>
        public string Message { get; }
        public Dictionary<string, object?>? Raw { get; }

        /// <summary>Snapshot attached to 409 pending_choice / option_not_found.</summary>
        public EdenPendingChoice? Pending
        {
            get
            {
                Dictionary<string, object?>? pending = Raw.GetObject("pending");
                return pending == null ? null : new EdenPendingChoice(pending);
            }
        }

        public bool IsRetryable => HttpStatus == 0 || Code == "actor_busy" || Code == "actor_timeout";

        public static EdenApiError FromResponse(long status, string body)
        {
            Dictionary<string, object?>? json = EdenJson.TryParseObject(body);
            string code = json.GetString("error", "http_" + status);
            string message = json.GetString("message", json == null ? body : string.Empty);
            return new EdenApiError(status, code, message, json);
        }

        public override string ToString() =>
            "HTTP " + HttpStatus + " " + Code + (Message.Length > 0 ? " (" + Message + ")" : string.Empty);
    }

    public static class EdenText
    {
        private static readonly Regex SetupBlock = new Regex(@"<SETUP>(.*?)</SETUP>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        private static readonly Regex AnyBlock = new Regex(@"<([A-Z_]+)>.*?</\1>", RegexOptions.Singleline | RegexOptions.CultureInvariant);

        /// <summary>Player-visible narrative of a stored assistant turn: the &lt;SETUP&gt; bodies, or the text with tag blocks removed.</summary>
        public static string VisibleNarrative(string content)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;
            MatchCollection setups = SetupBlock.Matches(content);
            if (setups.Count > 0)
            {
                var parts = new List<string>(setups.Count);
                foreach (Match m in setups)
                    parts.Add(m.Groups[1].Value.Trim());
                return string.Join("\n", parts.ToArray());
            }
            return AnyBlock.Replace(content, string.Empty).Trim();
        }

        private const string PageBreakAfter = "。！？!?…」』）)\n";

        /// <summary>
        /// Splits narrative into dialogue-box pages of at most <paramref name="maxChars"/>,
        /// preferring to break after sentence punctuation in the second half of a page.
        /// A page's break depends only on the characters inside it, so pages already
        /// shown stay put while a reply is still streaming in.
        /// </summary>
        public static List<string> Paginate(string text, int maxChars)
        {
            var pages = new List<string>();
            string s = (text ?? string.Empty).Trim();
            int start = 0;
            while (s.Length - start > maxChars)
            {
                int limit = start + maxChars;
                int cut = -1;
                for (int i = limit; i > start + maxChars / 2; i--)
                {
                    if (PageBreakAfter.IndexOf(s[i - 1]) >= 0)
                    {
                        cut = i;
                        break;
                    }
                }
                if (cut < 0)
                    cut = limit;
                if (char.IsHighSurrogate(s[cut - 1]))
                    cut--;
                pages.Add(s.Substring(start, cut - start).Trim());
                start = cut;
                while (start < s.Length && char.IsWhiteSpace(s[start]))
                    start++;
            }
            pages.Add(s.Substring(start).Trim());
            return pages;
        }
    }
}
