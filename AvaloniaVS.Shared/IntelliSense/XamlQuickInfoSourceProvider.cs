using System.ComponentModel.Composition;
using AvaloniaVS.Models;
using AvaloniaVS.Shared.IntelliSense;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace AvaloniaVS.IntelliSense
{
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [ContentType("xml")]
    [Name("Avalonia XAML QuickInfo")]
    internal class XamlQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        [ImportingConstructor]
        public XamlQuickInfoSourceProvider([Import] CompletionEngineSource completionEngineSource)
        {
            _completionEngineSource = completionEngineSource;
        }

        private readonly CompletionEngineSource _completionEngineSource;

        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
        {
            if (textBuffer.Properties.ContainsProperty(typeof(XamlBufferMetadata)))
            {
                // One source per buffer - VS disposes it when the buffer/session is torn down.
                return textBuffer.Properties.GetOrCreateSingletonProperty(
                    typeof(XamlQuickInfoSource),
                    () => new XamlQuickInfoSource(textBuffer, _completionEngineSource));
            }

            return null;
        }
    }
}
