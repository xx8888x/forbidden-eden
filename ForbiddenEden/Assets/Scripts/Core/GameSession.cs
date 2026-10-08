#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ForbiddenEden.Net;
using ForbiddenEden.World;

namespace ForbiddenEden.Core
{
    /// <summary>The new-life draft built during character creation, sent to POST /api/init.</summary>
    public sealed class NewLifeDraft
    {
        public List<int> Questionnaire = new List<int>();
        public string Mark = string.Empty;
        public string MarkName = string.Empty;
        public string MarkGlyph = string.Empty;
        public string MarkEcho = string.Empty;
        public string PlayerName = string.Empty;
        /// <summary>男 or 女.</summary>
        public string Gender = "男";
        public string ProfessionId = string.Empty;
        public string ProfessionName = string.Empty;
        public string StartScene = string.Empty;
        public string FirstNpc = string.Empty;
        public AvatarData Avatar = AvatarData.Default(true);

        public Dictionary<string, object?> ToInitBody(string deviceUid)
        {
            var body = new Dictionary<string, object?>
            {
                ["player_name"] = PlayerName,
                ["player_gender"] = Gender,
                ["device_uid"] = deviceUid,
                ["solo_mode"] = true,
                // 分級在開局固定；Unity 版固定一般向。
                ["content_rating"] = 0
            };
            if (ProfessionId.Length > 0)
                body["player_profession_id"] = ProfessionId;
            if (StartScene.Length > 0)
                body["start_scene"] = StartScene;
            if (FirstNpc.Length > 0)
                body["character_name"] = FirstNpc;
            if (Questionnaire.Count > 0)
                body["questionnaire"] = Questionnaire;
            if (Mark.Length > 0)
            {
                body["mark"] = Mark;
                body["mark_name"] = MarkName;
                body["mark_echo"] = MarkEcho;
            }
            return body;
        }
    }

    /// <summary>What the client knows about the running game, refreshed from done.state and /api/state.</summary>
    public sealed class GameSession
    {
        public NewLifeDraft Draft = new NewLifeDraft();
        public Dictionary<string, object?> State = new Dictionary<string, object?>();
        public List<string> Options = new List<string>();
        public string LastNarrative = string.Empty;
        public string SpeakerName = string.Empty;
        public int SpeakerHue = 210;
        public List<Dictionary<string, object?>> NpcRoster = new List<Dictionary<string, object?>>();
        public List<Dictionary<string, object?>> SceneRoster = new List<Dictionary<string, object?>>();

        public event Action? Changed;

        public string PlayerName => State.GetString("player_name", Draft.PlayerName);
        public string SceneId => State.GetString("current_scene");
        public string SceneName => State.GetString("scene_name", SceneId);
        public int Coins => State.GetInt("player_coins");
        public int Ap => State.GetInt("player_ap");
        public int ApMax => State.GetInt("player_ap_max");
        public int Stamina => State.GetInt("player_stamina");
        public int StaminaMax => State.GetInt("player_stamina_max");
        public bool Cinematic => State.GetBool("cinematic");
        public string StoryPhase => State.GetString("story_phase");
        public string StoryPhaseLabel => State.GetString("story_phase_label");
        public bool InContest => StoryPhase == "contest";
        public string Opponent => State.GetString("opponent");
        public string LastTurnDelta => State.GetString("last_turn_delta");

        public string ClockLabel
        {
            get
            {
                string label = State.GetString("clock_label");
                if (label.Length > 0)
                    return label;
                int day = State.GetInt("day_count");
                string period = State.GetString("current_period");
                return day > 0 ? "第 " + day + " 天・" + period : period;
            }
        }

        /// <summary>beat.seq / beat.total, or (0,0) when absent.</summary>
        public (int seq, int total, string goal) Beat
        {
            get
            {
                Dictionary<string, object?>? beat = State.GetObject("beat");
                return beat == null ? (0, 0, string.Empty) : (beat.GetInt("seq"), beat.GetInt("total"), beat.GetString("goal"));
            }
        }

        /// <summary>The contest round from last_turn_delta ("對抗：第 2 回合／勝"); 0 when unknown.</summary>
        public int ContestRound
        {
            get
            {
                Match m = Regex.Match(LastTurnDelta, @"第\s*(\d+)\s*回合");
                return m.Success ? int.Parse(m.Groups[1].Value) : 0;
            }
        }

        public void MergeState(Dictionary<string, object?>? fresh)
        {
            if (fresh == null)
                return;
            foreach (KeyValuePair<string, object?> kv in fresh)
                State[kv.Key] = kv.Value;
            // Fields absent from a done.state must not linger as stale one-shot signals.
            if (!fresh.ContainsKey("force_rest"))
                State.Remove("force_rest");
            string speaker = fresh.GetString("speaker_name");
            if (speaker.Length > 0)
            {
                SpeakerName = speaker;
                SpeakerHue = fresh.GetInt("speaker_hue", SpeakerHue);
            }
            Changed?.Invoke();
        }

        public void ResetForNewGame()
        {
            State = new Dictionary<string, object?>();
            Options = new List<string>();
            LastNarrative = string.Empty;
            SpeakerName = string.Empty;
            Changed?.Invoke();
        }

        public void NotifyChanged() => Changed?.Invoke();

        public Dictionary<string, object?>? FindNpc(string idOrName)
        {
            foreach (Dictionary<string, object?> npc in NpcRoster)
            {
                if (npc.GetString("npc_id") == idOrName || npc.GetString("name") == idOrName)
                    return npc;
            }
            return null;
        }

        public string NpcName(string id)
        {
            Dictionary<string, object?>? npc = FindNpc(id);
            return npc == null ? id : npc.GetString("name", id);
        }

        public Dictionary<string, object?>? FindScene(string id)
        {
            foreach (Dictionary<string, object?> s in SceneRoster)
            {
                if (s.GetString("scene_id") == id)
                    return s;
            }
            return null;
        }

        /// <summary>NPCs the scene roster places in the current scene.</summary>
        public List<string> PresentNpcIds()
        {
            var ids = new List<string>();
            Dictionary<string, object?>? scene = FindScene(SceneId);
            if (scene != null)
            {
                foreach (string id in scene.GetStringList("active_npcs"))
                    ids.Add(id);
            }
            return ids;
        }
    }

    /// <summary>Strips the server's control tags from stored narrative (same rule as the web client).</summary>
    public static class NarrativeText
    {
        private static readonly Regex DropBlocks = new Regex(
            @"<(SPEAKER|ACTIONS|STATE|WORLD|NEW_TASKS|OPTIONS)>.*?(</\1>|$)", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        private static readonly Regex SetupTags = new Regex(@"</?SETUP>", RegexOptions.CultureInvariant);
        private static readonly Regex LeftoverTags = new Regex(@"</?[A-Z_]{3,}>", RegexOptions.CultureInvariant);

        public static string Clean(string? content)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;
            string s = DropBlocks.Replace(content, string.Empty);
            s = SetupTags.Replace(s, "\n");
            s = LeftoverTags.Replace(s, string.Empty);
            return Regex.Replace(s, @"\n{3,}", "\n\n").Trim();
        }
    }
}
