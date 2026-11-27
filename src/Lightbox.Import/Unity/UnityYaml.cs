using System.Globalization;
using System.Text;

namespace Lightbox.Import.Unity;

/// <summary>
/// One value in a Unity YAML document: a scalar, a map or a sequence.
/// </summary>
/// <remarks>
/// Deliberately not a YAML library. Unity writes a narrow, regular subset —
/// block maps, block sequences whose dashes sit at their key's indent, one-line
/// flow maps and lists, and the odd wrapped scalar — and this reads exactly that.
/// Anchors, aliases and tags inside a document are kept as text and never
/// followed, so there is nothing to expand and nothing to recurse through (S1).
/// </remarks>
public sealed class UnityYamlNode
{
    public enum NodeKind { Scalar, Map, Sequence }

    public NodeKind Kind { get; }
    public string Text { get; } = "";
    public IReadOnlyList<KeyValuePair<string, UnityYamlNode>> Entries { get; } = [];
    public IReadOnlyList<UnityYamlNode> Items { get; } = [];

    private UnityYamlNode(NodeKind kind, string text,
        IReadOnlyList<KeyValuePair<string, UnityYamlNode>>? entries, IReadOnlyList<UnityYamlNode>? items)
    {
        Kind = kind;
        Text = text;
        Entries = entries ?? [];
        Items = items ?? [];
    }

    internal static UnityYamlNode Scalar(string text) => new(NodeKind.Scalar, text, null, null);
    internal static UnityYamlNode Map(List<KeyValuePair<string, UnityYamlNode>> entries) => new(NodeKind.Map, "", entries, null);
    internal static UnityYamlNode Sequence(List<UnityYamlNode> items) => new(NodeKind.Sequence, "", null, items);

    internal static readonly UnityYamlNode Empty = Scalar("");

    /// <summary>The value under <paramref name="key"/>, or null. The first entry wins.</summary>
    public UnityYamlNode? this[string key]
    {
        get
        {
            foreach (var e in Entries)
                if (e.Key == key) return e.Value;
            return null;
        }
    }

    /// <summary>Walks a dotted path of map keys.</summary>
    public UnityYamlNode? At(string dottedPath)
    {
        UnityYamlNode? n = this;
        foreach (var part in dottedPath.Split('.'))
        {
            n = n?[part];
            if (n is null) return null;
        }
        return n;
    }

    public bool IsEmpty => Kind == NodeKind.Scalar && Text.Length == 0
        || Kind == NodeKind.Sequence && Items.Count == 0
        || Kind == NodeKind.Map && Entries.Count == 0;

    public bool TryDouble(out double value) =>
        double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    public bool TryLong(out long value) =>
        long.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public override string ToString() => Kind switch
    {
        NodeKind.Scalar => Text,
        NodeKind.Map => "{" + string.Join(", ", Entries.Select(e => e.Key + ": " + e.Value)) + "}",
        _ => "[" + string.Join(", ", Items) + "]",
    };
}

/// <summary>A Unity YAML document: its class id, its file id and its body.</summary>
public sealed record UnityYamlDocument(int ClassId, long FileId, UnityYamlNode Root);

/// <summary>
/// Reads the YAML subset Unity serializes assets and <c>.meta</c> files in.
/// Learned from Unity's published manual on its text serialization and from
/// files a project produced (L1); no Unity code is read or copied.
/// </summary>
public static class UnityYaml
{
    /// <summary>Above this a file is refused rather than read (S1).</summary>
    public const int MaxChars = 16 * 1024 * 1024;

    /// <summary>Flow collections nest no deeper than this.</summary>
    public const int MaxFlowDepth = 16;

    /// <summary>Block structure nests no deeper than this.</summary>
    public const int MaxBlockDepth = 64;

