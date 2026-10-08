"""超禁忌學園 ～EDEN～ 離線模擬伺服器（給 Unity 客戶端預覽用）。

Wire formats follow ai-agent-server (internal/web/*.go). No LLM, no persistence across restarts.
Extended from the 1-Cut EdenMock with every endpoint the Unity screens call.

    python eden_mock_server.py [--port 3905] [--token-delay 0.025] [--coins 300]

Scripted behaviour:
  - input containing 越獄         -> 403 blocked_input (in-world refusal)
  - input containing 事件         -> the turn ends with a pending choice
  - input containing 排名 / 對決  -> story_phase becomes contest for three rounds
  - input containing 演出         -> a cinematic turn with a single 「▶ 繼續」
  - input containing 故事         -> a long reply that needs several dialogue pages
  - each turn costs 1 EP          -> 402 no_coins when it runs out (start with --coins 3 to see it)
"""

import argparse
import json
import queue
import random
import threading
import time
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ARGS = None
LOCK = threading.Lock()
SESSIONS = {}
DEVICE_TO_SID = {}
SAVES = {}  # device_uid -> list of save dicts

NPC_ROSTER = [
    {"npc_id": "npc_kujo_makoto", "name": "九條真", "gender": "女", "class": "3-A・新聞社", "surface_identity": "新聞社社長，說話很輕，但每句都像記下來了。（19 歲）", "speech_style": "冷靜"},
    {"npc_id": "npc_amamiya_mio", "name": "雨宮澪", "gender": "女", "class": "3-B・音樂社", "surface_identity": "音樂社的鍵盤手，放學後總在音樂教室。（18 歲）", "speech_style": "活潑"},
    {"npc_id": "npc_kanzaki_rei", "name": "神崎零", "gender": "男", "class": "3-S・學生會", "surface_identity": "學生會長，登錄日當天負責迎新。（20 歲）", "speech_style": "正式"},
    {"npc_id": "npc_tsukishiro_kasumi", "name": "月城霞", "gender": "女", "class": "3-B・心理研究社", "surface_identity": "心理研究社，常在圖書館最裡面的位子。（18 歲）", "speech_style": "輕柔"},
    {"npc_id": "npc_tachibana_misaki", "name": "橘美咲", "gender": "女", "class": "2-C・表演社", "surface_identity": "表演社的新星，排練從不缺席。（18 歲）", "speech_style": "開朗"},
    {"npc_id": "npc_kurosawa_hayato", "name": "黑澤隼人", "gender": "男", "class": "3-D・競技社", "surface_identity": "競技社王牌，排名戰的常勝者。（19 歲）", "speech_style": "直率"},
    {"npc_id": "npc_ibuki_ren", "name": "伊吹蓮", "gender": "男", "class": "3-D・競技社", "surface_identity": "隼人的搭檔，總在場邊提醒別人別急。（19 歲）", "speech_style": "沉穩"},
    {"npc_id": "npc_ava7", "name": "艾娃-7", "gender": "女", "class": "學園系統", "surface_identity": "學園的導覽 AI，以全像投影出現。（設定年齡 20 歲）", "speech_style": "機械"},
]

SCENES = [
    ("scene_registry", "新生登錄中心", ["scene_plaza"], ["npc_kanzaki_rei", "npc_ava7"], "新生在這裡領取學園終端。"),
    ("scene_plaza", "中央學園城・廣場", ["scene_registry", "scene_classroom", "scene_staffroom", "scene_pool", "scene_club", "scene_library", "scene_dorm", "scene_arena"], [], "噴水池旁總有人在等人。"),
    ("scene_classroom", "3-A 教室", ["scene_plaza", "scene_rooftop"], ["npc_kujo_makoto"], "午後的教室，黑板上還留著上一堂課的板書。"),
    ("scene_club", "社團館", ["scene_plaza"], ["npc_tachibana_misaki", "npc_amamiya_mio"], "聽說有人在競技塔待到熄燈。"),
    ("scene_library", "圖書館", ["scene_plaza"], ["npc_tsukishiro_kasumi"], "最裡面的位子好像一直是同一個人。"),
    ("scene_dorm", "宿舍生活區", ["scene_plaza"], [], "熄燈前的走廊很安靜。"),
    ("scene_arena", "天空競技塔", ["scene_plaza"], ["npc_kurosawa_hayato", "npc_ibuki_ren"], "排名戰的報名表貼滿了入口。"),
    ("scene_rooftop", "教學樓屋頂", ["scene_classroom"], [], "從這裡可以看見整座浮島。"),
    ("scene_staffroom", "職員室", ["scene_plaza", "scene_classroom"], ["npc_kanzaki_rei"], "放學後的職員室只剩一盞燈，桌上堆著登錄日的名冊。"),
    ("scene_pool", "夜間泳池", ["scene_plaza"], ["npc_tachibana_misaki"], "熄燈後的泳池還亮著彩燈，水面上漂著一張浮墊。"),
]
SCENE_ROSTER = [{"scene_id": s[0], "name": s[1], "exits": s[2], "active_npcs": s[3], "normal_description": s[4]} for s in SCENES]
SCENE_NAME = {s[0]: s[1] for s in SCENES}

