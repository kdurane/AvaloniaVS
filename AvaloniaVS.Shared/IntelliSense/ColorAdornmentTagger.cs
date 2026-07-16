using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;

namespace AvaloniaVS.Shared.IntelliSense
{
    internal sealed class ColorAdornmentTagger : ITagger<IntraTextAdornmentTag>, IDisposable
    {
        private static readonly Regex s_colorRegex = new(
            @"#(?<hex>[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})" +
            @"|(?<attrName>[A-Za-z_][\w:.]*)=""(?<named>[A-Za-z]+)""",
            RegexOptions.Compiled);

        private static readonly Dictionary<string, Color> s_namedColors =
            typeof(Colors)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(Color))
            .ToDictionary(p => p.Name, p => (Color)p.GetValue(null), StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> s_excludedAttributes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Text", "Content", "Name", "ToolTip"
        };

        private readonly ITextBuffer _buffer;
        private ITextSnapshot _cachedSnapshot;
        private List<(SnapshotSpan Span, Color Color)> _cachedMatches;

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public ColorAdornmentTagger(ITextBuffer buffer)
        {
            _buffer = buffer;
            _buffer.ChangedLowPriority += OnBufferChanged;
        }

        public IEnumerable<ITagSpan<IntraTextAdornmentTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0)
                yield break;

            var snapshot = spans[0].Snapshot;
            var matches = GetOrComputeMatches(snapshot);

            foreach (var (span, color) in matches)
            {
                if (!spans.IntersectsWith(span))
                    continue;

                var adornment = new Border
                {
                    Width = 12,
                    Height = 12,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(color),
                    Margin = new Thickness(2),
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var tag = new IntraTextAdornmentTag(adornment, null, PositionAffinity.Successor);
                yield return new TagSpan<IntraTextAdornmentTag>(new SnapshotSpan(span.Start, 0), tag);
            }
        }

        private List<(SnapshotSpan, Color)> GetOrComputeMatches(ITextSnapshot snapshot)
        {
            if (_cachedSnapshot == snapshot)
                return _cachedMatches;

            var text = snapshot.GetText();
            var results = new List<(SnapshotSpan, Color)>();

            foreach (Match match in s_colorRegex.Matches(text))
            {
                if (match.Groups["hex"].Success)
                {
                    if (TryParseHexColor(match.Groups["hex"].Value, out var color))
                    {
                        var span = new SnapshotSpan(snapshot, match.Groups["hex"].Index - 1, match.Groups["hex"].Length + 1);
                        results.Add((span, color));
                    }
                }
                else if (match.Groups["named"].Success)
                {
                    var attrName = match.Groups["attrName"].Value;

                    // Handle "TextBlock.Text" style attached/qualified names - only care about the tail.
                    var dotIndex = attrName.LastIndexOf('.');
                    var localName = dotIndex >= 0 ? attrName.Substring(dotIndex + 1) : attrName;

                    if (s_excludedAttributes.Contains(localName))
                        continue;

                    if (s_namedColors.TryGetValue(match.Groups["named"].Value, out var namedColor))
                    {
                        var g = match.Groups["named"];
                        var span = new SnapshotSpan(snapshot, g.Index, g.Length);
                        results.Add((span, namedColor));
                    }
                }
            }

            _cachedSnapshot = snapshot;
            _cachedMatches = results;
            return results;
        }

        private static bool TryParseHexColor(string hex, out Color color)
        {
            try
            {
                var argb = hex.Length == 8 ? "#" + hex : "#FF" + hex;
                var converted = ColorConverter.ConvertFromString(argb);
                if (converted is Color c)
                {
                    color = c;
                    return true;
                }
            }
            catch (FormatException)
            {
                // fall through
            }

            color = default;
            return false;
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            _cachedSnapshot = null;
            _cachedMatches = null;

            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                new SnapshotSpan(e.After, 0, e.After.Length)));
        }

        public void Dispose()
        {
            _buffer.ChangedLowPriority -= OnBufferChanged;
        }
    }
}
