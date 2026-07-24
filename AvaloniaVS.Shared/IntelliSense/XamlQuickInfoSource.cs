using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Ide.CompletionEngine;
using AvaloniaVS.Models;
using AvaloniaVS.Shared.IntelliSense;
using AvaloniaVS.Shared.Services;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Serilog;

namespace AvaloniaVS.IntelliSense
{
    /// <summary>
    /// Provides hover Quick Info for element and attribute names in Avalonia XAML, resolved
    /// against the same <see cref="CompletionEngine"/> metadata used for completions.
    /// </summary>
    internal class XamlQuickInfoSource(ITextBuffer textBuffer, CompletionEngineSource completionEngineSource) : IAsyncQuickInfoSource
    {
        public Task<QuickInfoItem> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            if (!textBuffer.Properties.TryGetProperty<XamlBufferMetadata>(typeof(XamlBufferMetadata), out var metadata) ||
                metadata.CompletionMetadata == null)
            {
                return Task.FromResult<QuickInfoItem>(null);
            }

            var snapshot = textBuffer.CurrentSnapshot;
            var point = session.GetTriggerPoint(snapshot);

            if (point is null)
            {
                return Task.FromResult<QuickInfoItem>(null);
            }

            var sw = Stopwatch.StartNew();
            var text = snapshot.GetText();
            var pos = point.Value.Position;

            if (!TryGetIdentifierSpan(text, pos, out var start, out var end))
            {
                return Task.FromResult<QuickInfoItem>(null);
            }

            textBuffer.Properties.TryGetProperty("AssemblyName", out string assemblyName);

            // Parse only up to the end of the hovered token - this mirrors how the completion
            // engine parses "up to the cursor", so state.TagName/AttributeName come back fully
            // populated for whatever the mouse is sitting on, rather than a partial prefix.
            var textToEnd = text.Substring(0, end);

            var helper = completionEngineSource.CompletionEngine.Helper;
            helper.SetMetadata(metadata.CompletionMetadata, textToEnd, assemblyName);

            if (helper.Metadata == null)
            {
                return Task.FromResult<QuickInfoItem>(null);
            }

            var state = XmlParser.Parse(textToEnd);
            string content = null;

            if (state.State == XmlParser.ParserState.StartElement && !state.IsInClosingTag)
            {
                content = DescribeTagName(helper, state.TagName, metadata.DocCache);
            }
            else if (state.State == XmlParser.ParserState.StartAttribute)
            {
                content = DescribeAttributeName(helper, state.TagName, state.AttributeName, metadata.DocCache);
            }

            sw.Stop();
            Log.Logger.Verbose("XAML QuickInfo took {Time}, hit: {Hit}", sw.Elapsed, content != null);

            if (content == null)
            {
                return Task.FromResult<QuickInfoItem>(null);
            }

            var span = new SnapshotSpan(snapshot, start, end - start);
            var trackingSpan = snapshot.CreateTrackingSpan(span, SpanTrackingMode.EdgeInclusive);

            return Task.FromResult(new QuickInfoItem(trackingSpan, content));
        }

        private static string DescribeTagName(CompletionEngine.MetadataHelper helper, string tagName, XmlDocCache docCache)
        {
            if (string.IsNullOrEmpty(tagName) || tagName.StartsWith("/"))
            {
                return null;
            }

            // Attached-property element syntax, e.g. <Grid.RowDefinitions>
            var dotPos = tagName.IndexOf('.');
            if (dotPos > 0)
            {
                var ownerName = tagName.Substring(0, dotPos);
                var propName = tagName.Substring(dotPos + 1);
                return DescribeProperty(helper.LookupProperty(ownerName, propName), ownerName, propName, docCache);
            }

            return DescribeType(helper.LookupType(tagName), tagName, docCache);
        }

        private static string DescribeAttributeName(CompletionEngine.MetadataHelper helper, string tagName, string attributeName, XmlDocCache docCache)
        {
            if (string.IsNullOrEmpty(attributeName))
            {
                return null;
            }

            // Attached property in attribute position, e.g. Grid.Row="1"
            var dotPos = attributeName.IndexOf('.');
            if (dotPos > 0)
            {
                var split = attributeName.Split(new[] { '.' }, 2);
                return DescribeProperty(helper.LookupProperty(split[0], split[1]), split[0], split[1], docCache);
            }

            if (tagName == null)
            {
                return null;
            }

            return DescribeProperty(helper.LookupProperty(tagName, attributeName), tagName, attributeName, docCache);
        }

        private static string DescribeType(MetadataType type, string tagName, XmlDocCache docCache)
        {
            if (type == null)
            {
                // Not necessarily wrong - could be an unresolved namespace prefix, a markup
                // extension referenced without its "Extension" suffix having matched, etc.
                return null;
            }

            var lines = new List<string> { type.FullName };

            var facts = new List<string>();
            if (type.IsAvaloniaObjectType)
                facts.Add("AvaloniaObject");
            if (type.IsMarkupExtension)
                facts.Add("MarkupExtension");
            if (type.IsEnum)
                facts.Add("enum");
            if (type.IsAbstract)
                facts.Add("abstract");
            if (facts.Count > 0)
            {
                lines.Add(string.Join(", ", facts));
            }

            //if (type.Properties.Count > 0 || type.Events.Count > 0)
            //{
            //    lines.Add($"{type.Properties.Count} properties, {type.Events.Count} events");
            //}

            var summary = docCache?.TryGetTypeSummary(type.FullName);
            if (!string.IsNullOrEmpty(summary))
            {
                lines.Add(summary);
            }

            return string.Join("\n", lines);
        }

        private static string DescribeProperty(MetadataProperty prop, string ownerName, string propName, XmlDocCache docCache)
        {
            if (prop == null)
            {
                return null;
            }

            //var kind = prop.IsAttached ? "attached property" : "property";
            //var access = prop.HasGetter && prop.HasSetter ? "get; set;"
            //   : prop.HasGetter ? "get;"
            //   : "set;";

            var owner = prop.DeclaringType?.Name ?? ownerName;
            var typeName = prop.Type?.Name ?? "object";

            var lines = new List<string>
            {
                $"{owner}.{prop.Name} : {typeName}",
               // $"{kind} \u2014 {access}"
            };

            var summary = docCache?.TryGetPropertySummary(prop.DeclaringType?.FullName ?? ownerName, prop.Name);
            if (!string.IsNullOrEmpty(summary))
            {
                lines.Add(summary);
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Finds the contiguous run of identifier characters (including '.' and ':' so that
        /// dotted attached-property names and xmlns-prefixed names count as one token) that
        /// the given position sits inside of.
        /// </summary>
        private static bool TryGetIdentifierSpan(string text, int pos, out int start, out int end)
        {
            start = end = pos;

            if (text.Length == 0)
            {
                return false;
            }

            // If we're right on a boundary, prefer the token to the left (matches how a mouse
            // cursor visually sits between/on characters when hovering a word).
            var probe = pos < text.Length && IsIdentifierChar(text[pos])
                ? pos
                : pos > 0 && IsIdentifierChar(text[pos - 1]) ? pos - 1 : -1;

            if (probe < 0)
            {
                return false;
            }

            start = probe;
            while (start > 0 && IsIdentifierChar(text[start - 1]))
            {
                start--;
            }

            end = probe + 1;
            while (end < text.Length && IsIdentifierChar(text[end]))
            {
                end++;
            }

            return true;
        }

        private static bool IsIdentifierChar(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == ':';

        public void Dispose()
        {
        }
    }
}
