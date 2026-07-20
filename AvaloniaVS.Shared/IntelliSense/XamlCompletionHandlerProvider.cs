using System;
using System.ComponentModel.Composition;
using AvaloniaVS.Models;
using AvaloniaVS.Shared.IntelliSense;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace AvaloniaVS.IntelliSense
{
    /// <summary>
    /// Registers a <see cref="XamlCompletionCommandHandler"/> with newly-created text views.
    /// </summary>
    [Export(typeof(IVsTextViewCreationListener))]
    [Name("Avalonia XAML completion handler")]
    [ContentType("xml")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [method: ImportingConstructor]
    internal class XamlCompletionHandlerProvider(
        [Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider,
        IVsEditorAdaptersFactoryService adapterService,
        ICompletionBroker completionBroker,
        ITextUndoHistoryRegistry textUndoHistoryRegistry,
        CompletionEngineSource completionEngineSource) : IVsTextViewCreationListener
    {
        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            var textView = adapterService.GetWpfTextView(textViewAdapter);

            // If the buffer contains Avalonia XAML, register a completion handler on it.
            if (textView.TextBuffer.Properties.ContainsProperty(typeof(XamlBufferMetadata)))
            {
                textView.Properties.GetOrCreateSingletonProperty(
                    () => new XamlCompletionCommandHandler(
                        serviceProvider,
                        completionBroker,
                        textView,
                        textViewAdapter,
                        completionEngineSource.CompletionEngine));

                textView.Properties.GetOrCreateSingletonProperty(
                    () => new XamlPasteCommandHandler(
                        serviceProvider,
                        textView,
                        textViewAdapter,
                        textUndoHistoryRegistry,
                        completionEngineSource.CompletionEngine));
            }
        }
    }
}