    /// <summary>
    /// Every document in <paramref name="text"/>. A file without <c>---</c>
    /// separators (a <c>.meta</c>) is one document with class id 0.
    /// </summary>
    /// <exception cref="FormatException">Naming <paramref name="label"/> and the line.</exception>
    public static IReadOnlyList<UnityYamlDocument> Parse(string text, string label)
    {
        if (text.Length > MaxChars)
            throw new FormatException($"{label}: larger than {MaxChars / (1024 * 1024)} MB; not read.");
        // Unity never writes a byte-order mark, but an editor or merge tool that
        // touched the file may have; left in, it hides the first line's key.
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        if (text.IndexOf('\0') >= 0)
            throw new FormatException($"{label}: binary data, not a text asset. Set the Unity project's Asset Serialization to Force Text.");

        var docs = new List<UnityYamlDocument>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var classId = 0;
        long fileId = 0;
        var body = new List<(int Indent, string Content, int LineNo)>();

        void Flush()
        {
            if (body.Count == 0) return;
            var p = new BlockParser(body, label);
            docs.Add(new UnityYamlDocument(classId, fileId, p.ParseDocument()));
            body.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            if (raw.Length > 0 && raw[0] == '%') continue;
            if (raw.StartsWith("---", StringComparison.Ordinal))
            {
                Flush();
                (classId, fileId) = ReadDocumentHeader(raw);
                continue;
            }
            if (raw.Trim().Length == 0) continue;
            var indent = 0;
            while (indent < raw.Length && raw[indent] == ' ') indent++;
            if (indent < raw.Length && raw[indent] == '\t')
                throw new FormatException($"{label}: line {i + 1}: a tab in the indentation.");
            body.Add((indent, raw[indent..].TrimEnd(), i + 1));
        }
        Flush();
        return docs;
    }

    // "--- !u!74 &7400000" (and "--- !u!1 &123 stripped").
    private static (int ClassId, long FileId) ReadDocumentHeader(string line)
    {
        var classId = 0;
        long fileId = 0;
        foreach (var part in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("!u!", StringComparison.Ordinal) && int.TryParse(part.AsSpan(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c))
                classId = c;
            else if (part.StartsWith('&') && long.TryParse(part.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var f))
                fileId = f;
        }
        return (classId, fileId);
    }

    private sealed class BlockParser(List<(int Indent, string Content, int LineNo)> lines, string label)
    {
        private int _i;
        // A "- key: value" item is read as a map one level in; the dash's line is
        // re-read at that indent without the dash, which this override supplies.
        private (int Index, int Indent, string Content)? _override;

        private (int Indent, string Content, int LineNo) Line(int i)
        {
            if (_override is { } o && o.Index == i) return (o.Indent, o.Content, lines[i].LineNo);
            return lines[i];
        }

        public UnityYamlNode ParseDocument()
        {
            var node = ParseBlock(Line(0).Indent, 0);
            if (_i < lines.Count)
                throw Error(_i, "unexpected indentation");
            return node;
        }

        private FormatException Error(int i, string what) =>
            new($"{label}: line {lines[Math.Min(i, lines.Count - 1)].LineNo}: {what}.");

        private static bool IsDash(string content) => content == "-" || content.StartsWith("- ", StringComparison.Ordinal);

        private UnityYamlNode ParseBlock(int indent, int depth)
        {
            if (depth > MaxBlockDepth) throw Error(_i, "nested too deeply");
            if (_i >= lines.Count) return UnityYamlNode.Empty;
            return IsDash(Line(_i).Content) ? ParseSequence(indent, depth) : ParseMap(indent, depth);
        }

        private UnityYamlNode ParseMap(int indent, int depth)
        {
            if (depth > MaxBlockDepth) throw Error(_i, "nested too deeply");
            var entries = new List<KeyValuePair<string, UnityYamlNode>>();
            while (_i < lines.Count)
            {
                var (ind, content, _) = Line(_i);
                if (ind < indent) break;
                if (ind > indent) throw Error(_i, "unexpected indentation");
                if (IsDash(content)) break;

                if (!TrySplitKey(content, out var key, out var rest))
                    throw Error(_i, "expected 'key: value'");
                _i++;

                UnityYamlNode value;
                if (rest.Length > 0)
                {
                    value = ParseInline(ContinueScalar(rest, indent), _i - 1);
                }
                else if (_i < lines.Count && Line(_i).Indent > indent)
                {
                    value = ParseBlock(Line(_i).Indent, depth + 1);
                }
                else if (_i < lines.Count && Line(_i).Indent == indent && IsDash(Line(_i).Content))
                {
                    // Unity's style: a sequence's dashes sit at its key's indent.
                    value = ParseSequence(indent, depth + 1);
                }
                else
                {
                    value = UnityYamlNode.Empty;
                }
                entries.Add(new(key, value));
            }
            return UnityYamlNode.Map(entries);
        }

