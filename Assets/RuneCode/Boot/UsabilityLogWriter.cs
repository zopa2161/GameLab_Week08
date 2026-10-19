using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

using UnityEngine;

namespace RuneCode
{
    /// <summary>Owns one local schema-v4 session; callers supply measurements and call Flush periodically on the owning thread.</summary>
    public sealed class UsabilityLogWriter : IDisposable
    {
        private const int SCHEMA_VERSION = 4;
        private const long FLUSH_INTERVAL_MS = 1000;
        private static readonly Encoding _encoding = new UTF8Encoding(false, true);
        private static readonly Dictionary<string, string> _combatFieldAliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["duration_ms"] = "seconds",
            ["damage_received"] = "damage_taken",
            ["dash_inputs"] = "space_presses",
            ["dashes_started"] = "dash_count",
        };
        private static readonly Dictionary<string, string> _combatEventFieldAliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["duration_ms"] = "seconds",
            ["damage_received"] = "damage_taken",
            ["dash_inputs"] = "space_presses",
            ["dashes_started"] = "dash_count",
            ["kill_count"] = "kills",
        };
        private static readonly Dictionary<string, string> _compileEventFieldAliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["is_valid"] = "ok",
            ["ram_used"] = "ram",
            ["node_count"] = "nodes",
            ["edge_count"] = "edges",
        };
        private static readonly Dictionary<string, string> _buildSnapshotFieldAliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["is_compile_valid"] = "compile_ok",
        };
        private static readonly Dictionary<string, string> _summaryFieldAliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["duration_ms"] = "elapsed_seconds",
            ["editor_click_count"] = "editor_clicks",
            ["successful_actions_count"] = "successful_actions",
            ["rejected_actions_count"] = "failed_actions",
        };
        private static readonly string[] _identityFields = { "schema_version", "participant_id", "test_session_id", "app_version", "condition", "editor_model", "test_order", "is_test_mode", "is_debug", "is_practice" };

        private readonly Dictionary<string, object> _session;
        private readonly string[] _combatColumns;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private StreamWriter _events;
        private StreamWriter _combats;
        private long _eventSequence;
        private long _lastFlushMs;
        private bool _isClosed;

        public string SessionId { get; }
        public string DirectoryPath { get; }
        public long EventCount => _eventSequence;
        public long ElapsedMilliseconds => _clock.ElapsedMilliseconds;

        /// <summary>Creates a unique session with metadata and ordered combat columns, separating editor and player log folders.</summary>
        public static UsabilityLogWriter StartSession(IDictionary<string, object> metadata, IEnumerable<string> combatColumns)
        {
            return new UsabilityLogWriter(metadata, combatColumns);
        }

        /// <summary>Copies session metadata and creates new files without reusing or truncating any previous session.</summary>
        private UsabilityLogWriter(IDictionary<string, object> metadata, IEnumerable<string> combatColumns)
        {
            _session = new Dictionary<string, object>(metadata);
            foreach (string key in new[] { "participant_id", "app_version", "condition", "editor_model", "test_order", "is_test_mode", "is_debug", "is_practice" })
                if (!_session.ContainsKey(key)) throw new ArgumentException("Missing session field: " + key, nameof(metadata));
            var columns = new List<string>(_identityFields);
            foreach (string column in combatColumns)
            {
                if (string.IsNullOrWhiteSpace(column)) throw new ArgumentException("Combat CSV needs named columns.", nameof(combatColumns));
                string normalizedColumn = RenameField(column, _combatFieldAliases);
                if (!columns.Contains(normalizedColumn)) columns.Add(normalizedColumn);
            }
            _combatColumns = columns.ToArray();
            SessionId = Guid.NewGuid().ToString("N");
            DirectoryPath = Path.Combine(Application.persistentDataPath,
                Application.isEditor ? "UsabilityTests_Editor" : "UsabilityTests", SessionId);
            _session["runtime_environment"] = Application.isEditor ? "editor" : "player";
            _session["schema_version"] = SCHEMA_VERSION;
            _session["test_session_id"] = SessionId;
            _session["started_at"] = Timestamp();
            _session["timezone"] = "UTC";
            _session["status"] = "in_progress";
            string sessionJson = Json(_session);
            Directory.CreateDirectory(Path.Combine(DirectoryPath, "builds"));
            try
            {
                _events = OpenNew(Path.Combine(DirectoryPath, "events.jsonl"));
                _combats = OpenNew(Path.Combine(DirectoryPath, "combats.csv"));
                _combats.WriteLine(CsvLine(_combatColumns));
                WriteSession(sessionJson);
                if (!Application.isEditor)
                    File.WriteAllText(Path.Combine(DirectoryPath, "player-session.marker"), Application.buildGUID);
                Flush();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>Appends one ordered event with monotonic elapsed time, UTC time and normalized equivalent field names; returns its session sequence.</summary>
        public long AppendEvent(string eventName, string context, IDictionary<string, object> data)
        {
            EnsureOpen();
            Dictionary<string, object> normalizedData = NormalizeEventData(eventName, data);
            var envelope = WithIdentity(new Dictionary<string, object>());
            envelope["event_name"] = eventName;
            envelope["event_seq"] = _eventSequence + 1;
            envelope["timestamp_utc"] = Timestamp();
            envelope["timezone"] = "UTC";
            envelope["elapsed_ms"] = _clock.ElapsedMilliseconds;
            envelope["editing_episode_id"] = FindValue(normalizedData, "editing_episode_id");
            envelope["graph_revision_id"] = FindValue(normalizedData, "graph_revision_id");
            envelope["spell_build_id"] = FindValue(normalizedData, "spell_build_id");
            envelope["combat_id"] = FindValue(normalizedData, "combat_id");
            envelope["context"] = context;
            envelope["data"] = normalizedData;
            string line = Json(envelope);
            _events.WriteLine(line);
            _eventSequence++;
            Flush(false);
            return _eventSequence;
        }

        /// <summary>Writes a never-overwritten graph snapshot with caller-supplied comparison, compile and progression metadata, then flushes events.</summary>
        public string WriteGraphSnapshot(string spellBuildId, SpellGraph graph, IDictionary<string, object> metadata)
        {
            EnsureOpen();
            if (string.IsNullOrWhiteSpace(spellBuildId) || spellBuildId == "." || spellBuildId == ".." ||
                spellBuildId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || spellBuildId.Contains("/") || spellBuildId.Contains("\\"))
                throw new ArgumentException("Build ID must be a single valid filename component.", nameof(spellBuildId));
            var snapshot = WithIdentity(RenameFields(metadata, _buildSnapshotFieldAliases));
            snapshot["spell_build_id"] = spellBuildId;
            snapshot["graph"] = SpellGraphData.From(graph);
            string json = Json(snapshot);
            string path = Path.Combine(DirectoryPath, "builds", spellBuildId + ".json");
            using (StreamWriter writer = OpenNew(path))
            {
                writer.Write(json);
                FlushWriter(writer);
            }
            Flush();
            return path;
        }

        /// <summary>Appends a combat measurement row using the session's fixed column order; missing values remain empty and unknown columns are rejected.</summary>
        public void AppendCombatSummary(IDictionary<string, object> row)
        {
            EnsureOpen();
            Dictionary<string, object> normalizedRow = NormalizeCombatSummary(row);
            foreach (string key in normalizedRow.Keys)
                if (Array.IndexOf(_combatColumns, key) < 0) throw new ArgumentException("Unknown combat column: " + key, nameof(row));
            var record = WithIdentity(normalizedRow);
            var values = new List<object>();
            foreach (string column in _combatColumns) values.Add(record.TryGetValue(column, out object value) ? value : null);
            _combats.WriteLine(CsvLine(values));
            Flush();
        }

        /// <summary>Flushes both streams through to disk; pass false from a periodic lifecycle hook to limit forced flushes to once per second.</summary>
        public void Flush(bool force = true)
        {
            EnsureOpen();
            if (!force && _clock.ElapsedMilliseconds - _lastFlushMs < FLUSH_INTERVAL_MS) return;
            FlushWriter(_events);
            FlushWriter(_combats);
            _lastFlushMs = _clock.ElapsedMilliseconds;
        }

        /// <summary>Writes one supplied summary row and final session state, then closes streams; does not invent test_end events or compute metrics.</summary>
        public void Close(IDictionary<string, object> summary, IDictionary<string, object> endState)
        {
            EnsureOpen();
            var finalSummary = WithIdentity(NormalizeSummary(summary));
            var columns = new List<string>(finalSummary.Keys);
            var values = new List<object>();
            foreach (string column in columns) values.Add(finalSummary[column]);
            string csv = CsvLine(columns) + "\n" + CsvLine(values) + "\n";
            _session["end_state"] = endState;
            _session["ended_at"] = Timestamp();
            _session["elapsed_ms"] = _clock.ElapsedMilliseconds;
            _session["status"] = "ended";
            string sessionJson = Json(_session);
            try
            {
                Flush();
                using (StreamWriter writer = OpenNew(Path.Combine(DirectoryPath, "summary.csv")))
                {
                    writer.Write(csv);
                    FlushWriter(writer);
                }
                WriteSession(sessionJson);
            }
            finally { Dispose(); }
        }

        /// <summary>Flushes and closes open streams once; disposal without Close leaves session.json marked in_progress for incomplete-session analysis.</summary>
        public void Dispose()
        {
            if (_isClosed) return;
            _isClosed = true;
            try
            {
                if (_events != null) try { FlushWriter(_events); } finally { _events.Dispose(); }
            }
            finally
            {
                if (_combats != null) try { FlushWriter(_combats); } finally { _combats.Dispose(); }
            }
        }

        /// <summary>Rejects operations after stream disposal.</summary>
        private void EnsureOpen()
        {
            if (_isClosed) throw new ObjectDisposedException(nameof(UsabilityLogWriter));
        }

        /// <summary>Copies supplied fields and stamps the immutable session identity on every artifact record.</summary>
        private Dictionary<string, object> WithIdentity(IDictionary<string, object> fields)
        {
            var record = new Dictionary<string, object>(fields);
            foreach (string key in _identityFields) record[key] = _session[key];
            return record;
        }

        /// <summary>Returns an optional linkage identifier from event data for its common-envelope field.</summary>
        private static object FindValue(IDictionary<string, object> values, string key)
        {
            return values != null && values.TryGetValue(key, out object value) ? value : null;
        }

        /// <summary>Creates a UTF-8 file exclusively with LF line endings and read sharing for live inspection.</summary>
        private static StreamWriter OpenNew(string path)
        {
            return new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), _encoding) { NewLine = "\n" };
        }

        /// <summary>Drains character buffers and asks the operating system to persist file data.</summary>
        private static void FlushWriter(StreamWriter writer)
        {
            writer.Flush();
            ((FileStream)writer.BaseStream).Flush(true);
        }

        /// <summary>Persists session JSON to a temporary file before atomically replacing the previous session state.</summary>
        private void WriteSession(string json)
        {
            string path = Path.Combine(DirectoryPath, "session.json");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (StreamWriter writer = OpenNew(temporary))
            {
                writer.Write(json);
                FlushWriter(writer);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        /// <summary>Returns an ISO 8601 UTC timestamp independent of the device timezone.</summary>
        private static string Timestamp()
        {
            return DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        }

        /// <summary>Renames event fields whose schema-v4 meaning matches the legacy field and converts their units where needed.</summary>
        private static Dictionary<string, object> NormalizeEventData(string eventName, IDictionary<string, object> data)
        {
            if (eventName == "compile_result") return RenameFields(data, _compileEventFieldAliases);
            if (eventName != "combat_end") return data == null
                ? new Dictionary<string, object>(StringComparer.Ordinal)
                : new Dictionary<string, object>(data, StringComparer.Ordinal);

            Dictionary<string, object> normalized = RenameFields(data, _combatEventFieldAliases);
            if (data != null && data.TryGetValue("duration_ms", out object durationMilliseconds))
                normalized["seconds"] = MillisecondsToSeconds(durationMilliseconds);
            return normalized;
        }

        /// <summary>Renames equivalent combat summary fields and converts the duration from milliseconds to seconds.</summary>
        private static Dictionary<string, object> NormalizeCombatSummary(IDictionary<string, object> row)
        {
            Dictionary<string, object> normalized = RenameFields(row, _combatFieldAliases);
            if (row.TryGetValue("duration_ms", out object durationMilliseconds))
                normalized["seconds"] = MillisecondsToSeconds(durationMilliseconds);
            return normalized;
        }

        /// <summary>Renames equivalent summary fields and converts the session duration from milliseconds to seconds.</summary>
        private static Dictionary<string, object> NormalizeSummary(IDictionary<string, object> summary)
        {
            Dictionary<string, object> normalized = RenameFields(summary, _summaryFieldAliases);
            if (summary.TryGetValue("duration_ms", out object durationMilliseconds))
                normalized["elapsed_seconds"] = MillisecondsToSeconds(durationMilliseconds);
            return normalized;
        }

        /// <summary>Copies fields under their target names and rejects a collision rather than silently overwriting data.</summary>
        private static Dictionary<string, object> RenameFields(IDictionary<string, object> fields, IDictionary<string, string> aliases)
        {
            var normalized = fields == null
                ? new Dictionary<string, object>(StringComparer.Ordinal)
                : new Dictionary<string, object>(fields, StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> alias in aliases)
            {
                if (!normalized.TryGetValue(alias.Key, out object value)) continue;
                if (normalized.ContainsKey(alias.Value))
                    throw new InvalidOperationException("Log field alias would overwrite data: " + alias.Key + " -> " + alias.Value);
                normalized.Remove(alias.Key);
                normalized.Add(alias.Value, value);
            }
            return normalized;
        }

        /// <summary>Returns a combat CSV header under the matching legacy alias when one is defined.</summary>
        private static string RenameField(string field, IDictionary<string, string> aliases)
        {
            return aliases.TryGetValue(field, out string alias) ? alias : field;
        }

        /// <summary>Converts a millisecond duration to seconds while preserving the measurement.</summary>
        private static double MillisecondsToSeconds(object milliseconds)
        {
            return Convert.ToDouble(milliseconds, CultureInfo.InvariantCulture) / 1000d;
        }

        /// <summary>Serializes string-key dictionaries, arrays, primitives and existing graph DTOs; unavailable non-finite numbers become null.</summary>
        private static string Json(object value)
        {
            if (value == null) return "null";
            if (value is string text) return Quote(text);
            if (value is bool flag) return flag ? "true" : "false";
            if (value is SpellGraphData graph) return JsonUtility.ToJson(graph);
            if (value is IDictionary<string, object> fields)
            {
                var pairs = new List<string>();
                foreach (KeyValuePair<string, object> field in fields) pairs.Add(Quote(field.Key) + ":" + Json(field.Value));
                return "{" + string.Join(",", pairs) + "}";
            }
            if (value is IDictionary numericFields)
            {
                var pairs = new List<string>();
                foreach (DictionaryEntry field in numericFields)
                {
                    if (!(field.Key is string key)) throw new ArgumentException("JSON object keys must be strings.");
                    pairs.Add(Quote(key) + ":" + Json(field.Value));
                }
                return "{" + string.Join(",", pairs) + "}";
            }
            if (value is IEnumerable sequence)
            {
                var items = new List<string>();
                foreach (object item in sequence) items.Add(Json(item));
                return "[" + string.Join(",", items) + "]";
            }
            if (value is double number && (double.IsNaN(number) || double.IsInfinity(number))) return "null";
            if (value is float single && (float.IsNaN(single) || float.IsInfinity(single))) return "null";
            if (value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint ||
                value is long || value is ulong || value is float || value is double || value is decimal)
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            throw new ArgumentException("Unsupported JSON value type: " + value.GetType().FullName);
        }

        /// <summary>Escapes JSON quotes, backslashes, control characters and surrogate code units without introducing physical line breaks.</summary>
        private static string Quote(string text)
        {
            var result = new StringBuilder("\"");
            foreach (char character in text)
            {
                if (character == '"' || character == '\\') result.Append('\\').Append(character);
                else if (character < ' ' || char.IsSurrogate(character)) result.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                else result.Append(character);
            }
            return result.Append('"').ToString();
        }

        /// <summary>Builds a CSV record with quoted, doubled-quote cells; null and non-finite values remain empty, while compound values use JSON.</summary>
        private static string CsvLine(IEnumerable values)
        {
            var cells = new List<string>();
            foreach (object value in values)
            {
                string text = value == null ? "" : value is string literal ? literal : Json(value);
                if (!(value is string) && text == "null") text = "";
                cells.Add("\"" + text.Replace("\"", "\"\"") + "\"");
            }
            return string.Join(",", cells);
        }
    }
}
