#nullable enable
using System;
using System.Text;

namespace ForbiddenEden.Net
{
    /// <summary>One dispatched Server-Sent Event: the <c>event:</c> name (empty when absent) and its joined <c>data:</c> lines.</summary>
    public readonly struct EdenSseMessage
    {
        public EdenSseMessage(string eventName, string data)
        {
            EventName = eventName;
            Data = data;
        }

        public string EventName { get; }
        public string Data { get; }
    }

    /// <summary>
    /// Incremental SSE parser (WHATWG event-stream rules) fed with raw network bytes.
    /// A stateful UTF-8 decoder keeps multi-byte characters that straddle two network
    /// chunks intact, which matters for a Chinese token stream. Accepts LF, CRLF and CR
    /// line endings, joins multi-line <c>data:</c>, and ignores <c>:</c> comment lines
    /// such as the server's 30-second <c>: keepalive</c>.
    /// </summary>
    public sealed class EdenSseParser
    {
        private readonly Action<EdenSseMessage> _onMessage;
        private readonly Decoder _decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetDecoder();
        private readonly StringBuilder _line = new StringBuilder();
        private readonly StringBuilder _data = new StringBuilder();
        private char[] _chars = new char[4096];
        private string _eventName = string.Empty;
        private bool _lastWasCarriageReturn;
        private bool _atStreamStart = true;

        public EdenSseParser(Action<EdenSseMessage> onMessage)
        {
            _onMessage = onMessage ?? throw new ArgumentNullException(nameof(onMessage));
        }

        /// <summary>Number of comment lines seen (keepalives); useful for liveness diagnostics.</summary>
        public int CommentCount { get; private set; }

        public void Feed(byte[] bytes, int offset, int count)
        {
            if (count <= 0)
                return;
            int needed = _decoder.GetCharCount(bytes, offset, count, flush: false);
            if (needed > _chars.Length)
                _chars = new char[Math.Max(needed, _chars.Length * 2)];
            int produced = _decoder.GetChars(bytes, offset, count, _chars, 0, flush: false);
            FeedChars(_chars, produced);
        }

        /// <summary>
        /// Call once when the stream ends. Per the SSE spec an event with no terminating
        /// blank line is discarded, so only a trailing unterminated line is processed.
        /// </summary>
        public void Finish()
        {
            int produced = _decoder.GetChars(Array.Empty<byte>(), 0, 0, _chars, 0, flush: true);
            FeedChars(_chars, produced);
            if (_line.Length > 0)
                ProcessLine();
            _data.Length = 0;
            _eventName = string.Empty;
        }

        private void FeedChars(char[] chars, int count)
        {
            for (int i = 0; i < count; i++)
            {
                char c = chars[i];
                if (_atStreamStart)
                {
                    _atStreamStart = false;
                    if (c == '﻿')
                        continue;
                }
                if (c == '\n')
                {
                    if (_lastWasCarriageReturn)
                    {
                        _lastWasCarriageReturn = false;
                        continue; // second half of CRLF
                    }
                    ProcessLine();
                }
                else if (c == '\r')
                {
                    _lastWasCarriageReturn = true;
                    ProcessLine();
                }
                else
                {
                    _lastWasCarriageReturn = false;
                    _line.Append(c);
                }
            }
        }

        private void ProcessLine()
        {
            if (_line.Length == 0)
            {
                Dispatch();
                return;
            }
            string line = _line.ToString();
            _line.Length = 0;

            if (line[0] == ':')
            {
                CommentCount++;
                return;
            }

            string field;
            string value;
            int colon = line.IndexOf(':');
            if (colon < 0)
            {
                field = line;
                value = string.Empty;
            }
            else
            {
                field = line.Substring(0, colon);
                int start = colon + 1;
                if (start < line.Length && line[start] == ' ')
                    start++;
                value = line.Substring(start);
            }

            switch (field)
            {
                case "data":
                    _data.Append(value).Append('\n');
                    break;
                case "event":
                    _eventName = value;
                    break;
                // "id" and "retry" are not used by ai-agent-server.
            }
        }

        private void Dispatch()
        {
            if (_data.Length == 0)
            {
                _eventName = string.Empty;
                return;
            }
            _data.Length -= 1; // trailing '\n'
            var message = new EdenSseMessage(_eventName, _data.ToString());
            _data.Length = 0;
            _eventName = string.Empty;
            _onMessage(message);
        }
    }
}
