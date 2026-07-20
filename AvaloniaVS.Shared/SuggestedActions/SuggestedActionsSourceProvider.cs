using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Differencing;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;

namespace AvaloniaVS.Shared.SuggestedActions
{
    [Export(typeof(ISuggestedActionsSourceProvider))]
    [Name("SuggestedActionsSourceProvider")]
    [ContentType("xml")]
    [method: ImportingConstructor]
    internal class SuggestedActionsSourceProvider([Import] IWpfDifferenceViewerFactoryService _diffFactory, [Import] IDifferenceBufferFactoryService _diffBufferFactory,
        [Import] ITextBufferFactoryService _bufferFactory, [Import] ITextEditorFactoryService _textEditorFactoryService) : ISuggestedActionsSourceProvider
    {
        [Import(typeof(ITextStructureNavigatorSelectorService))]
        internal ITextStructureNavigatorSelectorService NavigatorService { get; set; }

        public ISuggestedActionsSource CreateSuggestedActionsSource(ITextView textView, ITextBuffer textBuffer)
        {
            if (textBuffer == null && textView == null)
            {
                return null;
            }
            return new SuggestedActionsSource(this, textView, textBuffer, _diffFactory, _diffBufferFactory,
                _bufferFactory, _textEditorFactoryService);
        }
    }
}
