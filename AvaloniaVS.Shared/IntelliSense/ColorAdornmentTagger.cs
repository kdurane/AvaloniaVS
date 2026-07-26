using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        private static readonly Dictionary<string, Color> s_namedColors =
            typeof(Colors)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(Color))
            .ToDictionary(p => p.Name, p => (Color)p.GetValue(null), StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> s_excludedAttributes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Text", "Content", "Name", "ToolTip"
        };

        private readonly Dictionary<int, (Border Border, SolidColorBrush Brush)> _adornmentCache = [];
        private readonly XamlClassificationTagger _classificationTagger; 
        private readonly EventHandler<SnapshotSpanEventArgs> _onClassificationTagsChanged;

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public ColorAdornmentTagger(XamlClassificationTagger classificationTagger)
        {
            _classificationTagger = classificationTagger;
            _onClassificationTagsChanged = (s, e) => TagsChanged?.Invoke(this, e);
            _classificationTagger.TagsChanged += _onClassificationTagsChanged;
        }

        public IEnumerable<ITagSpan<IntraTextAdornmentTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0)
                yield break;

            var snapshot = spans[0].Snapshot;
            var matches = GetOrComputeMatches(snapshot);
            var seen = new HashSet<int>();

            foreach (var (span, color) in matches)
            {
                if (!spans.IntersectsWith(span))
                    continue;

                var key = span.Start.Position;
                seen.Add(key);

                Border adornment;
                SolidColorBrush brush;
                if (_adornmentCache.TryGetValue(key, out var cached))
                {
                    (adornment, brush) = cached;
                    brush.Color = color;
                }
                else
                {
                    brush = new SolidColorBrush(color);
                    adornment = new Border
                    {
                        Width = 12,
                        Height = 12,
                        CornerRadius = new CornerRadius(2),
                        Background = brush,
                        Margin = new Thickness(2),
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    _adornmentCache[key] = (adornment, brush);
                }

                var tag = new IntraTextAdornmentTag(adornment, null, PositionAffinity.Successor);
                yield return new TagSpan<IntraTextAdornmentTag>(new SnapshotSpan(span.Start, 0), tag);
            }

            foreach (var stale in _adornmentCache.Keys.Except(seen).ToList())
                _adornmentCache.Remove(stale);
        }

        private List<(SnapshotSpan, Color)> GetOrComputeMatches(ITextSnapshot snapshot)
        {
            var results = new List<(SnapshotSpan, Color)>();

            foreach (var (span, attributeName) in _classificationTagger.GetAttributeValueSpans(snapshot))
            {
                var value = span.GetText();

                if (value.Length > 0 && value[0] == '#' && TryParseHexColor(value.TrimStart('#'), out var hex))
                {
                    results.Add((span, hex));
                    continue;
                }

                var localName = attributeName?.Contains('.') == true
                    ? attributeName[(attributeName.LastIndexOf('.') + 1)..] : attributeName;

                if (localName != null && !s_excludedAttributes.Contains(localName) &&
                    s_namedColors.TryGetValue(value, out var named))
                {
                    results.Add((span, named));
                }
            }

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

        public void Dispose()
        {
            _classificationTagger.TagsChanged -= _onClassificationTagsChanged;
        }
    }
}
