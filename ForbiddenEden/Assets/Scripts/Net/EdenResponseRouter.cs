#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ForbiddenEden.Net
{
    /// <summary>Callbacks for a streaming response. All are optional and are invoked on the caller's thread (Unity main thread).</summary>
    public sealed class EdenStreamHandlers
    {
        public Action<string>? OnToken;
        public Action<string>? OnSystem;
        /// <summary>Human-readable diff line such as "💗 [name 好感 +3]"; numbers come from <see cref="OnDone"/>.</summary>
        public Action<string>? OnStateChange;
        public Action<EdenPendingChoice>? OnPendingChoice;
        /// <summary>Fired for the SSE event and for the plain-JSON reply /api/chat gives when the input resolves a pending choice.</summary>
        public Action<EdenChoiceResolved>? OnChoiceResolved;
        public Action<List<string>>? OnOptions;
        public Action<EdenTurnDone>? OnDone;
        public Action<EdenApiError>? OnError;
        /// <summary>Every parsed event, including unknown types, before the typed callback.</summary>
        public Action<EdenEvent>? OnAnyEvent;
    }

    /// <summary>
    /// Decides per response whether the body is an SSE stream or a plain JSON reply and
    /// routes it. <c>POST /api/chat</c> answers 200 text/event-stream for a normal turn,
    /// but plain JSON for 4xx/5xx errors and for 200 <c>choice_resolved</c>, so the
    /// decision has to wait for the status line and Content-Type.
    /// </summary>
    public sealed class EdenResponseRouter
    {
        private readonly EdenStreamHandlers _handlers;
        private readonly bool _expectDone;
        private readonly MemoryStream _body = new MemoryStream();
        private EdenSseParser? _parser;
        private long _status;
        private bool _begun;

        public EdenResponseRouter(EdenStreamHandlers handlers, bool expectDone)
        {
            _handlers = handlers;
            _expectDone = expectDone;
        }

        public bool IsStreaming => _parser != null;
        public bool SawDone { get; private set; }
        public bool Finished { get; private set; }
        public int EventCount { get; private set; }
        public int KeepaliveCount => _parser?.CommentCount ?? 0;

        public void Begin(long status, string? contentType)
        {
            if (_begun)
                return;
            _begun = true;
            _status = status;
            bool isEventStream = contentType != null &&
                                 contentType.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0;
            if (status >= 200 && status < 300 && isEventStream)
                _parser = new EdenSseParser(OnMessage);
        }

        public void Receive(byte[] data, int offset, int count)
        {
            if (Finished || count <= 0)
                return;
            if (_parser != null)
                _parser.Feed(data, offset, count);
            else
                _body.Write(data, offset, count);
        }

        /// <summary>Call exactly once after the transport finishes. <paramref name="transportError"/> is null on a clean close.</summary>
        public void Complete(long status, string? transportError, bool aborted)
        {
            if (Finished)
                return;
            if (!_begun)
                Begin(status, null);
            if (_status == 0)
                _status = status;

            if (_parser != null)
            {
                _parser.Finish();
                Finished = true;
                if (aborted)
                    return;
                if (_expectDone && !SawDone)
                {
                    string code = transportError != null ? "network" : "stream_incomplete";
                    _handlers.OnError?.Invoke(new EdenApiError(_status, code, transportError ?? "Stream closed before the done event.", null));
                }
                return;
            }

            Finished = true;
            if (aborted)
                return;
            string body = Encoding.UTF8.GetString(_body.ToArray());
            if (_status == 0)
            {
                _handlers.OnError?.Invoke(new EdenApiError(0, "network", transportError ?? "No response.", null));
                return;
            }
            if (_status >= 400)
            {
                _handlers.OnError?.Invoke(EdenApiError.FromResponse(_status, body));
                return;
            }

            Dictionary<string, object?>? json = EdenJson.TryParseObject(body);
            if (json != null && json.GetString("type") == "choice_resolved")
            {
                _handlers.OnChoiceResolved?.Invoke(new EdenChoiceResolved(json));
                return;
            }
            if (_expectDone || transportError != null)
            {
                string code = json == null ? "bad_json" : "unexpected_reply";
                _handlers.OnError?.Invoke(new EdenApiError(_status, code, transportError ?? body, json));
            }
        }

        private void OnMessage(EdenSseMessage message)
        {
            EdenEvent evt = EdenEvent.FromSse(message);
            EventCount++;
            _handlers.OnAnyEvent?.Invoke(evt);
            switch (evt.Kind)
            {
                case EdenEventKind.Token:
                    _handlers.OnToken?.Invoke(evt.Content);
                    break;
                case EdenEventKind.System:
                    _handlers.OnSystem?.Invoke(evt.Content);
                    break;
                case EdenEventKind.StateChange:
                    _handlers.OnStateChange?.Invoke(evt.Content);
                    break;
                case EdenEventKind.PendingChoice:
                {
                    EdenPendingChoice? pending = evt.PendingChoice;
                    if (pending != null)
                        _handlers.OnPendingChoice?.Invoke(pending);
                    break;
                }
                case EdenEventKind.ChoiceResolved:
                {
                    EdenChoiceResolved? resolved = evt.ChoiceResolved;
                    if (resolved != null)
                        _handlers.OnChoiceResolved?.Invoke(resolved);
                    break;
                }
                case EdenEventKind.Options:
                    _handlers.OnOptions?.Invoke(evt.Options);
                    break;
                case EdenEventKind.Done:
                    SawDone = true;
                    _handlers.OnDone?.Invoke(new EdenTurnDone(evt.Options, evt.State));
                    break;
            }
        }
    }
}
