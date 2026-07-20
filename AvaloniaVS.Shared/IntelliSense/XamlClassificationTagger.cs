using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Ide.CompletionEngine;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Tagging;

namespace AvaloniaVS.Shared.IntelliSense
{
    internal sealed class XamlClassificationTagger : ITagger<ClassificationTag>, IDisposable
    {

        private readonly ITextBuffer _buffer;
        private readonly IClassificationType _type;
        private readonly IClassificationType _property;
        private readonly IClassificationType _attachedProperty;
        private readonly IClassificationType _markupExtension;
        private readonly IClassificationType _comment;
        private readonly IClassificationType _namespacePrefix;

        private ITextSnapshot _cachedSnapshot;
        private List<TagSpan<ClassificationTag>> _cachedTags;

        private static readonly Regex s_fullCommentRegex = new(@"<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex s_extensionPropertyRegex = new(@"\b(?<prop>[A-Za-z_][\w]*)\s*=", RegexOptions.Compiled);
        private static readonly HashSet<string> s_typeValuedAttributes = ["x:Class", "x:DataType", "x:TypeArguments"];
        private static readonly HashSet<string> s_markupExtensions =
            ["Binding", "StaticResource", "DynamicResource", "TemplateBinding", "x:Static", "x:Type", "x:Null"];

        public XamlClassificationTagger(ITextBuffer buffer, IClassificationTypeRegistryService registry)
        {
            _buffer = buffer;
            _type = registry.GetClassificationType(PredefinedClassificationTypeNames.Type);
            _property = registry.GetClassificationType(PredefinedClassificationTypeNames.MarkupAttribute);
            _attachedProperty = registry.GetClassificationType(PredefinedClassificationTypeNames.MarkupNode);
            _markupExtension = registry.GetClassificationType(XamlClassificationTypeNames.MarkupExtension);
            _comment = registry.GetClassificationType(PredefinedClassificationTypeNames.Comment);
            _namespacePrefix = registry.GetClassificationType(XamlClassificationTypeNames.NamespacePrefix);

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
            if (_cachedSnapshot == snapshot && _cachedTags != null)
                return _cachedTags;

            var text = snapshot.GetText();
            var tags = new List<TagSpan<ClassificationTag>>();

            var comments = FindComments(text);

            foreach (var (Start, End) in comments)
            {
                tags.Add(MakeTag(snapshot, Start, End, _comment));
            }

            bool IsInComment(int position)
            {
                foreach (var (Start, End) in comments)
                {
                    if (position >= Start &&
                        position < End)
                    {
                        return true;
                    }
                }

                return false;
            }

            var parser = new XmlParser(text.AsMemory(), 0);

            foreach (var (state, spanStart, spanEnd, attributeName) in parser.EnumerateStates())
            {
                if (spanEnd <= spanStart || IsInComment(spanStart))
                    continue;

                switch (state)
                {
                    case XmlParser.ParserState.StartElement:
                        ClassifyElement(tags, snapshot, text, spanStart, spanEnd);
                        break;

                    case XmlParser.ParserState.StartAttribute:
                        ClassifyAttribute(tags, snapshot, text, spanStart, spanEnd);
                        break;

                    case XmlParser.ParserState.AttributeValue:
                        ClassifyAttributeValue(tags, snapshot, text, spanStart, spanEnd, attributeName);
                        break;
                }
            }

            _cachedSnapshot = snapshot;
            _cachedTags = tags;
            return tags;
        }

        private void ClassifyAttribute(List<TagSpan<ClassificationTag>> tags, ITextSnapshot snapshot, string text, int start, int end)
        {
            var value = text[start..end];
            var dot = value.IndexOf('.');

            if (dot > 0)
            {
                tags.Add(MakeTag(snapshot, start, start + dot, _type));
                tags.Add(MakeTag(snapshot, start + dot + 1, end, _attachedProperty));
                return;
            }

            tags.Add(MakeTag(snapshot, start, end, _property));
        }

        private void ClassifyElement(List<TagSpan<ClassificationTag>> tags, ITextSnapshot snapshot, string text, int start, int end)
        {
            var span = text.AsSpan(start, end - start);
            var nameStart = start;

            if (span.Length > 0 && span[0] == '<')
            {
                nameStart++;
                if (span.Length > 1 && span[1] == '/')
                {
                    nameStart++;
                }
            }

            // '<' or '</' gets no tag at all here — it'll be picked up as default/grey,
            // matching the closing '>' side you just fixed with s_elementCloseRegex
            var value = text[nameStart..end];
            var dot = value.IndexOf('.');

            if (dot > 0)
            {
                tags.Add(MakeTag(snapshot, nameStart, nameStart + dot, _type));
                tags.Add(MakeTag(snapshot, nameStart + dot + 1, end, _attachedProperty));
                return;
            }

            tags.Add(MakeTag(snapshot, nameStart, end, _type));
        }

        private void ClassifyExtensionProperties(List<TagSpan<ClassificationTag>> tags, ITextSnapshot snapshot, string text, int innerStart, int innerEnd)
        {
            var inner = text.AsSpan(innerStart, innerEnd - innerStart).ToString();

            foreach (Match match in s_extensionPropertyRegex.Matches(inner))
            {
                var group = match.Groups["prop"];
                tags.Add(MakeTag(snapshot, innerStart + group.Index, innerStart + group.Index + group.Length, _attachedProperty));
            }
        }

        private void ClassifyAttributeValue(List<TagSpan<ClassificationTag>> tags, ITextSnapshot snapshot, string text, int start, int end, string attributeName)
        {
            if (attributeName != null && s_typeValuedAttributes.Contains(attributeName))
            {
                ClassifyTypeReference(tags, snapshot, text, start, end);
                return;
            }

            var rawValue = text.AsSpan(start, end - start);
            var value = TrimAttributeQuotes(text.AsSpan(start, end - start));
            var extension = GetMarkupExtension(value);

            if (extension != null)
            {
                var extensionOffset = rawValue.IndexOf(extension.AsSpan());
                var extensionStart = start + extensionOffset;

                tags.Add(MakeTag(snapshot, extensionStart, extensionStart + extension.Length, _markupExtension));

                var braceOpen = rawValue.IndexOf('{');
                var braceClose = braceOpen >= 0 ? FindMatchingBrace(rawValue, braceOpen) : -1;

                if (braceOpen >= 0)
                {
                    if (braceClose > braceOpen)
                    {
                        ClassifyExtensionProperties(tags, snapshot, text, start + braceOpen + 1, start + braceClose);

                        if (extension == "x:Type" || extension == "TemplateBinding")
                        {
                            var argStart = extensionOffset + extension.Length;
                            while (argStart < rawValue.Length && rawValue[argStart] == ' ')
                                argStart++;

                            var argEnd = argStart;
                            while (argEnd < rawValue.Length && rawValue[argEnd] != '}' && rawValue[argEnd] != ',' && rawValue[argEnd] != ' ')
                                argEnd++;

                            if (argEnd > argStart)
                            {
                                ClassifyTypeReference(tags, snapshot, text, start + argStart, start + argEnd);
                            }
                        }
                    }
                }
            }
        }

        private void ClassifyTypeReference(List<TagSpan<ClassificationTag>> tags, ITextSnapshot snapshot, string text, int start, int end)
        {
            var span = text.AsSpan(start, end - start);
            var colon = span.IndexOf(':');

            if (colon > 0)
            {
                tags.Add(MakeTag(snapshot, start, start + colon, _namespacePrefix));
                tags.Add(MakeTag(snapshot, start + colon + 1, end, _type));
            }
            else
            {
                tags.Add(MakeTag(snapshot, start, end, _type));
            }
        }

        private static string GetMarkupExtension(ReadOnlySpan<char> value)
        {
            if (value.IsEmpty || value[0] != '{')
                return null;

            var end = -1;

            for (var i = 1; i < value.Length; i++)
            {
                if (value[i] == ' ' || value[i] == '}')
                {
                    end = i;
                    break;
                }
            }

            if (end <= 1)
                return null;

            var name = value[1..end].ToString();

            return s_markupExtensions.Contains(name)
                ? name
                : null;
        }

        private static ReadOnlySpan<char> TrimAttributeQuotes(ReadOnlySpan<char> value)
        {
            if (value.Length > 0 && (value[0] == '"' || value[0] == '\''))
            {
                value = value[1..];
            }

            if (value.Length > 0 && (value[^1] == '"' || value[^1] == '\''))
            {
                value = value[..^1];
            }

            return value;
        }

        private static List<(int Start, int End)> FindComments(string text)
        {
            return [.. s_fullCommentRegex.Matches(text)
                .Cast<Match>()
                .Select(x => (x.Index, x.Index + x.Length))];
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            // Invalidate cache; next GetTags call recomputes against the new snapshot.
            _cachedSnapshot = null;
            _cachedTags = null;

            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(e.After, 0, e.After.Length)));
        }

        private static TagSpan<ClassificationTag> MakeTag(ITextSnapshot snapshot, int start, int end, IClassificationType type)
        {
            return new TagSpan<ClassificationTag>(new SnapshotSpan(snapshot, start, end - start), new ClassificationTag(type));
        }

        public void Dispose()
        {
            _buffer.ChangedLowPriority -= OnBufferChanged;
        }

        private static int FindMatchingBrace(ReadOnlySpan<char> value, int openIndex)
        {
            var depth = 0;
            for (var i = openIndex; i < value.Length; i++)
            {
                if (value[i] == '{')
                    depth++;
                else if (value[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }
            return -1;
        }
    }
}
