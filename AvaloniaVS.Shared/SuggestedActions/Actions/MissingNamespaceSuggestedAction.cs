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
    internal class MissingNamespaceSuggestedAction(ITrackingSpan span, IWpfDifferenceViewerFactoryService diffFactory, IDifferenceBufferFactoryService diffBufferFactory,
        ITextBufferFactoryService bufferFactory, ITextEditorFactoryService textEditorFactoryService,
        string namespaceValue, Dictionary<string, string> aliases, string alias) : BaseSuggestedAction, ISuggestedAction
    {
        private readonly ITrackingSpan _span = span;
        private readonly string _namespaceValue = namespaceValue;
        private readonly IWpfDifferenceViewerFactoryService _diffFactory = diffFactory;
        private readonly IDifferenceBufferFactoryService _diffBufferFactory = diffBufferFactory;
        private readonly ITextBufferFactoryService _bufferFactory = bufferFactory;
        private readonly Dictionary<string, string> _aliases = aliases;
        private readonly string _alias = alias;
        private readonly ITextViewRoleSet _previewRoleSet = textEditorFactoryService.CreateTextViewRoleSet(PredefinedTextViewRoles.Analyzable);

        public string DisplayText { get; } = $"Add xmlns {alias}";

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
            var insertionPoint = lastNs is not null
                ? buffer.CurrentSnapshot.GetText().IndexOf(lastNs) + lastNs.Length + 2
                : buffer.CurrentSnapshot.GetText().IndexOf('>');

            buffer.Insert(insertionPoint, $"xmlns:{_alias}=\"{_namespaceValue}\"");
        }
    }
}
