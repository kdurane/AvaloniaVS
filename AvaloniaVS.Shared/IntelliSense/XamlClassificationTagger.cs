using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Tagging;

namespace AvaloniaVS.Shared.IntelliSense
{
    internal sealed class XamlClassificationTagger : ITagger<ClassificationTag>, IDisposable
    {
        private static readonly Regex s_xamlRegex = new(
            @"(?<xamlAttrName>x:Class|x:DataType)=""(?<xamlType>(?:[A-Za-z_][\w]*:)?[A-Za-z_][\w.]*)""" +
            @"|(?<control><\/?(?<type>[A-Za-z_][\w]*:[A-Za-z_][\w]*|[A-Z][\w]*))(?<typeProperty>\.[A-Za-z_][\w]*)?" +
            @"|(?<propertyOwner>[A-Za-z_][\w]*)\.(?<propertyName>[A-Za-z_][\w]*)(?=\s*=)" +
            @"|(?<attribute>[A-Za-z_][\w:]*)(?=\s*=)" +
            @"|\{(?<extension>Binding|StaticResource|DynamicResource)\b",
            RegexOptions.Compiled);

        private readonly ITextBuffer _buffer;
        private readonly IClassificationType _type;
        private readonly IClassificationType _property;
        private readonly IClassificationType _attachedProperty;
        private readonly IClassificationType _string;
        private readonly IClassificationType _markupExtension;
        private readonly IClassificationType _valueText;

        private ITextSnapshot _cachedSnapshot;
        private List<TagSpan<ClassificationTag>> _cachedTags;

        public XamlClassificationTagger(ITextBuffer buffer, IClassificationTypeRegistryService registry)
        {
            _buffer = buffer;
            _type = registry.GetClassificationType(PredefinedClassificationTypeNames.Type);
            _property = registry.GetClassificationType(PredefinedClassificationTypeNames.MarkupAttribute);
            _attachedProperty = registry.GetClassificationType(PredefinedClassificationTypeNames.Identifier);
            _string = registry.GetClassificationType(PredefinedClassificationTypeNames.String);
            _markupExtension = registry.GetClassificationType(XamlClassificationTypeNames.MarkupExtension);
            _valueText = registry.GetClassificationType(XamlClassificationTypeNames.ValueText);

            _buffer.ChangedLowPriority += OnBufferChanged;
        }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public IEnumerable<ITagSpan<ClassificationTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0)
                yield break;

            var snapshot = spans[0].Snapshot;
            var tags = GetOrComputeTags(snapshot);

            foreach (var tag in tags)
            {
                if (spans.IntersectsWith(tag.Span))
                {
                    yield return tag;
                }
            }
        }

        private List<TagSpan<ClassificationTag>> GetOrComputeTags(ITextSnapshot snapshot)
        {
            if (_cachedSnapshot == snapshot)
            {
                return _cachedTags;
            }

            var text = snapshot.GetText();
            var tags = new List<TagSpan<ClassificationTag>>();


            foreach (Match match in s_xamlRegex.Matches(text))
            {
                if (match.Groups["type"].Success)
                {
                    tags.Add(MakeTag(snapshot, match.Groups["type"], _type));
                    if (match.Groups["typeProperty"].Success)
                    {
                        tags.Add(MakeTag(snapshot, match.Groups["typeProperty"], _attachedProperty));
                    }
                }
                else if (match.Groups["xamlAttrName"].Success)
                {
                    tags.Add(MakeTag(snapshot, match.Groups["xamlAttrName"], _property));
                    tags.Add(MakeTag(snapshot, match.Groups["xamlType"], _type));
                }
                else if (match.Groups["propertyOwner"].Success)
                {
                    tags.Add(MakeTag(snapshot, match.Groups["propertyOwner"], _type));
                    tags.Add(MakeTag(snapshot, match.Groups["propertyName"], _attachedProperty));
                }
                else if (match.Groups["attribute"].Success)
                {
                    tags.Add(MakeTag(snapshot, match.Groups["attribute"], _property));
                }
                else if (match.Groups["extension"].Success)
                {
                    tags.Add(MakeTag(snapshot, match.Groups["extension"], _markupExtension));
                }
                else if (match.Groups["value"].Success)
                {
                    tags.Add(MakeTag(snapshot, match.Groups["value"], _valueText));
                }
            }

            _cachedSnapshot = snapshot;
            _cachedTags = tags;
            return tags;
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            // Invalidate cache; next GetTags call recomputes against the new snapshot.
            _cachedSnapshot = null;
            _cachedTags = null;

            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                new SnapshotSpan(e.After, 0, e.After.Length)));
        }

        public void Dispose()
        {
            _buffer.ChangedLowPriority -= OnBufferChanged;
        }

        private static TagSpan<ClassificationTag> MakeTag(ITextSnapshot snapshot, Group group, IClassificationType classification)
        {
            return new TagSpan<ClassificationTag>(new SnapshotSpan(snapshot, group.Index, group.Length), new ClassificationTag(classification));
        }
    }
}
