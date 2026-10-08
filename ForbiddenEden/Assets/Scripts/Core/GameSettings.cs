using System;
using System.IO;
using UnityEngine;

namespace ForbiddenEden.Core
{
    /// <summary>Player-side settings, saved as JSON under the user data folder.</summary>
    [Serializable]
    public sealed class GameSettings
    {
        public const string DefaultServer = "http://127.0.0.1:3905";

        public string serverUrl = DefaultServer;
        /// <summary>zh-TW, zh-CN, ja, en, vi, th — what POST /api/lang accepts.</summary>
        public string lang = "zh-TW";
        public float fontScale = 1f;
        /// <summary>Characters per second for the dialogue typewriter; 0 shows text at once.</summary>
        public float textSpeed = 40f;
        public float bgmVolume = 0.7f;
        public float sfxVolume = 0.8f;
        public float voiceVolume = 0.8f;
        public float cameraSpeed = 1f;
        public bool cameraFollow = true;
        public int qualityLevel = 1; // 0 效能 / 1 標準 / 2 高畫質
        public bool fullscreen = true;
        public int resolutionIndex = -1;
        public bool ageConfirmed;
        public string deviceUid = string.Empty;
        /// <summary>anon or member.</summary>
        public string accountMode = "anon";
        public string memberToken = string.Empty;
        public string titleCharacter = "rotate";
        /// <summary>Last session id this device played, for 繼續.</summary>
        public string lastSessionId = string.Empty;

        public static string UserDataDir
        {
            get
            {
                string dir = Path.Combine(Application.persistentDataPath, "UserData");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string CardsDir => Ensure(Path.Combine(UserDataDir, "cards"));
        public static string PhotosDir => Ensure(Path.Combine(UserDataDir, "photos"));
        public static string LogsDir => Ensure(Path.Combine(UserDataDir, "logs"));

        private static string FilePath => Path.Combine(UserDataDir, "settings.json");

        private static string Ensure(string dir)
        {
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static GameSettings Load()
        {
            GameSettings s = null;
            try
            {
                if (File.Exists(FilePath))
                    s = JsonUtility.FromJson<GameSettings>(File.ReadAllText(FilePath));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("settings.json unreadable, using defaults: " + ex.Message);
            }
            s ??= new GameSettings();
            if (string.IsNullOrEmpty(s.deviceUid))
                s.deviceUid = "web-unity-" + Guid.NewGuid().ToString("N").Substring(0, 16);
            s.Save();
            return s;
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("settings.json not saved: " + ex.Message);
            }
        }

        public static readonly string[] LangCodes = { "zh-TW", "zh-CN", "ja", "en", "vi", "th" };
        public static readonly string[] LangNames = { "繁體中文", "简体中文", "日本語", "English", "Tiếng Việt", "ไทย" };
        public static readonly string[] QualityNames = { "效能", "標準", "高畫質" };

        public static string LangName(string code)
        {
            int i = Array.IndexOf(LangCodes, code);
            return i >= 0 ? LangNames[i] : code;
        }
    }
}