PROFESSIONS = [
    {"id": "honor", "name": "優等生・被期待者", "icon": "🎖", "scene": "scene_classroom", "npc": "npc_kujo_makoto", "npc_role": "新聞社社長", "event_name": "被點名的新生", "event_desc": "開學第一天，你的名字就出現在新聞社的版面上。"},
    {"id": "transfer", "name": "轉學生・外來者", "icon": "🧳", "scene": "scene_registry", "npc": "npc_kanzaki_rei", "npc_role": "學生會長", "event_name": "遲到的登錄", "event_desc": "你錯過了正式登錄，只能在閉館前找到會長。"},
    {"id": "rebel", "name": "問題學生・不服從者", "icon": "⚡", "scene": "scene_club", "npc": "npc_kujo_makoto", "npc_role": "新聞社社長", "event_name": "社團館的爭論", "event_desc": "商業娛樂環的傳單事件，讓你被盯上了。"},
    {"id": "athlete", "name": "運動型・衝鋒者", "icon": "🏃", "scene": "scene_arena", "npc": "npc_kurosawa_hayato", "npc_role": "競技社王牌", "event_name": "挑戰書", "event_desc": "競技塔入口，有人把挑戰書塞進你手裡。"},
    {"id": "heir", "name": "富家子弟・被供養者", "icon": "💎", "scene": "scene_dorm", "npc": "npc_amamiya_mio", "npc_role": "音樂社鍵盤手", "event_name": "隔壁的琴聲", "event_desc": "宿舍隔壁傳來琴聲，一直彈到熄燈。"},
    {"id": "watched", "name": "觀察候選・被標記者", "icon": "👁", "scene": "scene_library", "npc": "npc_tsukishiro_kasumi", "npc_role": "心理研究社", "event_name": "同一本書", "event_desc": "你和她在圖書館借走了同一本書。"},
]

MARKS = [
    {"code": "hun", "glyph": "魂", "name": "歸憶之印", "tagline": "你記得的，比你以為的還多。", "echo": "你記得的，比你以為的還多。"},
    {"code": "yan", "glyph": "焰", "name": "燃燼之印", "tagline": "你不等別人允許。", "echo": "你不等別人允許，火總是先從你這裡燒起來。"},
    {"code": "jing", "glyph": "鏡", "name": "映照之印", "tagline": "你看見別人看不見的那一面。", "echo": "你看見別人看不見的那一面，也因此很少說出口。"},
]
QUESTIONS = [
    (1, "登錄中心的人問你：「來到這裡之前，你最常想起的是什麼？」", ["一個沒說完的約定", "一場還沒打完的比賽", "一扇一直關著的門"]),
    (2, "走廊上有人在吵架，你會？", ["上前問清楚發生什麼事", "站在旁邊，記下誰說了什麼", "繞路，假裝沒看見"]),
    (3, "如果只能帶一樣東西進學園，你會帶？", ["一張舊照片", "一雙跑鞋", "一本寫滿字的筆記"]),
    (4, "你希望三年後，別人怎麼說起你？", ["「他一直都在」", "「他贏過所有人」", "「沒有人真的了解他」"]),
]
POEM = ["天空沒有盡頭。", "", "在雲層之上，", "有一座不屬於任何地圖的學園。", "", "你在鐘聲裡醒來——"]

NAMES_M = ["林予安", "周以恆", "陳柏宇", "許晨曦", "江承翰", "葉知秋"]
NAMES_F = ["林雨晴", "蘇若瑜", "許文玲", "沈語彤", "白芷晴", "季安然"]

