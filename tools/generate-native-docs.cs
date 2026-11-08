// The parser behind tools/generate-native-docs.ps1, compiled by its Add-Type: Slang's header
// comments, attached to the declarations they touch, written as XML docs into ClangSharp's output.
// Not part of any project; the script's header says what it carries and what it does not.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpSlangDocs
{
    public sealed class Report
    {
        public int Types, Members, Slots, TypeComments, MemberComments, SlotComments;
        public List<string> Warnings = new List<string>();
        public List<string> NotCarried = new List<string>();
    }

    internal sealed class Comment
    {
        public string Text;
        public int StartLine, EndLine, Column;
        public bool Line;        // a // comment
        public bool TrailMarker; // //<, ///<, /**<, /*!<, //!<
    }

    internal sealed class Decl
    {
        public List<Comment> Doc = new List<Comment>();
        public void Add(IEnumerable<Comment> c) { Doc.AddRange(c); }
    }

    internal sealed class Scope
    {
        public string Kind;   // "ns", "struct", "enum", "anon"
        public string Path;   // "IGlobalSession", "SpecializationArg.Kind"; null when anonymous
        public Decl TypeDecl;
    }

    public static class Carrier
    {
        // ------------------------------------------------------------------ results of the header pass

        private static readonly Dictionary<string, Decl> TypeDocs = new Dictionary<string, Decl>();
        private static readonly Dictionary<string, Decl> MemberDocs = new Dictionary<string, Decl>();
        private static readonly HashSet<string> Ambiguous = new HashSet<string>();
        private static readonly Dictionary<string, List<KeyValuePair<string, Decl>>> Slots = new Dictionary<string, List<KeyValuePair<string, Decl>>>();
        private static readonly Dictionary<string, string> Bases = new Dictionary<string, string>();
        private static readonly HashSet<string> Carried = new HashSet<string>();
        private static readonly Dictionary<string, Decl> IntegralDocs = new Dictionary<string, Decl>();

        public static Report Run(string[] headers, string[] files)
        {
            TypeDocs.Clear(); MemberDocs.Clear(); Ambiguous.Clear(); Slots.Clear(); Bases.Clear(); Carried.Clear(); IntegralDocs.Clear();
            foreach (var h in headers) Parse(h.Replace("\r\n", "\n"));

            var report = new Report
            {
                TypeComments = TypeDocs.Count(d => d.Value.Doc.Count > 0),
                MemberComments = MemberDocs.Count(d => d.Value.Doc.Count > 0 && !Ambiguous.Contains(d.Key)),
                SlotComments = Slots.Sum(s => s.Value.Count(m => m.Value.Doc.Count > 0)),
            };
            foreach (var f in files.OrderBy(f => f, StringComparer.Ordinal)) Apply(f, report);
            report.NotCarried.AddRange(TypeDocs.Where(d => d.Value.Doc.Count > 0 && !Carried.Contains("T:" + d.Key)).Select(d => "type " + d.Key));
            report.NotCarried.AddRange(MemberDocs.Where(d => d.Value.Doc.Count > 0 && !Ambiguous.Contains(d.Key) && !Carried.Contains("M:" + d.Key)).Select(d => "member " + d.Key));
            report.NotCarried.Sort(StringComparer.Ordinal);
            return report;
        }

        // ------------------------------------------------------------------ the header

        private static void Parse(string text)
        {
            var pending = new List<Comment>();
            int pendingEnd = -10;
            var stmt = new StringBuilder();
            Decl stmtDecl = null;
            Decl last = null; int lastEnd = -10;
            Decl trailTarget = null; int trailLine = -10, trailCol = -1;
            Decl openerDecl = null; int openerLine = -10;
            int lastCodeLine = -10, lastPpLine = -10;
            var scopes = new List<Scope> { new Scope { Kind = "ns" } };
            int skipDepth = 0; // inside a function body or an initializer

            int line = 1, col = 0, i = 0, n = text.Length;

            while (i < n)
            {
                char c = text[i];
                if (c == '\n') { line++; col = 0; i++; continue; }
                if (c == ' ' || c == '\t' || c == '\r') { i++; col++; continue; }

                // comments
                if (c == '/' && i + 1 < n && (text[i + 1] == '/' || text[i + 1] == '*'))
                {
                    var cm = new Comment { StartLine = line, Column = col, Line = text[i + 1] == '/' };
                    int start = i;
                    if (cm.Line)
                    {
                        while (i < n && text[i] != '\n') { i++; col++; }
                    }
                    else
                    {
                        i += 2; col += 2;
                        while (i < n && !(text[i] == '*' && i + 1 < n && text[i + 1] == '/'))
                        {
                            if (text[i] == '\n') { line++; col = 0; } else col++;
                            i++;
                        }
                        i = Math.Min(n, i + 2); col += 2;
                    }
                    cm.EndLine = line;
                    cm.Text = text.Substring(start, i - start);
                    cm.TrailMarker = Regex.IsMatch(cm.Text, @"^(//[/!]?<|/\*[*!]<)");
                    if (skipDepth > 0) continue;
                    if (cm.StartLine == lastPpLine) continue;

                    if (cm.StartLine == lastCodeLine || cm.TrailMarker)
                    {
                        if (stmt.Length > 0) { (stmtDecl ?? (stmtDecl = new Decl())).Doc.Add(cm); continue; }
                        if (last != null && (lastEnd == cm.StartLine || cm.TrailMarker)) { last.Doc.Add(cm); trailTarget = last; trailLine = cm.EndLine; trailCol = cm.Column; continue; }
                        if (openerDecl != null && openerLine == cm.StartLine) { openerDecl.Doc.Add(cm); continue; }
                    }
                    if (cm.Line && trailTarget != null && cm.StartLine == trailLine + 1 && cm.Column == trailCol)
                    {
                        trailTarget.Doc.Add(cm); trailLine = cm.EndLine; continue;
                    }
                    if (stmt.Length > 0) { (stmtDecl ?? (stmtDecl = new Decl())).Doc.Add(cm); continue; }
                    if (pending.Count > 0 && cm.StartLine > pendingEnd + 1) pending.Clear();
                    pending.Add(cm); pendingEnd = cm.EndLine;
                    trailTarget = null;
                    continue;
                }

                // preprocessor lines, with their continuations
                if (c == '#' && IsLineStart(text, i))
                {
                    while (i < n && text[i] != '\n')
                    {
                        if (text[i] == '\\' && i + 1 < n && text[i + 1] == '\n') { i += 2; line++; col = 0; continue; }
                        i++; col++;
                    }
                    lastPpLine = line; pending.Clear(); trailTarget = null;
                    continue;
                }

                // string and character literals
                if (c == '"' || c == '\'')
                {
                    int start = i; i++; col++;
                    while (i < n && text[i] != c && text[i] != '\n') { if (text[i] == '\\') { i++; col++; } i++; col++; }
                    i++; col++;
                    if (skipDepth == 0) { StartStatement(ref stmtDecl, stmt, pending, pendingEnd, line); stmt.Append(text, start, i - start).Append(' '); lastCodeLine = line; }
                    continue;
                }

                if (skipDepth > 0)
                {
                    if (c == '{') skipDepth++;
                    else if (c == '}')
                    {
                        skipDepth--;
                        if (skipDepth == 0)
                        {
                            // a function body ends its declaration; an initializer does not
                            var s = Squash(stmt.ToString());
                            if (s.Contains("(") && !s.Contains("="))
                            {
                                last = Finish(scopes, s, stmtDecl, true); lastEnd = line;
                                stmt.Clear(); stmtDecl = null;
                            }
                        }
                    }
                    i++; col++; lastCodeLine = line;
                    continue;
                }

                if (c == ';' || c == ',' || c == '{' || c == '}')
                {
                    var top = scopes[scopes.Count - 1];
                    var s = Squash(stmt.ToString());
                    lastCodeLine = line; trailTarget = null; openerDecl = null;
                    if (c == ',')
                    {
                        if (top.Kind == "enum" || top.Kind == "anonenum")
                        {
                            last = s.Length > 0 ? Finish(scopes, s, stmtDecl, false) : null; lastEnd = line;
                            stmt.Clear(); stmtDecl = null;
                        }
                        else { StartStatement(ref stmtDecl, stmt, pending, pendingEnd, line); stmt.Append(", "); }
                    }
                    else if (c == ';')
                    {
                        last = s.Length > 0 ? Finish(scopes, s, stmtDecl, false) : null; lastEnd = line;
                        stmt.Clear(); stmtDecl = null;
                    }
                    else if (c == '{')
                    {
                        if (stmt.Length == 0) StartStatement(ref stmtDecl, stmt, pending, pendingEnd, line);
                        var opened = Open(scopes, s, stmtDecl ?? new Decl());
                        if (opened == null)
                        {
                            skipDepth = 1; // a function body or an initializer: the statement goes on
                        }
                        else
                        {
                            scopes.Add(opened);
                            openerDecl = opened.TypeDecl; openerLine = line;
                            stmt.Clear(); stmtDecl = null; last = null;
                        }
                    }
                    else // '}'
                    {
                        if ((top.Kind == "enum" || top.Kind == "anonenum") && s.Length > 0)
                        {
                            last = Finish(scopes, s, stmtDecl, false); lastEnd = line;
                        }
                        else last = null;
                        stmt.Clear(); stmtDecl = null;
                        if (scopes.Count > 1) scopes.RemoveAt(scopes.Count - 1);
                    }
                    i++; col++;
                    continue;
                }

                // an interface's IID macro: a statement with no semicolon, and braces in its arguments
                if (stmt.Length == 0 && Regex.IsMatch(text.Substring(i, Math.Min(32, n - i)), @"^SLANG_(COM_INTERFACE|CLASS_GUID)\b"))
                {
                    int depth = 0;
                    while (i < n)
                    {
                        if (text[i] == '\n') { line++; col = 0; } else col++;
                        if (text[i] == '(') depth++;
                        if (text[i] == ')' && --depth == 0) { i++; break; }
                        i++;
                    }
                    pending.Clear(); lastCodeLine = line;
                    continue;
                }

                // code
                int st = i;
                while (i < n && !char.IsWhiteSpace(text[i]) && text[i] != ';' && text[i] != ',' && text[i] != '{' && text[i] != '}' && text[i] != '"' && text[i] != '\''
                       && !(text[i] == '/' && i + 1 < n && (text[i + 1] == '/' || text[i + 1] == '*')))
                {
                    i++; col++;
                }
                if (i == st) { i++; col++; }
                StartStatement(ref stmtDecl, stmt, pending, pendingEnd, line);
                stmt.Append(text, st, i - st).Append(' ');
                lastCodeLine = line; trailTarget = null;
            }
        }

        private static bool IsLineStart(string text, int i)
        {
            for (int j = i - 1; j >= 0 && text[j] != '\n'; j--) if (text[j] != ' ' && text[j] != '\t') return false;
            return true;
        }

        private static void StartStatement(ref Decl stmtDecl, StringBuilder stmt, List<Comment> pending, int pendingEnd, int line)
        {
            if (stmt.Length > 0) return;
            stmtDecl = new Decl();
            if (pending.Count > 0 && line <= pendingEnd + 1) stmtDecl.Add(pending);
            pending.Clear();
        }

        private static string Squash(string s) { return Regex.Replace(s, @"\s+", " ").Trim(); }

        private static readonly Regex Ident = new Regex(@"[A-Za-z_]\w*");

        private static string TypePath(List<Scope> scopes, string name)
        {
            var outer = scopes.LastOrDefault(s => s.Kind == "struct");
            if (name == "Attribute") name = "SlangAttribute";
            return outer == null ? name : outer.Path + "." + name;
        }

        /// <summary>A scope for a block opener, or null for a function body or an initializer.</summary>
        private static Scope Open(List<Scope> scopes, string s, Decl decl)
        {
            if (s.StartsWith("namespace") || s.StartsWith("extern \"C\"") || s == "SLANG_EXTERN_C" || s == "") return new Scope { Kind = "ns" };
            var m = Regex.Match(s, @"^(?:typedef )?(?:struct|class|union)\b(.*)$");
            if (m.Success && !s.Contains("("))
            {
                var rest = m.Groups[1].Value;
                string bas = null;
                var colon = rest.IndexOf(':');
                if (colon >= 0)
                {
                    var b = Ident.Matches(rest.Substring(colon + 1)).Cast<Match>().Select(x => x.Value).Where(x => x != "public" && x != "slang" && x != "virtual").LastOrDefault();
                    bas = b; rest = rest.Substring(0, colon);
                }
                var name = Ident.Matches(rest).Cast<Match>().Select(x => x.Value).Where(x => !x.StartsWith("SLANG_")).FirstOrDefault();
                if (name == null || scopes.Any(x => x.Kind == "anon")) return new Scope { Kind = "anon" };
                var path = TypePath(scopes, name);
                if (bas != null) Bases[path] = bas == "Attribute" ? "SlangAttribute" : bas;
                Merge(TypeDocs, path, decl);
                return new Scope { Kind = "struct", Path = path, TypeDecl = TypeDocs[path] };
            }
            var e = Regex.Match(s, @"^(?:typedef )?enum(?: class| struct)?\s*([A-Za-z_]\w*)?\s*(?::.*)?$");
            if (e.Success)
            {
                if (!e.Groups[1].Success || scopes.Any(x => x.Kind == "anon")) return new Scope { Kind = "anonenum", Path = null };
                var path = TypePath(scopes, e.Groups[1].Value);
                var integral = Regex.Match(s, @":\s*(\w+Integral)\s*$");
                Decl above;
                if (decl.Doc.Count == 0 && integral.Success && IntegralDocs.TryGetValue(integral.Groups[1].Value, out above)) decl = above;
                Merge(TypeDocs, path, decl);
                return new Scope { Kind = "enum", Path = path, TypeDecl = TypeDocs[path] };
            }
            if (s.Contains("(") || s.Contains("=") || s.Contains("operator")) return null;
            return new Scope { Kind = "anon" };
        }

        private static void Merge(Dictionary<string, Decl> into, string key, Decl decl)
        {
            Decl existing;
            if (into.TryGetValue(key, out existing)) { if (existing.Doc.Count == 0) existing.Doc.AddRange(decl.Doc); }
            else into[key] = decl;
        }

        private static void Member(string key, Decl decl)
        {
            if (MemberDocs.ContainsKey(key)) { Ambiguous.Add(key); return; }
            MemberDocs[key] = decl;
        }

        /// <summary>Records a finished declaration in its scope; returns what a trailing comment attaches to.</summary>
        private static Decl Finish(List<Scope> scopes, string s, Decl decl, bool hadBody)
        {
            decl = decl ?? new Decl();
            var top = scopes[scopes.Count - 1];
            if (top.Kind == "anon") return decl;
            if (top.Kind == "enum" || top.Kind == "anonenum")
            {
                var m = Regex.Match(s, @"^([A-Za-z_]\w*)");
                if (!m.Success) return decl;
                var owner = top.Kind == "enum" ? top.Path : (scopes.LastOrDefault(x => x.Kind == "struct") == null ? "SlangApi" : scopes.Last(x => x.Kind == "struct").Path);
                Member(owner + "." + m.Groups[1].Value, decl);
                return decl;
            }
            if (s.StartsWith("typedef"))
            {
                // slang.h documents an enum above the integer typedef that precedes it: typedef ... SlangStageIntegral; enum SlangStage : SlangStageIntegral
                var td = Regex.Match(s, @"^typedef .*\b(\w+Integral)$");
                if (td.Success && decl.Doc.Count > 0) IntegralDocs[td.Groups[1].Value] = decl;
                return decl;
            }
            if (s.StartsWith("using ") || s.StartsWith("friend ") || s.StartsWith("template")) return decl;

            if (top.Kind == "struct")
            {
                var fp = Regex.Match(s, @"\(\s*(?:SLANG_[A-Z_]+\s*)*\*\s*([A-Za-z_]\w*)\s*\)\s*\(");
                if (fp.Success) { Member(top.Path + "." + fp.Groups[1].Value, decl); return decl; }
                if (s.Split('=')[0].Contains("("))
                {
                    var name = FunctionName(s);
                    if (name == null) return decl;
                    if (Regex.IsMatch(s, @"\bvirtual\b"))
                    {
                        List<KeyValuePair<string, Decl>> list;
                        if (!Slots.TryGetValue(top.Path, out list)) Slots[top.Path] = list = new List<KeyValuePair<string, Decl>>();
                        list.Add(new KeyValuePair<string, Decl>(name, decl));
                    }
                    else Member(top.Path + "." + name, decl);
                    return decl;
                }
                var field = Regex.Replace(s, @"=.*$", "");
                field = Regex.Replace(field, @"(\[[^\]]*\]\s*)+$", "").Trim();
                field = Regex.Replace(field, @":\s*\d+\s*$", "").Trim();
                var f = Regex.Match(field, @"([A-Za-z_]\w*)\s*$");
                if (f.Success && field.Contains(" ")) Member(top.Path + "." + f.Groups[1].Value, decl);
                return decl;
            }

            // namespace scope: flat functions and inline helpers, all on SlangApi
            if (s.Contains("("))
            {
                var name = FunctionName(s);
                if (name != null) Member("SlangApi." + name, decl);
            }
            return decl;
        }

        private static string FunctionName(string s)
        {
            foreach (Match m in Regex.Matches(s, @"([A-Za-z_]\w*)\s*\("))
            {
                var w = m.Groups[1].Value;
                if (w.StartsWith("SLANG_") || w == "operator" || w == "alignas" || w == "decltype" || w == "sizeof" || w == "alignof" || w == "static_assert" || w.EndsWith("_t")) continue;
                return w;
            }
            return null;
        }

        // ------------------------------------------------------------------ comment text to XML

        private static string Escape(string s)
        {
            s = s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            return Regex.Replace(s, @"`([^`]+)`", "<c>$1</c>");
        }

        /// <summary>The comment's lines with the markers removed; blank lines kept as paragraph breaks.</summary>
        private static IEnumerable<string> Lines(Comment c)
        {
            if (c.Line)
            {
                var t = Regex.Replace(c.Text, @"^//[/!]?<?", "");
                yield return t.Trim();
                yield break;
            }
            var body = Regex.Replace(c.Text, @"^/\*[*!]?<?", "");
            body = Regex.Replace(body, @"\*/$", "");
            foreach (var raw in body.Split('\n'))
            {
                var t = raw.Trim();
                if (t.StartsWith("*") && !t.StartsWith("*/")) t = t.Substring(1).Trim();
                yield return t;
            }
        }

        private sealed class Xml
        {
            public List<string> Paragraphs = new List<string>();
            public List<KeyValuePair<string, string>> Params = new List<KeyValuePair<string, string>>();
            public string Returns;
        }

        private static Xml Render(Decl decl)
        {
            var xml = new Xml();
            var text = new StringBuilder();
            StringBuilder param = null; string paramName = null;
            StringBuilder returns = null;
            Action flushParam = () =>
            {
                if (paramName != null) xml.Params.Add(new KeyValuePair<string, string>(paramName, Squash(param.ToString())));
                paramName = null; param = null;
            };
            Action flushText = () =>
            {
                var p = Squash(text.ToString());
                if (p.Length > 0) xml.Paragraphs.Add(p);
                text.Clear();
            };

            foreach (var c in decl.Doc)
            {
                foreach (var raw in Lines(c))
                {
                    var l = raw;
                    if (Regex.IsMatch(l, @"^[-=*/#~_+]{3,}$")) { l = ""; }
                    if (l.Length == 0)
                    {
                        if (paramName != null) flushParam();
                        else if (returns != null) { xml.Returns = Squash(returns.ToString()); returns = null; }
                        else flushText();
                        continue;
                    }
                    l = Regex.Replace(l, @"^[@\\](brief|short)\b\s*", "");
                    var pm = Regex.Match(l, @"^[@\\]param(?:\s*\[[^\]]*\])?\s+([A-Za-z_]\w*)\s*(.*)$");
                    if (pm.Success)
                    {
                        flushParam(); flushText();
                        if (returns != null) { xml.Returns = Squash(returns.ToString()); returns = null; }
                        paramName = pm.Groups[1].Value; param = new StringBuilder(pm.Groups[2].Value).Append(' ');
                        continue;
                    }
                    var rm = Regex.Match(l, @"^[@\\]returns?\b\s*(.*)$");
                    if (rm.Success)
                    {
                        flushParam(); flushText();
                        returns = new StringBuilder(rm.Groups[1].Value).Append(' ');
                        continue;
                    }
                    if (Regex.IsMatch(l, @"^[@\\]\w+"))
                    {
                        // another command (@note, @deprecated, @see, @code): back to the text, word kept
                        flushParam();
                        if (returns != null) { xml.Returns = Squash(returns.ToString()); returns = null; }
                        flushText();
                        l = Regex.Replace(l, @"^[@\\](\w+)", "$1:");
                    }
                    if (paramName != null) param.Append(l).Append(' ');
                    else if (returns != null) returns.Append(l).Append(' ');
                    else text.Append(l).Append(' ');
                }
                // a separate comment is a separate paragraph
                if (paramName == null && returns == null) flushText();
            }
            flushParam();
            if (returns != null) xml.Returns = Squash(returns.ToString());
            flushText();
            if (xml.Returns != null && xml.Returns.Length == 0) xml.Returns = null;
            xml.Params.RemoveAll(p => p.Value.Length == 0);
            return xml;
        }

        private static List<string> Emit(Decl decl, string indent, IList<string> parameters)
        {
            if (decl == null || decl.Doc.Count == 0) return null;
            var xml = Render(decl);
            if (xml.Paragraphs.Count == 0 && xml.Params.Count == 0 && xml.Returns == null) return null;

            // <param> only when the names are exactly the generated parameters (CS1572, CS1573)
            var names = xml.Params.Select(p => p.Key).ToList();
            bool exact = parameters != null && names.Count > 0 && names.Distinct().Count() == names.Count
                         && new HashSet<string>(names).SetEquals(parameters);
            var paragraphs = xml.Paragraphs.Select(Escape).ToList();
            if (!exact) foreach (var p in xml.Params) paragraphs.Add("<c>" + Escape(p.Key) + "</c>: " + Escape(p.Value));

            var lines = new List<string>();
            var summary = paragraphs.Count > 0 ? paragraphs[0] : "";
            Func<string, string> esc = t => t;
            lines.Add(indent + "/// <summary>" + esc(summary) + "</summary>");
            if (paragraphs.Count > 1)
            {
                lines.Add(indent + "/// <remarks>");
                foreach (var p in paragraphs.Skip(1)) lines.Add(indent + "/// <para>" + esc(p) + "</para>");
                lines.Add(indent + "/// </remarks>");
            }
            if (exact)
            {
                foreach (var name in parameters)
                {
                    var p = xml.Params.First(x => x.Key == name);
                    lines.Add(indent + "/// <param name=\"" + name + "\">" + Escape(p.Value) + "</param>");
                }
            }
            if (xml.Returns != null) lines.Add(indent + "/// <returns>" + Escape(xml.Returns) + "</returns>");
            return lines;
        }

        // ------------------------------------------------------------------ the generated files

        private static readonly Regex TypeLine = new Regex(@"^(?<indent>\s*)public (?:(?:unsafe|static|partial|readonly) )*(?:struct|enum|class) (?<name>\w+)");
        private static readonly Regex MethodLine = new Regex(@"^\s*public (?:(?:static|extern|unsafe|readonly|new) )*[^=;]*?\b(?<name>\w+)\((?<params>.*)\)\s*(=>.*)?;?$");
        private static readonly Regex FieldLine = new Regex(@"^\s*public (?:(?:fixed|readonly|static) )*[^=;(]*?\b(?<name>\w+)(\[\w+\])?;\s*$");
        private static readonly Regex PropertyLine = new Regex(@"^\s*public [^=;(]*?\b(?<name>\w+)\s*$");
        private static readonly Regex ConstLine = new Regex(@"^\s*public (?:const|static readonly) [^=]*?\b(?<name>\w+) =");
        private static readonly Regex EnumMemberLine = new Regex(@"^\s*(?<name>[A-Za-z_]\w*)( = [^,]*)?,?\s*$");

        private static IList<string> ParameterNames(string list)
        {
            var names = new List<string>();
            int depth = 0; var cur = new StringBuilder();
            foreach (var ch in list + ",")
            {
                if (ch == '(' || ch == '[' || ch == '<') depth++;
                if (ch == ')' || ch == ']' || ch == '>') depth--;
                if (ch == ',' && depth == 0)
                {
                    var p = Regex.Replace(cur.ToString(), @"\[[^\]]*\]", "");
                    p = Regex.Replace(p, @"=.*$", "").Trim();
                    var m = Regex.Match(p, @"([A-Za-z_]\w*)\s*$");
                    if (m.Success && p.Length > 0) names.Add(m.Groups[1].Value);
                    cur.Clear();
                    continue;
                }
                cur.Append(ch);
            }
            return names;
        }

        private static List<KeyValuePair<string, Decl>> Chain(string type)
        {
            var result = new List<KeyValuePair<string, Decl>>();
            string bas;
            if (Bases.TryGetValue(type, out bas) && bas != type) result.AddRange(Chain(bas));
            List<KeyValuePair<string, Decl>> own;
            if (Slots.TryGetValue(type, out own)) result.AddRange(own);
            return result;
        }

        private static void Apply(string file, Report report)
        {
            var lines = File.ReadAllText(file).Split('\n').ToList();
            if (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);
            if (lines.Any(l => l.TrimStart().StartsWith("///"))) return; // carries its own docs (_InterfaceGuids.cs, the helper attributes)

            var inserts = new SortedDictionary<int, List<string>>();
            var stack = new List<KeyValuePair<int, string>>(); // indent, path
            var vtable = new Dictionary<string, List<KeyValuePair<int, string>>>(); // type -> (line, name) of [VtblIndex] methods
            var isEnum = new HashSet<string>();

            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                var tm = TypeLine.Match(l);
                if (tm.Success)
                {
                    int ind = tm.Groups["indent"].Value.Length;
                    while (stack.Count > 0 && stack[stack.Count - 1].Key >= ind) stack.RemoveAt(stack.Count - 1);
                    var name = tm.Groups["name"].Value;
                    var path = stack.Count == 0 ? name : stack[stack.Count - 1].Value + "." + name;
                    stack.Add(new KeyValuePair<int, string>(ind, path));
                    if (l.Contains(" enum ")) isEnum.Add(path);
                    Decl d;
                    if (TypeDocs.TryGetValue(path, out d))
                    {
                        var doc = Emit(d, tm.Groups["indent"].Value, null);
                        if (doc != null) { inserts[AttributeStart(lines, i)] = doc; report.Types++; Carried.Add("T:" + path); }
                    }
                    continue;
                }
                if (stack.Count == 0 || l.Trim().Length == 0 || l.TrimStart().StartsWith("[") || l.TrimStart().StartsWith("//")) continue;

                int indent = l.Length - l.TrimStart().Length;
                var owner = stack.LastOrDefault(s => s.Key + 4 == indent);
                if (owner.Value == null) continue;
                var typePath = owner.Value;

                string member = null; IList<string> parameters = null; bool slot = false;
                Match m;
                if (isEnum.Contains(typePath))
                {
                    if ((m = EnumMemberLine.Match(l)).Success) member = m.Groups["name"].Value;
                }
                else if ((m = ConstLine.Match(l)).Success) member = m.Groups["name"].Value;
                else if ((m = MethodLine.Match(l)).Success)
                {
                    member = m.Groups["name"].Value;
                    parameters = ParameterNames(m.Groups["params"].Value);
                    slot = i > 0 && PrecedingAttributes(lines, i).Any(a => a.StartsWith("[VtblIndex("));
                }
                else if ((m = FieldLine.Match(l)).Success) member = m.Groups["name"].Value;
                else if (i + 1 < lines.Count && lines[i + 1].Trim() == "{" && (m = PropertyLine.Match(l)).Success) member = m.Groups["name"].Value; // a bitfield
                if (member == null) continue;

                if (slot)
                {
                    List<KeyValuePair<int, string>> list;
                    if (!vtable.TryGetValue(typePath, out list)) vtable[typePath] = list = new List<KeyValuePair<int, string>>();
                    list.Add(new KeyValuePair<int, string>(i, member));
                    continue;
                }

                Decl md;
                var key = typePath + "." + member;
                if (!Ambiguous.Contains(key) && MemberDocs.TryGetValue(key, out md))
                {
                    var doc = Emit(md, new string(' ', indent), parameters);
                    if (doc != null) { inserts[AttributeStart(lines, i)] = doc; report.Members++; Carried.Add("M:" + key); }
                }
            }

            foreach (var t in vtable)
            {
                var chain = Chain(t.Key);
                if (chain.Count == 0) continue;
                if (chain.Count != t.Value.Count || chain.Where((c, k) => c.Key != t.Value[k].Value).Any())
                {
                    report.Warnings.Add(t.Key + ": " + t.Value.Count + " generated slots, " + chain.Count + " in the header's declaration order, or the names differ: its slot docs are not carried");
                    continue;
                }
                for (int k = 0; k < chain.Count; k++)
                {
                    int at = t.Value[k].Key;
                    var m = MethodLine.Match(lines[at]);
                    var doc = Emit(chain[k].Value, new string(' ', lines[at].Length - lines[at].TrimStart().Length), ParameterNames(m.Groups["params"].Value));
                    if (doc != null) { inserts[AttributeStart(lines, at)] = doc; report.Slots++; }
                }
            }

            if (inserts.Count == 0) return;
            foreach (var ins in inserts.Reverse()) lines.InsertRange(ins.Key, ins.Value);
            File.WriteAllText(file, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        }

        private static IEnumerable<string> PrecedingAttributes(List<string> lines, int i)
        {
            for (int j = i - 1; j >= 0 && lines[j].TrimStart().StartsWith("["); j--) yield return lines[j].Trim();
        }

        private static int AttributeStart(List<string> lines, int i)
        {
            while (i > 0 && lines[i - 1].TrimStart().StartsWith("[")) i--;
            return i;
        }
    }
}
