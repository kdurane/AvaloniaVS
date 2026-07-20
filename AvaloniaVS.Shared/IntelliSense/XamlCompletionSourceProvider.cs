using System.ComponentModel.Composition;
using AvaloniaVS.Models;
using AvaloniaVS.Shared.IntelliSense;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace AvaloniaVS.IntelliSense
{
    [Export(typeof(ICompletionSourceProvider))]
    [ContentType("xml")]
    [Name("Avalonia XAML Completion")]
    [method: ImportingConstructor]
    internal class XamlCompletionSourceProvider([Import] CompletionEngineSource completionEngineSource) : ICompletionSourceProvider
    {
        public ICompletionSource TryCreateCompletionSource(ITextBuffer textBuffer)
        {
            if (textBuffer.Properties.ContainsProperty(typeof(XamlBufferMetadata)))
            {
                return new XamlCompletionSource(textBuffer, completionEngineSource);
            }

            return null;
        }
    }
}
