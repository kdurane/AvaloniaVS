using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaVS.Shared.SuggestedActions.Actions.Base;
using AvaloniaVS.Shared.SuggestedActions.Helpers;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Differencing;
using Microsoft.VisualStudio.Text.Editor;

namespace AvaloniaVS.Shared.SuggestedActions.Actions
{
    internal class MissingNamespaceAndAliasSuggestedAction : BaseSuggestedAction, ISuggestedAction
    {
        private readonly ITrackingSpan _span;
        private readonly ITextSnapshot _snapshot;
        private readonly string _targetClassName;
        private readonly string _namespaceValue;
        private readonly string _namespaceAlias;
        private readonly IWpfDifferenceViewerFactoryService _diffFactory;
        private readonly IDifferenceBufferFactoryService _diffBufferFactory;
        private readonly ITextBufferFactoryService _bufferFactory;
        private readonly Dictionary<string, string> _aliases;
        private readonly ITextViewRoleSet _previewRoleSet;

        public MissingNamespaceAndAliasSuggestedAction(ITrackingSpan span, IWpfDifferenceViewerFactoryService diffFactory,
            IDifferenceBufferFactoryService diffBufferFactory, ITextBufferFactoryService bufferFactory, ITextEditorFactoryService textEditorFactoryService,
            string targetClassName, string namespaceValue, Dictionary<string, string> aliases)
        {
            _span = span;
            _snapshot = _span.TextBuffer.CurrentSnapshot;
            _targetClassName = targetClassName;
            _namespaceValue = namespaceValue;
            _namespaceAlias = namespaceValue.Split(':').Last().Split('.').Last().Split('/').Last();
            DisplayText = $"Add xmlns {_namespaceAlias}";
            _diffFactory = diffFactory;
            _diffBufferFactory = diffBufferFactory;
            _bufferFactory = bufferFactory;
            _aliases = aliases;
            _previewRoleSet = textEditorFactoryService.CreateTextViewRoleSet(PredefinedTextViewRoles.Analyzable);
        }

        public string DisplayText { get; }

        public Task<object> GetPreviewAsync(CancellationToken cancellationToken)
            => Task.FromResult<object>(PreviewProvider.GetPreview(_bufferFactory, _span, _diffBufferFactory, _diffFactory, _previewRoleSet, ApplySuggestion));

        public void Invoke(CancellationToken cancellationToken)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                ApplySuggestion(_span.TextBuffer);
            }
        }

        private void ApplySuggestion(ITextBuffer buffer)
        {
            var lastNs = _aliases.LastOrDefault().Value;
            buffer.Replace(_span.GetSpan(_snapshot), $"{_namespaceAlias.ToLower()}:{_targetClassName}");

            var insertionPoint = lastNs is not null
                ? buffer.CurrentSnapshot.GetText().IndexOf(lastNs) + lastNs.Length + 2
                : buffer.CurrentSnapshot.GetText().IndexOf('>'); // fallback: no existing aliases to anchor on

            buffer.Insert(insertionPoint, $"xmlns:{_namespaceAlias.ToLower()}=\"{_namespaceValue}\"");
        }
    }
}