CAMERA = {
    "shots": {"version": "1.0", "categories": [{"id": "A", "name": "人物", "range": "A01–A04"}, {"id": "R", "name": "關係", "range": "R01–R05"}],
              "shots": [{"id": i, "category": i[0], "name_zh": n} for i, n in [("A01", "特寫"), ("A02", "近景"), ("A03", "中景"), ("A04", "全身"), ("R01", "雙人遠景"), ("R02", "群像"), ("R03", "環境"), ("R04", "過肩"), ("R05", "對峙")]]},
    "angles": {"version": "1.0", "angles": [{"id": i, "name_zh": n} for i, n in [("F", "正面"), ("F3Q", "前斜側"), ("SIDE", "側面"), ("R3Q", "後斜側"), ("REAR", "背面"), ("HIGH", "俯角"), ("LOW", "仰角"), ("TOP", "頂視")]]},
    "motions": {"version": "1.0", "motions": [{"id": "M0", "name_zh": "靜止"}, {"id": "M1", "name_zh": "緩推"}]},
    "presets": {"version": "1.0", "presets": [
        {"id": "P01", "name_zh": "初遇・正面中景", "shot": "A03", "angle": "F", "motion": "M0"},
        {"id": "P02", "name_zh": "低語・特寫", "shot": "A01", "angle": "F3Q", "motion": "M1"},
        {"id": "P03", "name_zh": "對峙・側面", "shot": "R05", "angle": "SIDE", "motion": "M0"},
        {"id": "P04", "name_zh": "仰望・全身", "shot": "A04", "angle": "LOW", "motion": "M0"},
        {"id": "P05", "name_zh": "俯瞰・環境", "shot": "R03", "angle": "HIGH", "motion": "M1"},
        {"id": "P06", "name_zh": "背影・遠景", "shot": "R01", "angle": "REAR", "motion": "M0"},
        {"id": "P07", "name_zh": "近景・前斜側", "shot": "A02", "angle": "F3Q", "motion": "M0"},
        {"id": "P08", "name_zh": "過肩對話", "shot": "R04", "angle": "R3Q", "motion": "M0"},
    ]},
}


def sse(obj):
    return ("data: " + json.dumps(obj, ensure_ascii=False, separators=(",", ":")) + "\n\n").encode("utf-8")


def typed(t, content):
    return sse({"type": t, "content": content})


def npc_name(npc_id):
    return next((n["name"] for n in NPC_ROSTER if n["npc_id"] == npc_id), npc_id)


def new_session(req, sid):
    prof = next((p for p in PROFESSIONS if p["id"] == req.get("player_profession_id")), PROFESSIONS[2])
    scene = req.get("start_scene") or prof["scene"]
    return {
        "sid": sid, "device": req.get("device_uid", ""),
        "player_name": req.get("player_name") or "旅人", "gender": req.get("player_gender") or "男",
        "character_name": req.get("character_name") or npc_name(prof["npc"]), "profession": prof,
        "scene": scene, "coins": ARGS.coins, "ap": 12, "stamina": 80,
        "turn": 0, "day": 1, "period_i": 2, "beat": 1, "cinematic": False,
        "contest_round": 0, "delta": "", "phase": "daily",
        "options": ["【社交】「你好，我是新來的。」", "【推理】「這裡發生過什麼事？」", "【探查】看看四周", "【冒險】「帶我去看看。」"],
        "pending": None, "history": [], "events": queue.Queue(), "checkpoint": None,
        "inventory": [
            {"id": "it_bar", "name": "能量棒", "cat": "consumable", "qty": 3, "desc": "學園福利社的招牌。", "effect": "行動力 +3、體力 +30", "icon": "🍫", "ops": ["use", "discard"]},
            {"id": "it_flyer", "name": "社團招募單", "cat": "key", "qty": 1, "desc": "招募月期間可以交給社團。", "effect": "", "icon": "📄", "ops": []},
            {"id": "it_terminal", "name": "個人終端", "cat": "key", "qty": 1, "desc": "新生登錄時發的終端。", "effect": "", "icon": "📱", "ops": []},
        ],
        "quests_active": [{"quest_id": "HS-M2", "name": "招募月：加入一個社團", "steps": [("在社團館看看招募單", True), ("和至少兩個社團的人談過", False), ("遞交招募單", False)]}],
        "quests_available": [{"quest_id": "HS-S1", "name": "音樂教室的琴聲", "giver": "npc_amamiya_mio"}, {"quest_id": "HS-S2", "name": "新聞社的匿名投稿", "giver": "npc_kujo_makoto"}],
    }


PERIODS = ["早晨", "上午", "午後", "傍晚", "夜晚"]


