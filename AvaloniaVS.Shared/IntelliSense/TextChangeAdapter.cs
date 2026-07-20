using Microsoft.VisualStudio.Text;
using AvaloniaTextChange = Avalonia.Ide.CompletionEngine.ITextChange;

namespace AvaloniaVS.IntelliSense
{
    public class TextChangeAdapter(ITextChange textChange) : AvaloniaTextChange
    {
        /// <inheritdoc/>
        public int NewPosition => textChange.NewPosition;

        /// <inheritdoc/>
        public string NewText => textChange.NewText;

        /// <inheritdoc/>
        public int OldPosition => textChange.OldPosition;

        /// <inheritdoc/>
        public string OldText => textChange.OldText;
    }
}
