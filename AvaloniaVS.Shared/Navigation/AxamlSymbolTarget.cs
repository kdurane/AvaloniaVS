using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;

namespace AvaloniaVS.Shared.Navigation
{
    internal enum AxamlSymbolKind
    {
        BindingMember,
        Control,
        ViewModelType
    }

    /// <summary>
    /// What's sitting under the caret/click point, in a form the resolver can act on.
    /// </summary>
    internal sealed class AxamlSymbolTarget(AxamlSymbolKind kind, string qualifiedName, SnapshotSpan span)
    {
        public AxamlSymbolKind Kind { get; } = kind;
        public string QualifiedName { get; } = qualifiedName;
        public SnapshotSpan Span { get; } = span;
    }

    /// <summary>
    /// Deliberately line-local regex matching rather than a full XML parse - same trade-off
    /// as the namespace-suggestion feature. Covers the two cases that matter for navigation:
    /// an element's opening tag name, and a type reference in x:DataType or a
    /// {x:Static}/{d:DesignInstance Type=...} markup extension. DataContext set purely from
    /// code-behind (no x:DataType) isn't visible here - it never was without evaluating C#.
    /// </summary>
    internal static class AxamlSymbolSpanResolver
    {
        private static readonly Regex s_openingTag = new(
            @"<(?<qualified>(?:[A-Za-z_][\w]*:)?[A-Za-z_][\w.]*)",
            RegexOptions.Compiled);

        private static readonly Regex s_typeReferenceAttribute = new(
            @"(?:x:DataType|Type)\s*=\s*""?(?<qualified>(?:[A-Za-z_][\w]*:)?[A-Za-z_][\w.]*)""?",
            RegexOptions.Compiled);

        private static readonly Regex s_bindingPath = new(
            @"\{(?:Binding|CompiledBinding)\s+(?:Path\s*=\s*)?(?<path>[A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)*)",
            RegexOptions.Compiled);

        private static readonly Regex s_dataTypeOnly = new(
            @"x:DataType\s*=\s*""(?<qualified>[^""]+)""",
            RegexOptions.Compiled);

        public static AxamlSymbolTarget TryResolve(SnapshotSpan triggerSpan)
        {
            var snapshot = triggerSpan.Snapshot;
            var line = snapshot.GetLineFromPosition(triggerSpan.Start.Position);
            string lineText = line.GetText();
            int caretOffset = triggerSpan.Start.Position - line.Start.Position;

            return TryMatch(s_openingTag, lineText, caretOffset, line.Start, AxamlSymbolKind.Control)
                ?? TryMatch(s_typeReferenceAttribute, lineText, caretOffset, line.Start, AxamlSymbolKind.ViewModelType)
                ?? TryMatchBindingPath(lineText, caretOffset, line.Start);
        }

        private static AxamlSymbolTarget TryMatch(
            Regex regex, string lineText, int caretOffset, SnapshotPoint lineStart, AxamlSymbolKind kind)
        {
            foreach (Match match in regex.Matches(lineText))
            {
                var group = match.Groups["qualified"];
                if (!group.Success)
                    continue;

                if (caretOffset >= group.Index && caretOffset <= group.Index + group.Length)
                {
                    var span = new SnapshotSpan(lineStart + group.Index, group.Length);
                    return new AxamlSymbolTarget(kind, group.Value, span);
                }
            }

            return null;
        }

        private static AxamlSymbolTarget TryMatchBindingPath(string lineText, int caretOffset, SnapshotPoint lineStart)
        {
            foreach (Match match in s_bindingPath.Matches(lineText))
            {
                var pathGroup = match.Groups["path"];
                if (!pathGroup.Success)
                    continue;
                if (caretOffset < pathGroup.Index || caretOffset > pathGroup.Index + pathGroup.Length)
                    continue;

                string fullPath = pathGroup.Value;
                int caretInPath = caretOffset - pathGroup.Index;
                var segments = fullPath.Split('.');

                int cursor = 0;
                var upToCaret = new List<string>();
                var targetSpan = new SnapshotSpan(lineStart + pathGroup.Index, pathGroup.Length);

                foreach (var seg in segments)
                {
                    upToCaret.Add(seg);
                    int segEnd = cursor + seg.Length;
                    if (caretInPath >= cursor && caretInPath <= segEnd)
                    {
                        targetSpan = new SnapshotSpan(lineStart + pathGroup.Index + cursor, seg.Length);
                        break;
                    }
                    cursor = segEnd + 1; // skip the '.'
                }

                return new AxamlSymbolTarget(AxamlSymbolKind.BindingMember, string.Join(".", upToCaret), targetSpan);
            }
            return null;
        }

        public static string FindEnclosingDataTypeName(SnapshotPoint point)
        {
            string textBefore = point.Snapshot.GetText(0, point.Position);
            var matches = s_dataTypeOnly.Matches(textBefore);
            return matches.Count > 0 ? matches[^1].Groups["qualified"].Value : null;
        }
    }
}
