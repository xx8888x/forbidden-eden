#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ForbiddenEden.Net
{
    /// <summary>
    /// Client for the ai-agent-server (超禁忌學園 ～EDEN～) player API. Ported from the 1-Cut
    /// prototype to the Unity 6 networking API. Coroutines run on <c>host</c>, so every
    /// callback arrives on the Unity main thread.
    ///
    /// Protocol notes (verified against the Go source):
    /// - The session id goes in <c>?sid=</c>; it embeds player/NPC names, which are not
    ///   safe in an HTTP header value.
    /// - A chat turn's tokens and its <c>done</c> event arrive on the <c>POST /api/chat</c>
    ///   response itself. <c>GET /api/events</c> only carries server pushes.
    /// </summary>
    public sealed class EdenApiClient
    {
        private readonly MonoBehaviour _host;
        private readonly Action<string>? _log;
        private UnityWebRequest? _chatRequest;
        private UnityWebRequest? _eventsRequest;
        private bool _eventsWanted;
        private int _eventsGeneration;

        public EdenApiClient(MonoBehaviour host, string baseUrl, Action<string>? log = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            BaseUrl = baseUrl;
            _log = log;
        }

        public string BaseUrl { get; set; }
        public string SessionId { get; set; } = string.Empty;
        /// <summary>Optional member JWT, sent as <c>Authorization: Bearer</c>.</summary>
        public string BearerToken { get; set; } = string.Empty;
        public int RequestTimeoutSeconds { get; set; } = 30;
        /// <summary>/api/init may run an LLM call for the opening before it answers.</summary>
        public int InitTimeoutSeconds { get; set; } = 180;
        /// <summary>Whole-turn cap for /api/chat; 0 disables.</summary>
        public int ChatTimeoutSeconds { get; set; } = 300;

        public bool HasSession => SessionId.Length > 0;
        public bool IsChatInFlight => _chatRequest != null;
        public bool IsEventsOpen => _eventsRequest != null;

        // ---- generic JSON endpoints ---------------------------------------------------

        /// <summary>GET returning any JSON value (object or array).</summary>
        public Coroutine Get(string pathAndQuery, bool withSession, Action<object?> onOk, Action<EdenApiError>? onError = null) =>
            _host.StartCoroutine(SendJson("GET", pathAndQuery, null, withSession, RequestTimeoutSeconds, (status, body) =>
            {
                if (EdenJson.TryParse(body, out object? value))
                    onOk(value);
                else
                    onError?.Invoke(new EdenApiError(status, "bad_json", body, null));
            }, onError));

        public Coroutine GetObject(string pathAndQuery, bool withSession, Action<Dictionary<string, object?>> onOk, Action<EdenApiError>? onError = null) =>
            _host.StartCoroutine(SendJson("GET", pathAndQuery, null, withSession, RequestTimeoutSeconds, (status, body) =>
            {
                Dictionary<string, object?>? json = EdenJson.TryParseObject(body);
                if (json == null)
                    onError?.Invoke(new EdenApiError(status, "bad_json", body, null));
                else
                    onOk(json);
            }, onError));

        public Coroutine GetList(string pathAndQuery, bool withSession, Action<List<object?>> onOk, Action<EdenApiError>? onError = null) =>
            Get(pathAndQuery, withSession, value =>
            {
                if (value is List<object?> list)
                    onOk(list);
                else if (value is Dictionary<string, object?> obj && FirstList(obj) is List<object?> inner)
                    onOk(inner); // tolerate {"items":[...]} style wrappers
                else
                    onError?.Invoke(new EdenApiError(200, "bad_json", "Expected a JSON array.", value as Dictionary<string, object?>));
            }, onError);

        /// <summary>POST a JSON body; the reply may be any JSON value (null when the body is empty).</summary>
        public Coroutine Post(string pathAndQuery, object? body, bool withSession, Action<object?> onOk, Action<EdenApiError>? onError = null, int timeout = 0) =>
            _host.StartCoroutine(SendJson("POST", pathAndQuery, body == null ? "{}" : EdenJson.Serialize(body), withSession,
                timeout > 0 ? timeout : RequestTimeoutSeconds, (status, text) =>
                {
                    if (string.IsNullOrWhiteSpace(text))
                        onOk(null);
                    else if (EdenJson.TryParse(text, out object? value))
                        onOk(value);
                    else
                        onError?.Invoke(new EdenApiError(status, "bad_json", text, null));
                }, onError));

        // ---- session ---------------------------------------------------------------------

        /// <summary>Finds an existing session for <paramref name="deviceUid"/>; adopts it as <see cref="SessionId"/> when found.</summary>
        public Coroutine AutoLogin(string deviceUid, Action<Dictionary<string, object?>> onOk, Action<EdenApiError>? onError = null) =>
            GetObject("/api/player/auto_login?device_uid=" + Uri.EscapeDataString(deviceUid), false, json =>
            {
                if (json.GetBool("has_session"))
                    SessionId = json.GetString("session_id");
                onOk(json);
            }, onError);

        public Coroutine Init(Dictionary<string, object?> body, Action<EdenInitResult> onOk, Action<EdenApiError>? onError = null) =>
            _host.StartCoroutine(SendJson("POST", "/api/init", EdenJson.Serialize(body), false, InitTimeoutSeconds, (status, text) =>
            {
                Dictionary<string, object?>? json = EdenJson.TryParseObject(text);
                var result = json == null ? null : new EdenInitResult(json);
                if (result == null || result.SessionId.Length == 0)
                {
                    onError?.Invoke(new EdenApiError(status, "bad_init_reply", text, json));
                    return;
                }
                SessionId = result.SessionId;
                onOk(result);
            }, onError));

        /// <summary>Full state, or null when the server reports <c>{"exists":false}</c>.</summary>
        public Coroutine GetState(Action<EdenStateSnapshot?> onOk, Action<EdenApiError>? onError = null) =>
            GetObject("/api/state", true, json => onOk(json.GetBool("exists") ? new EdenStateSnapshot(json) : null), onError);

        public Coroutine GetHistory(int limit, Action<List<EdenHistoryMessage>, Dictionary<string, object?>> onOk, Action<EdenApiError>? onError = null) =>
            GetObject(limit > 0 ? "/api/history?limit=" + limit : "/api/history", true, json =>
            {
                var messages = new List<EdenHistoryMessage>();
                List<object?>? list = json.GetList("messages");
                if (list != null)
                {
                    foreach (object? item in list)
                    {
                        if (item is Dictionary<string, object?> m)
                            messages.Add(new EdenHistoryMessage(m));
                    }
                }
                onOk(messages, json);
            }, onError);

        public Coroutine GetPendingChoice(Action<EdenPendingChoice?> onOk, Action<EdenApiError>? onError = null) =>
            GetObject("/api/events/pending-choices", true, json =>
            {
                Dictionary<string, object?>? pending = json.GetObject("pending");
                onOk(pending == null ? null : new EdenPendingChoice(pending));
            }, onError);

        public Coroutine Choose(string optionId, Action<EdenChoiceResolved> onOk, Action<EdenApiError>? onError = null)
        {
            string body = EdenJson.Serialize(new Dictionary<string, object?> { ["option_id"] = optionId });
            return _host.StartCoroutine(SendJson("POST", "/api/events/choose", body, true, RequestTimeoutSeconds, (status, text) =>
            {
                Dictionary<string, object?>? json = EdenJson.TryParseObject(text);
                if (json == null)
                    onError?.Invoke(new EdenApiError(status, "bad_json", text, null));
                else
                    onOk(new EdenChoiceResolved(json));
            }, onError));
        }

        // ---- streaming ---------------------------------------------------------------

        /// <summary>
        /// Sends one turn. <paramref name="input"/> may be free text or a digit "1".."9"
        /// picking the current option. Exactly one of <c>OnDone</c>, <c>OnChoiceResolved</c>
        /// (plain-JSON path) or <c>OnError</c> ends the turn.
        /// </summary>
        public bool SendChat(string input, EdenStreamHandlers handlers)
        {
            if (!HasSession)
            {
                handlers.OnError?.Invoke(new EdenApiError(0, "no_session", "Call Init or AutoLogin first.", null));
                return false;
            }
            if (_chatRequest != null)
            {
                handlers.OnError?.Invoke(new EdenApiError(0, "chat_in_flight", "A turn is already streaming.", null));
                return false;
            }
            string body = EdenJson.Serialize(new Dictionary<string, object?> { ["input"] = input });
            _host.StartCoroutine(ChatCoroutine(body, handlers));
            return true;
        }

        public void CancelChat() => _chatRequest?.Abort();

        /// <summary>
        /// Keeps <c>GET /api/events</c> open, reconnecting with backoff until
        /// <see cref="CloseEvents"/> or a 401.
        /// </summary>
        public void OpenEvents(EdenStreamHandlers handlers)
        {
            if (!HasSession)
            {
                handlers.OnError?.Invoke(new EdenApiError(0, "no_session", "Call Init or AutoLogin first.", null));
                return;
            }
            CloseEvents();
            _eventsWanted = true;
            _eventsGeneration++;
            _host.StartCoroutine(EventsLoop(handlers, _eventsGeneration));
        }

        public void CloseEvents()
        {
            _eventsWanted = false;
            _eventsGeneration++;
            _eventsRequest?.Abort();
        }

        private IEnumerator ChatCoroutine(string body, EdenStreamHandlers handlers)
        {
            var router = new EdenResponseRouter(handlers, expectDone: true);
            using (UnityWebRequest request = CreateRequest("POST", "/api/chat", body, true))
            {
                request.SetRequestHeader("Accept", "text/event-stream");
                request.timeout = ChatTimeoutSeconds;
                request.downloadHandler = new StreamingDownloadHandler(request, router, _log);
                _chatRequest = request;
                yield return request.SendWebRequest();
                _chatRequest = null;
                try
                {
                    router.Complete(request.responseCode, TransportError(request), aborted: false);
                }
                catch (Exception ex)
                {
                    _log?.Invoke("EDEN chat handler threw: " + ex);
                }
            }
        }

        private IEnumerator EventsLoop(EdenStreamHandlers handlers, int generation)
        {
            float backoff = 1f;
            bool unauthorized = false;
            var relay = new EdenStreamHandlers
            {
                OnToken = handlers.OnToken,
                OnSystem = handlers.OnSystem,
                OnStateChange = handlers.OnStateChange,
                OnPendingChoice = handlers.OnPendingChoice,
                OnChoiceResolved = handlers.OnChoiceResolved,
                OnOptions = handlers.OnOptions,
                OnDone = handlers.OnDone,
                OnAnyEvent = handlers.OnAnyEvent,
                OnError = error =>
                {
                    _log?.Invoke("EDEN events: " + error);
                    if (error.HttpStatus != 401)
                        return;
                    unauthorized = true;
                    handlers.OnError?.Invoke(error);
                }
            };
            while (_eventsWanted && generation == _eventsGeneration)
            {
                var router = new EdenResponseRouter(relay, expectDone: false);
                using (UnityWebRequest request = CreateRequest("GET", "/api/events", null, true))
                {
                    request.SetRequestHeader("Accept", "text/event-stream");
                    request.timeout = 0;
                    request.downloadHandler = new StreamingDownloadHandler(request, router, _log);
                    _eventsRequest = request;
                    yield return request.SendWebRequest();
                    _eventsRequest = null;
                    bool aborted = !_eventsWanted || generation != _eventsGeneration;
                    try
                    {
                        router.Complete(request.responseCode, TransportError(request), aborted);
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke("EDEN events handler threw: " + ex);
                    }
                    if (router.EventCount > 0)
                        backoff = 1f;
                }
                if (unauthorized)
                {
                    _eventsWanted = false;
                    yield break;
                }
                if (!_eventsWanted || generation != _eventsGeneration)
                    yield break;
                yield return new WaitForSecondsRealtime(backoff);
                backoff = Mathf.Min(backoff * 2f, 15f);
            }
        }

        // ---- plumbing ----------------------------------------------------------------

        private IEnumerator SendJson(string method, string pathAndQuery, string? body, bool withSession, int timeout,
            Action<long, string> onOk, Action<EdenApiError>? onError)
        {
            using (UnityWebRequest request = CreateRequest(method, pathAndQuery, body, withSession))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = timeout;
                yield return request.SendWebRequest();

                long status = request.responseCode;
                string text = request.downloadHandler.text ?? string.Empty;
                EdenApiError? error = null;
                if (request.result == UnityWebRequest.Result.ConnectionError || status == 0)
                    error = new EdenApiError(0, "network", request.error ?? "No response.", null);
                else if (status >= 400)
                    error = EdenApiError.FromResponse(status, text);

                try
                {
                    if (error != null)
                    {
                        _log?.Invoke("EDEN " + method + " " + StripQuery(pathAndQuery) + " failed: " + error);
                        onError?.Invoke(error);
                    }
                    else
                    {
                        onOk(status, text);
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke("EDEN " + method + " " + StripQuery(pathAndQuery) + " handler threw: " + ex);
                }
            }
        }

        private UnityWebRequest CreateRequest(string method, string pathAndQuery, string? body, bool withSession)
        {
            string url = BaseUrl.TrimEnd('/') + pathAndQuery;
            if (withSession && HasSession)
                url += (pathAndQuery.IndexOf('?') >= 0 ? "&" : "?") + "sid=" + Uri.EscapeDataString(SessionId);

            var request = new UnityWebRequest(url, method);
            if (body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.useHttpContinue = false;
            if (BearerToken.Length > 0)
                request.SetRequestHeader("Authorization", "Bearer " + BearerToken);
            return request;
        }

        private static string? TransportError(UnityWebRequest request) =>
            request.result == UnityWebRequest.Result.ConnectionError ? (request.error ?? "network error") : null;

        private static string StripQuery(string pathAndQuery)
        {
            int q = pathAndQuery.IndexOf('?');
            return q < 0 ? pathAndQuery : pathAndQuery.Substring(0, q);
        }

        private static List<object?>? FirstList(Dictionary<string, object?> obj)
        {
            foreach (KeyValuePair<string, object?> kv in obj)
            {
                if (kv.Value is List<object?> list)
                    return list;
            }
            return null;
        }

        /// <summary>Hands each received chunk to the router as it arrives (called by Unity on the main thread).</summary>
        private sealed class StreamingDownloadHandler : DownloadHandlerScript
        {
            private readonly UnityWebRequest _request;
            private readonly EdenResponseRouter _router;
            private readonly Action<string>? _log;

            public StreamingDownloadHandler(UnityWebRequest request, EdenResponseRouter router, Action<string>? log)
                : base(new byte[8192])
            {
                _request = request;
                _router = router;
                _log = log;
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                try
                {
                    _router.Begin(_request.responseCode, _request.GetResponseHeader("Content-Type"));
                    _router.Receive(data, 0, dataLength);
                }
                catch (Exception ex)
                {
                    _log?.Invoke("EDEN stream handler threw: " + ex);
                }
                return true;
            }
        }
    }
}