def state(s, full=False):
    st = {
        "cinematic": s["cinematic"], "stage_title": "第一章 入城", "player_name": s["player_name"],
        "character_name": s["character_name"], "main_quest": "", "affection": 0, "turn_count": s["turn"],
        "current_scene": s["scene"], "scene_name": SCENE_NAME.get(s["scene"], s["scene"]), "total_points": 0,
        "inventory": [i["name"] for i in s["inventory"]], "emotion": s.get("emotion", "neutral"), "emotion_emoji": "😐", "mode": 0,
        "player_coins": s["coins"], "player_ap": s["ap"], "player_ap_max": 20, "player_stamina": s["stamina"], "player_stamina_max": 100,
        "game_mode": "story", "companions": None, "chapter_title": "第一章 入城", "day_count": s["day"],
        "current_period": PERIODS[s["period_i"]], "clock_label": "第 %d 天 %s" % (s["day"], PERIODS[s["period_i"]]),
        "character_status": "", "character_mood": "", "player_dead": False, "no_advance_streak": 0,
        "story_phase": s["phase"], "story_phase_label": {"daily": "日常", "event": "事件", "contest": "對抗"}[s["phase"]],
        "beat": {"quest_id": "HS-M2", "quest_name": "招募月", "seq": s["beat"], "total": 5, "goal": "在招募月結束前做出選擇", "mode": s["phase"]},
        "last_turn_delta": s["delta"],
    }
    if s["checkpoint"]:
        st["chapter_checkpoint"] = s["checkpoint"]
    if full:
        st.update({"exists": True, "options": s["options"], "lang": "", "content_rating": 0, "visited_scenes": [s["scene"]],
                   "player_gender": s["gender"], "player_profession_id": s["profession"]["id"],
                   "player_profession_name": s["profession"]["name"], "chapter_end_ready": False,
                   "active_tasks": [], "completed_tasks": [], "key_events": []})
        if s["phase"] == "contest":
            st["opponent"] = "黑澤隼人"
    else:
        st["speaker_name"] = s["character_name"]
        st["speaker_hue"] = 280
    return st