        private UnityYamlNode ParseSequence(int indent, int depth)
        {
            if (depth > MaxBlockDepth) throw Error(_i, "nested too deeply");
            var items = new List<UnityYamlNode>();
            while (_i < lines.Count)
            {
                var (ind, content, _) = Line(_i);
                if (ind != indent || !IsDash(content)) break;

                var after = content.Length > 1 ? content[2..].TrimStart() : "";
                var innerIndent = indent + (content.Length - content[1..].TrimStart().Length);
                if (after.Length == 0)
                {
                    _i++;
                    items.Add(_i < lines.Count && Line(_i).Indent > indent
                        ? ParseBlock(Line(_i).Indent, depth + 1)
                        : UnityYamlNode.Empty);
                }
                else if (IsDash(after))
                {
                    // "- - item": a sequence of sequences, which Unity writes for
                    // custom physics shapes and outlines. The inner one starts here.
                    _override = (_i, innerIndent, after);
                    items.Add(ParseSequence(innerIndent, depth + 1));
                }
                else if (StartsAsKey(after))
                {
                    // "- key: value" opens a map whose first entry is this line.
                    _override = (_i, innerIndent, after);
                    items.Add(ParseMap(innerIndent, depth + 1));
                }
                else
                {
                    _i++;
                    items.Add(ParseInline(ContinueScalar(after, indent), _i - 1));
                }
            }
            return UnityYamlNode.Sequence(items);
        }

        // A plain or quoted scalar can wrap onto deeper-indented lines.
        private string ContinueScalar(string first, int indent)
        {
            if (first.StartsWith('{') || first.StartsWith('[')) return first;
            var sb = new StringBuilder(first);
            while (_i < lines.Count && Line(_i).Indent > indent)
            {
                sb.Append(' ').Append(Line(_i).Content);
                _i++;
            }
            return sb.ToString();
        }

        private static bool StartsAsKey(string s) =>
            s.Length > 0 && s[0] is not ('{' or '[' or '\'' or '"') && TrySplitKey(s, out _, out _);

        private static bool TrySplitKey(string content, out string key, out string rest)
        {
            key = rest = "";
            if (content.Length == 0 || content[0] is '{' or '[' or '\'' or '"') return false;
            var colon = content.IndexOf(": ", StringComparison.Ordinal);
            if (colon < 0)
            {
                if (!content.EndsWith(':')) return false;
                key = content[..^1];
                return key.Length > 0;
            }
            key = content[..colon];
            rest = content[(colon + 2)..].Trim();
            return key.Length > 0;
        }

        private UnityYamlNode ParseInline(string text, int lineIndex)
        {
            if (text.StartsWith('{') || text.StartsWith('['))
            {
                var pos = 0;
                var node = ParseFlow(text, ref pos, 0, lineIndex);
                SkipSpaces(text, ref pos);
                if (pos != text.Length) throw Error(lineIndex, "text after a closed bracket");
                return node;
            }
            return UnityYamlNode.Scalar(Unquote(text, lineIndex));
        }

