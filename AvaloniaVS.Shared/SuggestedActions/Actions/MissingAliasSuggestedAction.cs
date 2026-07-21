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
    internal class MissingAliasSuggestedAction : BaseSuggestedAction, ISuggestedAction
    {
        private readonly ITrackingSpan _span;
        private readonly ITextSnapshot _snapshot;
        private readonly string _targetClassName;
        private readonly string _namespaceAlias;
        private readonly IWpfDifferenceViewerFactoryService _diffFactory;
        private readonly IDifferenceBufferFactoryService _diffBufferFactory;
        private readonly ITextBufferFactoryService _bufferFactory;
        private readonly ITextViewRoleSet _previewRoleSet;

        public MissingAliasSuggestedAction(ITrackingSpan span, IWpfDifferenceViewerFactoryService diffFactory, IDifferenceBufferFactoryService diffBufferFactory,
            ITextBufferFactoryService bufferFactory, ITextEditorFactoryService textEditorFactoryService,
            string targetClassName, string namespaceValue)
        {
            _span = span;
            _snapshot = _span.TextBuffer.CurrentSnapshot;
            _targetClassName = targetClassName;
            _namespaceAlias = namespaceValue.Split(':').Last().Split('.').Last().Split('/').Last();
            _diffFactory = diffFactory;
            _diffBufferFactory = diffBufferFactory;
            _bufferFactory = bufferFactory;
            _previewRoleSet = textEditorFactoryService.CreateTextViewRoleSet(PredefinedTextViewRoles.Analyzable);
            DisplayText = $"Use {_namespaceAlias.ToLower()} ({namespaceValue})";
        }

        public string DisplayText { get; }

        public Task<object> GetPreviewAsync(CancellationToken cancellationToken)
            => Task.FromResult<object>(PreviewProvider.GetPreview(_bufferFactory, _span, _diffBufferFactory, _diffFactory, _previewRoleSet, ApplySuggestion));

        private void ApplySuggestion(ITextBuffer buffer)
            => buffer.Replace(_span.GetSpan(_snapshot), $"{_namespaceAlias.ToLower()}:{_targetClassName}");

        public void Invoke(CancellationToken cancellationToken) => ApplySuggestion(_span.TextBuffer);
    }
}
