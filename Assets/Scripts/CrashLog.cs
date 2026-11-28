using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace XCity
{
    internal static class CrashLog
    {
        private const int MaxBytes = 256 * 1024;
        private const int RetainedBytes = MaxBytes / 2;
        private const string EntryPrefix = "[XCity error] ";
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static readonly byte[] EntryMarker = Utf8.GetBytes("\n" + EntryPrefix);
        private static readonly object Gate = new object();
        private static string _path, _pendingPath, _appVersion, _deviceModel;
        private static MapSpec? _map;
        private static bool _startupNotice, _markedThisSession;
        private static int _count;

        public static int Count
        {
            get { lock (Gate) return _count; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            // Unity properties are read here on the main thread, never by the log callback.
            Application.logMessageReceivedThreaded -= Record;
            lock (Gate)
            {
                _path = null;
                _map = null;
                _count = 0;
                _startupNotice = _markedThisSession = false;
                try
                {
                    string directory = Application.persistentDataPath;
                    Directory.CreateDirectory(directory);
                    _appVersion = Application.version;
                    _deviceModel = SystemInfo.deviceModel;
                    _pendingPath = Path.Combine(directory, "xcity-log.pending");
                    _path = Path.Combine(directory, "xcity-log.txt");
                    if (File.Exists(_path)) _count = CountEntries(File.ReadAllText(_path, Utf8));
                    _startupNotice = _count > 0 && File.Exists(_pendingPath);
                    File.Delete(_pendingPath);
                }
                catch (Exception)
                {
                    // Logging must not prevent the game from opening.
                }
            }
            Application.logMessageReceivedThreaded += Record;
        }

        public static void SetMap(MapSpec map)
        {
            lock (Gate) _map = map;
        }

        public static bool TakeStartupNotice()
        {
            lock (Gate)
            {
                bool pending = _startupNotice;
                _startupNotice = false;
                return pending;
            }
        }

        private static void Record(string message, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            lock (Gate)
            {
                if (_path == null) return;
                try
                {
                    string map = _map.HasValue ? $" | map={_map.Value.Style} | seed={_map.Value.Seed}" : "";
                    string header = $"{EntryPrefix}{DateTimeOffset.UtcNow:O} | {type} | app={_appVersion} | device={_deviceModel}{map}\n";
                    // Reserve space for both the message and the stack of even a very large error.
                    byte[] entry = Utf8.GetBytes(header + Indent(message) + Indent(stack));
                    if (entry.Length > RetainedBytes)
                    {
                        const int payloadLimit = RetainedBytes / 2 - 1024;
                        entry = Utf8.GetBytes(header + Indent(Shorten(message, payloadLimit)) + Indent(Shorten(stack, payloadLimit)));
                        // Multiline payloads can grow when indented.
                        Array.Resize(ref entry, Utf8Boundary(entry, Math.Min(entry.Length, RetainedBytes - 1)));
                        Array.Resize(ref entry, entry.Length + 1);
                        entry[entry.Length - 1] = (byte)'\n';
                    }

                    var file = new FileInfo(_path);
                    if (file.Exists && file.Length + entry.Length > MaxBytes)
                    {
                        Compact(entry);
                    }
                    else
                    {
                        using (var stream = new FileStream(_path, FileMode.Append, FileAccess.Write))
                            stream.Write(entry, 0, entry.Length);
                        _count++;
                    }

                    if (!_markedThisSession)
                    {
                        File.WriteAllText(_pendingPath, "1", Utf8);
                        _markedThisSession = true;
                    }
                }
                catch (Exception)
                {
                    // Never log from inside this handler or propagate failures to Unity.
                }
            }
        }

        private static string Indent(string value) => string.IsNullOrEmpty(value) ? "" :
            "  " + value.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\n  ") + "\n";

        private static string Shorten(string value, int limit)
        {
            if (string.IsNullOrEmpty(value)) return "";
            byte[] bytes = Utf8.GetBytes(value);
            return bytes.Length <= limit ? value :
                Utf8.GetString(bytes, 0, Utf8Boundary(bytes, limit)) + "…";
        }

        private static int Utf8Boundary(byte[] bytes, int end)
        {
            while (end < bytes.Length && end > 0 && (bytes[end] & 0xc0) == 0x80) end--;
            return end;
        }

        private static void Compact(byte[] entry)
        {
            string temporary = _path + ".tmp";
            using (var source = File.OpenRead(_path))
            {
                int remaining = (int)Math.Min(source.Length, RetainedBytes - entry.Length);
                var tail = new byte[remaining];
                source.Seek(-remaining, SeekOrigin.End);
                for (int read = 0; read < remaining;)
                {
                    int length = source.Read(tail, read, remaining - read);
                    if (length == 0) throw new EndOfStreamException();
                    read += length;
                }

                // Start at a record boundary so old partial entries cannot break the count or UTF-8.
                int start = FirstHeader(tail);
                using (var target = File.Create(temporary))
                {
                    target.Write(tail, start, tail.Length - start);
                    target.Write(entry, 0, entry.Length);
                }
            }
            File.Replace(temporary, _path, null);
            _count = CountEntries(File.ReadAllText(_path, Utf8));
        }

        private static int FirstHeader(byte[] tail)
        {
            for (int i = 0; i <= tail.Length - EntryMarker.Length; i++)
            {
                int j = 0;
                while (j < EntryMarker.Length && tail[i + j] == EntryMarker[j]) j++;
                if (j == EntryMarker.Length) return i + 1;
            }
            return tail.Length;
        }

        private static int CountEntries(string text)
        {
            int count = 0;
            using var reader = new StringReader(text);
            for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
                if (line.StartsWith(EntryPrefix, StringComparison.Ordinal)) count++;
            return count;
        }

        public static bool TryRead(out string text, int lineLimit = 0)
        {
            lock (Gate)
            {
                text = "";
                try
                {
                    if (_path == null) return false;
                    if (!File.Exists(_path)) return true;
                    text = File.ReadAllText(_path, Utf8);
                    if (lineLimit > 0)
                    {
                        int end = text.Length;
                        if (end > 0 && text[end - 1] == '\n') end--;
                        for (int i = end - 1; i >= 0; i--)
                            if (text[i] == '\n' && --lineLimit == 0)
                            {
                                text = text.Substring(i + 1);
                                break;
                            }
                    }
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public static bool TryClear()
        {
            lock (Gate)
            {
                try
                {
                    if (_path == null) return false;
                    File.Delete(_path);
                    _count = 0;
                    _startupNotice = _markedThisSession = false;
                    File.Delete(_pendingPath);
                    File.Delete(_path + ".tmp");
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }
    }
}