def record_save(s):
    lst = SAVES.setdefault(s["device"], [])
    save_id = "state_memory_%s.json" % s["sid"].replace("/", "_")
    entry = {"save_id": save_id, "player_name": s["player_name"], "character_name": s["character_name"], "scene": s["scene"],
             "scene_name": SCENE_NAME.get(s["scene"], s["scene"]), "profession_id": s["profession"]["id"],
             "turn_count": s["turn"], "message_count": len(s["history"]), "updated_unix": int(time.time()), "_sid": s["sid"]}
    lst[:] = [e for e in lst if e["save_id"] != save_id]
    lst.insert(0, entry)


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        print("[mock]", fmt % args, flush=True)

    def parsed(self):
        u = urllib.parse.urlsplit(self.path)
        return u.path, urllib.parse.parse_qs(u.query)

    def send_json(self, status, obj, headers=None):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Access-Control-Allow-Origin", "*")
        for k, v in (headers or {}).items():
            self.send_header(k, v)
        self.end_headers()
        self.wfile.write(body)

    def read_body(self):
        n = int(self.headers.get("Content-Length") or 0)
        return self.rfile.read(n) if n else b""

    def sid(self, q):
        return self.headers.get("X-Session-ID") or (q.get("sid") or [""])[0]

    def session(self, q):
        s = SESSIONS.get(self.sid(q))
        if s is None:
            self.send_json(401, {"error": "no_session", "message": "請先建立遊戲"})
        return s

    def begin_sse(self):
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.send_header("Cache-Control", "no-cache")
        self.send_header("Connection", "close")
        self.end_headers()
        self.close_connection = True

    def write(self, data):
        self.wfile.write(data)
        self.wfile.flush()

    # ---- GET ---------------------------------------------------------------------------

    def do_GET(self):
        path, q = self.parsed()
        if path == "/api/version":
            return self.send_json(200, {"world_id": "hschool", "api_version": "v0", "commit": "mock", "build_time": "", "go_version": "mock", "worldpack_hash": "000000000000"})
        if path == "/api/npc/roster":
            return self.send_json(200, [dict(n, aliases=[], portrait=n["npc_id"], model_key=n["npc_id"]) for n in NPC_ROSTER])
        if path == "/api/scene/roster":
            return self.send_json(200, SCENE_ROSTER)
        if path == "/api/onboarding/config":
            rows = [{"no": no, "question": text, "seq": i + 1, "text": a} for no, text, answers in QUESTIONS for i, a in enumerate(answers)]
            return self.send_json(200, {"marks": MARKS, "sysnames": [], "poem": [{"order": i + 1, "text": t, "type": "gap" if not t else "line"} for i, t in enumerate(POEM)],
                                        "login": {}, "questions": rows})
        if path == "/api/solo/random_name":
            g = (q.get("gender") or ["male"])[0]
            return self.send_json(200, {"name": random.choice(NAMES_F if g == "female" else NAMES_M), "gender": g, "lang": "zh-Hant", "tries": 1})
        if path == "/api/solo/professions":
            return self.send_json(200, [dict(p, scene_name=SCENE_NAME[p["scene"]], alt_scene="", alt_scene_name="", event_code="EVT", reward="", next="",
                                             portrait_male="player_M1.png", portrait_female="player_F1.png") for p in PROFESSIONS])
        if path == "/api/solo/blacklist_names":
            names = sorted(n["name"] for n in NPC_ROSTER)
            return self.send_json(200, {"count": len(names), "names": names})
        if path == "/api/solo/saves":
            uid = (q.get("device_uid") or [""])[0]
            return self.send_json(200, [{k: v for k, v in e.items() if not k.startswith("_")} for e in SAVES.get(uid, [])])
        if path.startswith("/api/camera/"):
            kind = path.rsplit("/", 1)[-1]
            if kind not in CAMERA:
                return self.send_json(404, {"error": "not_found", "message": "kind 須為 shots|angles|motions|presets"})
            return self.send_json(200, CAMERA[kind])
        if path == "/api/player/auto_login":
            uid = (q.get("device_uid") or [""])[0].strip()
            if not uid:
                return self.send_json(400, {"error": "缺少 device_uid 參數"})
            s = SESSIONS.get(DEVICE_TO_SID.get(uid, ""))
            if s is None:
                return self.send_json(200, {"has_session": False})
            return self.send_json(200, {"has_session": True, "session_id": s["sid"], "player_name": s["player_name"], "current_scene": s["scene"], "is_solo": True})
        if path == "/api/state":
            s = SESSIONS.get(self.sid(q))
            return self.send_json(200, state(s, full=True) if s else {"exists": False})
        if path == "/api/history":
            s = self.session(q)
            if s is None:
                return
            limit = int((q.get("limit") or ["0"])[0] or 0)
            msgs = s["history"][-limit:] if limit else s["history"]
            return self.send_json(200, {"mode": 0, "character_name": s["character_name"], "player_name": s["player_name"],
                                        "initial_event": s.get("opening", ""), "initial_message": "", "is_first_turn": s["turn"] == 0,
                                        "era_summary": "", "chapter_summary": "", "total_messages": len(s["history"]), "truncated": False, "messages": msgs})
        if path == "/api/events/pending-choices":
            s = self.session(q)
            if s is None:
                return
            return self.send_json(200, {"pending": s["pending"]})
        if path == "/api/events":
            s = self.session(q)
            if s is None:
                return
            return self.events_stream(s)
        if path == "/api/scene/movable":
            s = self.session(q)
            if s is None:
                return
            exits = next(x["exits"] for x in SCENE_ROSTER if x["scene_id"] == s["scene"])
            return self.send_json(200, {"current_scene": s["scene"], "current_scene_name": SCENE_NAME[s["scene"]],
                                        "movable": [{"scene_id": e, "name": e, "scene_name": SCENE_NAME[e]} for e in exits]})
        if path == "/api/inventory/live":
            s = self.session(q)
            if s is None:
                return
            return self.send_json(200, {"capacity": {"used": sum(i["qty"] for i in s["inventory"]), "max": 30}, "coins": s["coins"], "items": s["inventory"]})
        if path == "/api/quest/player/active":
            s = self.session(q)
            if s is None:
                return
            quests = [{"quest_id": x["quest_id"], "status": "ACTIVE", "display": {"name_zh": x["name"]}, "description": x["name"],
                       "current_step": 1, "category": "main",
                       "step_progress": [{"step_no": i + 1, "title": t, "status": "DONE" if d else "PENDING"} for i, (t, d) in enumerate(x["steps"])]}
                      for x in s["quests_active"]]
            return self.send_json(200, {"count": len(quests), "max_concurrent": 5, "quests": quests})
        if path == "/api/quest/player/available":
            s = self.session(q)
            if s is None:
                return
            items = [{"quest_id": x["quest_id"], "quest_name": x["name"], "status": "AVAILABLE", "giver_npc_id": x["giver"]} for x in s["quests_available"]]
            return self.send_json(200, {"count": len(items), "counts": {"available": len(items)}, "items": items})
        if path == "/api/quest/player/history":
            return self.send_json(200, {"total": 0, "limit": 20, "offset": 0, "items": []})
        self.send_json(404, {"error": "not_found"})

    # ---- POST --------------------------------------------------------------------------

    def do_POST(self):
        path, q = self.parsed()
        raw = self.read_body()
        try:
            body = json.loads(raw.decode("utf-8")) if raw else {}
        except ValueError:
            return self.send_json(400, {"error": "invalid request"})

        if path == "/api/onboarding/score":
            answers = body.get("questionnaire")
            if not isinstance(answers, list) or len(answers) != len(QUESTIONS):
                return self.send_json(400, {"error": "bad_request", "message": "questionnaire"})
            mark = MARKS[sum(answers) % len(MARKS)]
            return self.send_json(200, {"mark": mark["code"], "mark_name": mark["name"], "mark_glyph": mark["glyph"], "mark_echo": mark["echo"]})

        if path == "/api/init":
            gender = body.get("player_gender", "")
            if gender not in ("", "男", "女", "male", "female"):
                return self.send_json(400, {"error": "bad_request", "message": "player_gender"})
            time.sleep(0.8)
            uid = (body.get("device_uid") or "anon").replace("web-", "", 1)
            s = new_session(body, None)
            sid = "web_%s_%s_%s" % (uid, s["player_name"], s["character_name"])
            s["sid"] = sid
            SESSIONS[sid] = s
            if body.get("device_uid"):
                DEVICE_TO_SID[body["device_uid"]] = sid
            prof = s["profession"]
            opening = ("鐘聲在雲層上方迴盪。你在%s醒來，%s正低頭看著你。\n「%s」%s說，「你就是那個%s吧？」"
                       % (SCENE_NAME[s["scene"]], s["character_name"], prof["event_desc"], s["character_name"], prof["name"].split("・")[0]))
            s["opening"] = opening
            record_save(s)
            return self.send_json(200, {"status": "ok", "session_id": sid, "initial_event": opening, "initial_options": s["options"],
                                        "message": "遊戲已建立：%s 與 %s 在 %s" % (s["player_name"], s["character_name"], SCENE_NAME[s["scene"]]),
                                        "assigned_quest": {"quest_id": "HS-M2", "name_zh": "招募月", "brief_zh": "加入一個社團"}},
                                  {"Set-Cookie": "session_id=%s; Path=/; HttpOnly" % urllib.parse.quote(sid)})

        if path == "/api/solo/load":
            uid = body.get("device_uid", "")
            entry = next((e for e in SAVES.get(uid, []) if e["save_id"] == body.get("save_id")), None)
            if entry is None:
                return self.send_json(404, {"error": "存檔不存在"})
            DEVICE_TO_SID[uid] = entry["_sid"]
            s = SESSIONS[entry["_sid"]]
            return self.send_json(200, {"status": "ok", "session_id": entry["_sid"], "player_name": s["player_name"],
                                        "character_name": s["character_name"], "scene": s["scene"], "message": "已接回進行中的存檔"})

        s = self.session(q)
        if s is None:
            return

        if path == "/api/events/choose":
            return self.resolve_choice(s, (body.get("option_id") or "").strip(), via_chat=False)
        if path == "/api/lang":
            if body.get("lang") not in ("zh-TW", "zh-CN", "ja", "en", "vi", "th"):
                return self.send_json(400, {"error": "unsupported_lang"})
            return self.send_json(200, {"lang": body["lang"]})
        if path == "/api/scene/move":
            target = body.get("scene_id") or body.get("scene") or ""
            if target == s["scene"]:
                return self.send_json(400, {"ok": False, "error": "same_scene", "message": "你已經在這裡了"})
            exits = next(x["exits"] for x in SCENE_ROSTER if x["scene_id"] == s["scene"])
            if target not in exits:
                return self.send_json(422, {"ok": False, "error": "not_adjacent", "message": "無法從這裡直達該地點", "from": s["scene"], "to": target, "exits": exits})
            prev, s["scene"] = s["scene"], target
            record_save(s)
            return self.send_json(200, {"ok": True, "scene": target, "scene_name": SCENE_NAME[target], "prev_scene": prev, "message": "【🏃 移動】前往 " + SCENE_NAME[target]})
        if path == "/api/inventory/use":
            item = next((i for i in s["inventory"] if i["name"] == body.get("name")), None)
            if item is None:
                return self.send_json(400, {"error": "item_not_in_bag", "message": "行囊裡沒有這個道具"})
            if "use" not in item["ops"]:
                return self.send_json(400, {"error": "not_usable", "message": "這個道具不能直接使用"})
            item["qty"] -= 1
            if item["qty"] <= 0:
                s["inventory"].remove(item)
            s["ap"] = min(20, s["ap"] + 3)
            s["stamina"] = min(100, s["stamina"] + 30)
            return self.send_json(200, {"ok": True, "name": item["name"], "message": "服用「%s」：行動力 +3、體力 +30" % item["name"],
                                        "player_ap": s["ap"], "ap_max": 20, "player_stamina": s["stamina"], "stamina_max": 100})
        if path.startswith("/api/quest/player/"):
            parts = path.split("/")
            verb, qid = parts[4], urllib.parse.unquote(parts[5]) if len(parts) > 5 else ""
            if verb == "accept":
                x = next((a for a in s["quests_available"] if a["quest_id"] == qid), None)
                if x is None:
                    return self.send_json(404, {"error": "quest_not_found", "quest_id": qid})
                s["quests_available"].remove(x)
                s["quests_active"].append({"quest_id": qid, "name": x["name"], "steps": [("和委託人談談", False)]})
                return self.send_json(200, {"ok": True, "quest_id": qid, "new_status": "ACTIVE", "accepted_at_turn": s["turn"], "task_id": "qr_%s" % qid})
            if verb == "abandon":
                if qid.startswith("HS-M"):
                    return self.send_json(409, {"error": "abandon_forbidden", "message": "主線任務不能放棄"})
                s["quests_active"] = [a for a in s["quests_active"] if a["quest_id"] != qid]
                return self.send_json(200, {"ok": True, "quest_id": qid, "new_status": "ABANDONED", "abandoned_at_turn": s["turn"]})
            if verb == "complete":
                x = next((a for a in s["quests_active"] if a["quest_id"] == qid), None)
                if x is None:
                    return self.send_json(409, {"error": "not_active", "quest_id": qid})
                todo = [t for t, d in x["steps"] if not d]
                if todo:
                    return self.send_json(409, {"error": "objectives_not_met", "quest_id": qid, "message": "尚有 %d 個步驟未完成：%s" % (len(todo), "、".join(todo))})
                s["quests_active"].remove(x)
                return self.send_json(200, {"ok": True, "quest_id": qid, "grade": "A", "rewards_granted": {"exp": 10, "coin": 0}})
        if path.startswith("/api/stage/clear-ack/"):
            return self.send_json(200, {"ok": True, "stage_id": path.rsplit("/", 1)[-1]})
        if path == "/api/chat":
            text = body.get("input") or ""
            if not text:
                return self.send_json(400, {"error": "missing input"})
            if s["pending"] is not None:
                pc = s["pending"]
                oid = ""
                if text.isdigit() and 1 <= int(text) <= len(pc["options"]):
                    oid = pc["options"][int(text) - 1]["id"]
                if not oid:
                    return self.send_json(409, {"ok": False, "error": "pending_choice", "pending": pc, "message": "⏸️ 突發事件「%s」等待你的介入。" % pc["event_name"]})
                return self.resolve_choice(s, oid, via_chat=True)
            if s["coins"] <= 0:
                return self.send_json(402, {"error": "no_coins", "message": "EP 已用盡,需儲值才能繼續對話", "coins": 0})
            if "越獄" in text:
                return self.send_json(403, {"error": "blocked_input", "category": "jailbreak", "message": "%s皺起眉頭：「你在說什麼奇怪的話？」" % s["character_name"]})
            return self.chat_stream(s, text)
        self.send_json(404, {"error": "not_found"})

    # ---- behaviours ----------------------------------------------------------------------

    def resolve_choice(self, s, oid, via_chat):
        pc = s["pending"]
        if pc is None:
            return self.send_json(400, {"ok": False, "error": "no_pending"})
        opt = next((o for o in pc["options"] if o["id"] == oid), None)
        if opt is None:
            return self.send_json(409, {"ok": False, "error": "option_not_found", "pending": pc})
        s["pending"] = None
        result = {"ok": True, "event_id": pc["event_id"], "event_name": pc["event_name"], "chosen_id": opt["id"], "chosen_text": opt["text"],
                  "chosen_icon": opt["icon"], "narrative": "你選擇了「%s」。鐘聲停了，走廊又恢復了安靜。" % opt["text"], "diff": None}
        s["events"].put(("choice_resolved", json.dumps(result, ensure_ascii=False)))
        if via_chat:
            result = dict(result, type="choice_resolved")
        return self.send_json(200, result)

    def chat_stream(self, s, text):
        if text.isdigit() and 1 <= int(text) <= len(s["options"]):
            text = s["options"][int(text) - 1]
        name = s["character_name"]
        s["coins"] -= 1
        s["turn"] += 1
        # Emotion tag for the speaker's face: keyword first, otherwise a rotation through the Plutchik set.
        moods = [("笑", "joy"), ("開心", "joy"), ("生氣", "anger"), ("難過", "sadness"), ("怕", "fear"), ("驚", "surprise"), ("事件", "surprise"), ("排名", "anticipation"), ("對決", "anticipation")]
        s["emotion"] = next((m for k, m in moods if k in text), ["joy", "trust", "neutral", "sadness", "anger", "surprise"][s["turn"] % 6])
        s["checkpoint"] = None
        if s["turn"] % 3 == 0:
            s["period_i"] = (s["period_i"] + 1) % len(PERIODS)
            if s["period_i"] == 0:
                s["day"] += 1
        was_cin = s["cinematic"]
        s["cinematic"] = "演出" in text and not was_cin
        if ("排名" in text or "對決" in text) and s["phase"] != "contest":
            s["phase"], s["contest_round"] = "contest", 0
        if s["phase"] == "contest":
            s["contest_round"] += 1
            r = s["contest_round"]
            if r >= 3:
                s["delta"] = "對抗：第 %d 回合／勝" % r
                reply = "隼人的腳步亂了一拍。你抓住那一瞬間，場邊的伊吹蓮低聲說：「就是現在。」裁判舉起了你的手。"
                s["options"] = ["【社交】向隼人伸出手", "【推理】「你剛剛是故意的？」", "【探查】看向場邊的教官"]
                s["phase"], s["contest_round"] = "daily", 0
                s["checkpoint"] = {"quest_id": "HS-M3", "quest_name": "排名戰・初戰", "source": "beat:contest", "turn": s["turn"]}
                s["beat"] = min(5, s["beat"] + 1)
            else:
                s["delta"] = "對抗：第 %d 回合" % r
                reply = "黑澤隼人換了腳步，假動作比剛才更快。場邊傳來一陣低語。"
                s["options"] = ["【正面】正面迎上", "【守勢】拉開距離觀察", "【取巧】假裝失誤引他出手", "【退讓】先退一步"]
        elif s["cinematic"]:
            s["delta"] = ""
            reply = "（鐘聲響起。窗外的天空都市緩緩亮起燈火，%s沒有說話，只是望著遠方。）" % name
            s["options"] = ["▶ 繼續"]
        elif "故事" in text:
            s["delta"] = "學園的傳聞多了一則"
            reply = ("%s把手中的筆記本闔上，想了一下才開口：「很久以前，天空都市還沒有現在這麼多規矩的時候，這座學園只是一座觀測站。"
                     "每天傍晚，研究員們會在屋頂上記錄雲的形狀。據說有一位研究員發現，雲層裡偶爾會浮現不屬於任何人的訊號。」"
                     "她停頓了一下，看向你。「那個訊號後來被命名為 ECHO。沒有人知道它從哪裡來，也沒有人知道它想說什麼。"
                     "有人說它是某種警告，也有人說它只是在模仿我們。」她輕輕笑了。「你相信哪一種？」" % name)
            s["options"] = ["【社交】「是警告吧。」", "【推理】「只是模仿。」", "【探查】「後來呢？」"]
        else:
            s["delta"] = "社團館的布告欄換了一整面"
            s["beat"] = min(5, s["beat"] + (1 if s["turn"] % 2 == 0 else 0))
            reply = "%s輕輕點頭：「%s？……嗯，我聽到了。」她把一張招募單推到你面前。" % (name, text.split("】")[-1][:24])
            s["options"] = ["【社交】「這是什麼社團？」", "【推理】「為什麼找上我？」", "【探查】看看招募單背面", "【冒險】「要不要來一場排名對決？」"]
        s["history"].append({"role": "user", "content": text})
        s["history"].append({"role": "assistant", "content": reply + "\n<SPEAKER>%s</SPEAKER>" % name, "speaker_name": name, "speaker_hue": 280})
        record_save(s)

        self.begin_sse()
        try:
            self.write(typed("system", "token 統計：prompt=0 completion=%d" % len(reply)))
            time.sleep(0.6)  # time to first token
            for ch in reply:
                data = typed("token", ch)
                cut = len(data) // 2
                self.write(data[:cut])
                time.sleep(ARGS.token_delay / 2)
                self.write(data[cut:])
                time.sleep(ARGS.token_delay / 2)
            self.write(typed("state_change", "\n  💗 [%s 好感 +3]" % name))
            if "事件" in text:
                s["pending"] = {"event_id": "ev_bell", "event_name": "突然的鐘聲", "category": "sudden", "source": "preset", "fired_at_turn": s["turn"],
                                "prompt_inject": "校舍的鐘聲不合時宜地響起，走廊上的人都停下了腳步。",
                                "options": [{"id": "opt_listen", "text": "停下來聽", "icon": "👂"}, {"id": "opt_run", "text": "衝去看看", "icon": "🏃"},
                                            {"id": "opt_ignore", "text": "當作沒聽到", "icon": "🙈", "is_default": True}]}
                payload = json.dumps(s["pending"], ensure_ascii=False)
                self.write(typed("pending_choice", payload))
            self.write(sse({"type": "options", "options": s["options"]}))
            self.write(sse({"type": "done", "options": s["options"], "state": state(s)}))
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass

    def events_stream(self, s):
        self.begin_sse()
        try:
            last = time.time()
            while s["sid"] in SESSIONS:
                try:
                    kind, content = s["events"].get(timeout=0.25)
                    self.write(typed(kind, content))
                except queue.Empty:
                    pass
                if time.time() - last >= 30:
                    self.write(b": keepalive\n\n")
                    last = time.time()
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass


def main():
    global ARGS
    p = argparse.ArgumentParser()
    p.add_argument("--port", type=int, default=3905)
    p.add_argument("--token-delay", type=float, default=0.025)
    p.add_argument("--coins", type=int, default=300)
    ARGS = p.parse_args()
    server = ThreadingHTTPServer(("127.0.0.1", ARGS.port), Handler)
    server.daemon_threads = True
    print("[mock] 超禁忌學園 模擬伺服器 http://127.0.0.1:%d" % ARGS.port, flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