        private UnityYamlNode ParseFlow(string s, ref int pos, int depth, int lineIndex)
        {
            if (depth > MaxFlowDepth) throw Error(lineIndex, "brackets nested too deeply");
            SkipSpaces(s, ref pos);
            if (pos >= s.Length) throw Error(lineIndex, "unclosed bracket");

            if (s[pos] == '{')
            {
                pos++;
                var entries = new List<KeyValuePair<string, UnityYamlNode>>();
                while (true)
                {
                    SkipSpaces(s, ref pos);
                    if (pos >= s.Length) throw Error(lineIndex, "unclosed '{'");
                    if (s[pos] == '}') { pos++; break; }
                    var colon = s.IndexOf(':', pos);
                    if (colon < 0) throw Error(lineIndex, "a flow map entry without ':'");
                    var key = s[pos..colon].Trim();
                    pos = colon + 1;
                    var value = ParseFlowValue(s, ref pos, depth, lineIndex);
                    entries.Add(new(key, value));
                    ExpectSeparator(s, ref pos, '}', lineIndex);
                }
                return UnityYamlNode.Map(entries);
            }

            if (s[pos] == '[')
            {
                pos++;
                var items = new List<UnityYamlNode>();
                while (true)
                {
                    SkipSpaces(s, ref pos);
                    if (pos >= s.Length) throw Error(lineIndex, "unclosed '['");
                    if (s[pos] == ']') { pos++; break; }
                    items.Add(ParseFlowValue(s, ref pos, depth, lineIndex));
                    ExpectSeparator(s, ref pos, ']', lineIndex);
                }
                return UnityYamlNode.Sequence(items);
            }

            throw Error(lineIndex, "expected '{' or '['");
        }

        private UnityYamlNode ParseFlowValue(string s, ref int pos, int depth, int lineIndex)
        {
            SkipSpaces(s, ref pos);
            if (pos < s.Length && s[pos] is '{' or '[')
                return ParseFlow(s, ref pos, depth + 1, lineIndex);

            var start = pos;
            if (pos < s.Length && s[pos] is '\'' or '"')
            {
                var q = s[pos++];
                while (pos < s.Length)
                {
                    if (s[pos] == q)
                    {
                        if (q == '\'' && pos + 1 < s.Length && s[pos + 1] == '\'') { pos += 2; continue; }
                        pos++;
                        break;
                    }
                    if (q == '"' && s[pos] == '\\') pos++;
                    pos++;
                }
            }
            else
            {
                while (pos < s.Length && s[pos] is not (',' or '}' or ']')) pos++;
            }
            return UnityYamlNode.Scalar(Unquote(s[start..Math.Min(pos, s.Length)].Trim(), lineIndex));
        }

        // After a flow value comes ',' or the closer and nothing else. Without this a
        // stray closer of the other kind ("[}") is never consumed and the loop spins.
        private void ExpectSeparator(string s, ref int pos, char closer, int lineIndex)
        {
            SkipSpaces(s, ref pos);
            if (pos >= s.Length) throw Error(lineIndex, $"unclosed '{(closer == '}' ? '{' : '[')}'");
            if (s[pos] == ',') { pos++; return; }
            if (s[pos] != closer) throw Error(lineIndex, $"'{s[pos]}' where ',' or '{closer}' belongs");
        }

        private static void SkipSpaces(string s, ref int pos)
        {
            while (pos < s.Length && s[pos] == ' ') pos++;
        }

        private string Unquote(string s, int lineIndex)
        {
            if (s.Length >= 1 && s[0] == '\'')
            {
                if (s.Length < 2 || s[^1] != '\'') throw Error(lineIndex, "unclosed quote");
                return s[1..^1].Replace("''", "'");
            }
            if (s.Length >= 1 && s[0] == '"')
            {
                var trailing = 0;
                for (var k = s.Length - 2; k >= 1 && s[k] == '\\'; k--) trailing++;
                // An odd run of backslashes before the last quote escapes it.
                if (s.Length < 2 || s[^1] != '"' || trailing % 2 == 1) throw Error(lineIndex, "unclosed quote");
                var inner = s[1..^1];
                var sb = new StringBuilder(inner.Length);
                for (var k = 0; k < inner.Length; k++)
                {
                    if (inner[k] != '\\' || k + 1 >= inner.Length) { sb.Append(inner[k]); continue; }
                    var c = inner[++k];
                    sb.Append(c switch { 'n' => '\n', 't' => '\t', '\\' => '\\', '"' => '"', '/' => '/', _ => c });
                }
                return sb.ToString();
            }
            return s;
        }
    }
}
