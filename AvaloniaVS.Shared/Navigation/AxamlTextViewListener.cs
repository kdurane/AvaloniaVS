using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace AvaloniaVS.Shared.Navigation
{
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("xml")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class AxamlTextViewListener : IVsTextViewCreationListener
    {
        [Import]
        internal IVsEditorAdaptersFactoryService EditorAdaptersFactory { get; set; }

        [Import]
        internal IXamlTypeResolutionService TypeResolutionService { get; set; }

        [Import]
        internal SVsServiceProvider ServiceProvider { get; set; }

        [Import]
        internal VisualStudioWorkspace Workspace { get; set; }

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            var view = EditorAdaptersFactory.GetWpfTextView(textViewAdapter);
            var commandFilter = new AxamlGoToDefinitionCommandFilter(view, TypeResolutionService, ServiceProvider, Workspace);
            textViewAdapter.AddCommandFilter(commandFilter, out var next);
            commandFilter.Next = next;
        }
    }
}
