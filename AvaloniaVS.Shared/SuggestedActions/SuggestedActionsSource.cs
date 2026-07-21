using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Avalonia.Ide.CompletionEngine;
using AvaloniaVS.Models;
using AvaloniaVS.Shared.SuggestedActions.Actions;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Differencing;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;

namespace AvaloniaVS.Shared.SuggestedActions
{
    internal class SuggestedActionsSource(SuggestedActionsSourceProvider testSuggestedActionsSourceProvider, ITextView textView, ITextBuffer textBuffer,
        IWpfDifferenceViewerFactoryService diffFactory, IDifferenceBufferFactoryService diffBufferFactory, ITextBufferFactoryService bufferFactory,
        ITextEditorFactoryService textEditorFactoryService) : ISuggestedActionsSource
    {
        private readonly SuggestedActionsSourceProvider _factory = testSuggestedActionsSourceProvider;
        private readonly ITextBuffer _textBuffer = textBuffer;
        private readonly IWpfDifferenceViewerFactoryService _diffFactory = diffFactory;
        private readonly IDifferenceBufferFactoryService _diffBufferFactory = diffBufferFactory;
        private readonly ITextBufferFactoryService _bufferFactory = bufferFactory;
        private readonly ITextEditorFactoryService _textEditorFactoryService = textEditorFactoryService;
        private readonly ITextView _textView = textView;

        public event EventHandler<EventArgs> SuggestedActionsChanged;

        public void Dispose()
        {
        }

        public IEnumerable<SuggestedActionSet> GetSuggestedActions(ISuggestedActionCategorySet requestedActionCategories, SnapshotSpan range,
            CancellationToken cancellationToken)
        {
            var availability = SuggestedActionsAreAvailable(range);
            if (!availability.NeedsNamespaceAndAlias && !availability.NeedsAliasOnly && !availability.NeedsNamespaceOnly)
            {
                return [];
            }

            TryGetWordUnderCaret(out var extent);
            var trackingSpan = range.Snapshot.CreateTrackingSpan(extent.Span, SpanTrackingMode.EdgeInclusive);

            ISuggestedAction suggestedAction = availability switch
            {
                { NeedsNamespaceAndAlias: true } => new MissingNamespaceAndAliasSuggestedAction(
                    trackingSpan, _diffFactory, _diffBufferFactory, _bufferFactory, _textEditorFactoryService,
                    availability.TargetClassName, availability.NamespaceValue,
                    CompletionEngine.GetNamespaceAliases(extent.Span.Snapshot.TextBuffer.CurrentSnapshot.GetText())),

                { NeedsAliasOnly: true } => new MissingAliasSuggestedAction(
                    trackingSpan, _diffFactory, _diffBufferFactory, _bufferFactory, _textEditorFactoryService,
                    availability.TargetClassName, availability.NamespaceValue),

                { NeedsNamespaceOnly: true } => new MissingNamespaceSuggestedAction(
                    trackingSpan, _diffFactory, _diffBufferFactory, _bufferFactory, _textEditorFactoryService,
                    availability.NamespaceValue,
                    CompletionEngine.GetNamespaceAliases(extent.Span.Snapshot.TextBuffer.CurrentSnapshot.GetText()),
                    availability.ExistingAlias),

                _ => null
            };

            return suggestedAction is null ? [] : [new SuggestedActionSet("Any", [suggestedAction])];
        }

        public Task<bool> HasSuggestedActionsAsync(ISuggestedActionCategorySet requestedActionCategories, SnapshotSpan range, CancellationToken cancellationToken)
        {
            var availability = SuggestedActionsAreAvailable(range);
            return Task.FromResult(availability.NeedsNamespaceAndAlias || availability.NeedsAliasOnly || availability.NeedsNamespaceOnly);
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }

        private bool TryGetWordUnderCaret(out TextExtent wordExtent)
        {
            var caret = _textView.Caret;
            SnapshotPoint point;

            if (caret.Position.BufferPosition > 0)
            {
                point = caret.Position.BufferPosition - 1;
            }
            else
            {
                wordExtent = default;
                return false;
            }

            var navigator = _factory.NavigatorService.GetTextStructureNavigator(_textBuffer);
            wordExtent = navigator.GetExtentOfWord(point);

            // If the caret landed on a namespace prefix (word immediately followed by ':'),
            // resolve to the class name that follows the colon instead - this is the case
            // where the squiggle/caret sits on "nidea" in "nidea:WindowTopbar" rather than
            // directly on "WindowTopbar".
            var snapshot = wordExtent.Span.Snapshot;
            var afterWord = wordExtent.Span.End;
            if (afterWord.Position < snapshot.Length && snapshot[afterWord.Position] == ':')
            {
                var afterColon = afterWord + 1;
                if (afterColon.Position < snapshot.Length)
                {
                    wordExtent = navigator.GetExtentOfWord(afterColon);
                }
            }

            return true;
        }

        private readonly struct SuggestedActionAvailability(
            bool needsNamespaceAndAlias, bool needsAliasOnly, bool needsNamespaceOnly,
            string targetClassName, string namespaceValue, string existingAlias)
        {
            public bool NeedsNamespaceAndAlias { get; } = needsNamespaceAndAlias;
            public bool NeedsAliasOnly { get; } = needsAliasOnly;
            public bool NeedsNamespaceOnly { get; } = needsNamespaceOnly;
            public string TargetClassName { get; } = targetClassName;
            public string NamespaceValue { get; } = namespaceValue;
            public string ExistingAlias { get; } = existingAlias;
        }

        private static bool TryFindPreferredNamespace(Metadata metadata, string className, out string namespaceValue)
        {
            var matches = metadata.Namespaces
                .Where(ns => ns.Value.ContainsKey(className))
                .Select(ns => ns.Key)
                .ToList();

            if (matches.Count == 0)
            {
                namespaceValue = null;
                return false;
            }

            // A type can legitimately be registered under more than one namespace string
            // (e.g. XmlnsDefinitionAttribute mapping several CLR namespaces to one public
            // xmlns URI). Prefer the friendly URI form over the raw clr-namespace/using: fallback,
            // rather than whichever happened to be registered last during the metadata scan.
            namespaceValue = matches.FirstOrDefault(ns => ns.Contains("://")) ?? matches[0];
            return true;
        }



        /// <returns>
        /// This method returns 3 bool values. First one defines whether MissingNamespaceAndAliasSuggestedAction should be applied
        /// Second one defines whether MissingAliasSuggestedAction should be applied.
        /// Third one defines whether MissingNamespaceSuggestedAction should be applied.
        /// </returns>
        private SuggestedActionAvailability SuggestedActionsAreAvailable(SnapshotSpan range)
        {
            if (!TryGetWordUnderCaret(out var extent))
            {
                return default;
            }

            var span = range.Snapshot.CreateTrackingSpan(extent.Span, SpanTrackingMode.EdgeInclusive);
            var snapshot = span.TextBuffer.CurrentSnapshot;
            var targetClassName = span.GetText(snapshot);

            span.TextBuffer.Properties.TryGetProperty<XamlBufferMetadata>(typeof(XamlBufferMetadata), out var bufferMetadata);
            if (bufferMetadata?.CompletionMetadata is not { } metadata)
            {
                return default;
            }

            if (!TryFindPreferredNamespace(metadata, targetClassName, out var namespaceValue))
            {
                return default;
            }

            // Exclude built-in Avalonia controls - these are included by default and never need a fix.
            if (metadata.Namespaces.TryGetValue("https://github.com/avaloniaui", out var avaloniaTypes)
                && avaloniaTypes.ContainsKey(targetClassName))
            {
                return default;
            }

            var documentText = span.TextBuffer.CurrentSnapshot.GetText();
            var existingAliases = CompletionEngine.GetNamespaceAliases(documentText);
            var namespaceAlreadyAliased = existingAliases.ContainsValue(namespaceValue);
            var hasAlias = HasAlias(out var currentAlias);

            if (!namespaceAlreadyAliased)
            {
                return hasAlias
                    ? new SuggestedActionAvailability(false, false, true, targetClassName, namespaceValue, currentAlias)
                    : new SuggestedActionAvailability(true, false, false, targetClassName, namespaceValue, null);
            }
            else if (!hasAlias)
            {
                return new SuggestedActionAvailability(false, true, false, targetClassName, namespaceValue, null);
            }

            return default;
        }

        private bool HasAlias(out string alias)
        {

            var span = _textView.Caret.ContainingTextViewLine.Extent.GetText().Trim();
            var xmlReader = XmlReader.Create(new StringReader(span));
            try
            {
                xmlReader.Read();
            }
            catch
            {
                if (xmlReader.NodeType == XmlNodeType.Element && !string.IsNullOrEmpty(xmlReader.Prefix))
                {
                    alias = xmlReader.Prefix;
                    return true;
                }
            }
            alias = null;
            return false;
        }
    }
}
