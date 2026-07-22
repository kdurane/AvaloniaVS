using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace AvaloniaVS.Shared.Navigation
{
    internal sealed class AxamlGoToDefinitionCommandFilter(IWpfTextView view, IXamlTypeResolutionService typeResolutionService,
    System.IServiceProvider serviceProvider, VisualStudioWorkspace workspace) : IOleCommandTarget
    {
        private IOleCommandTarget _next;

        public IOleCommandTarget Next
        {
            get => _next;
            set => _next = value;
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (cCmds == 1 &&
                pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97 &&
                prgCmds[0].cmdID == (uint)VSConstants.VSStd97CmdID.GotoDefn)
            {
                prgCmds[0].cmdf =
                    (uint)(OLECMDF.OLECMDF_SUPPORTED |
                           OLECMDF.OLECMDF_ENABLED);

                return VSConstants.S_OK;
            }

            return _next?.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText) ?? VSConstants.OLE_E_CANTCONVERT;
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97 &&
                nCmdID == (uint)VSConstants.VSStd97CmdID.GotoDefn)
            {
                var caret = view.Caret.Position.BufferPosition;
                var span = new SnapshotSpan(
                    caret.Snapshot,
                    caret.Position,
                    0);

                Navigate(span);

                return VSConstants.S_OK;
            }

            return _next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
        }

        private async void Navigate(SnapshotSpan span)
        {
            var target = AxamlSymbolSpanResolver.TryResolve(span);
            if (target == null)
                return;

            if (target.Kind == AxamlSymbolKind.BindingMember)
            {
                await NavigateToBindingMemberAsync(target, span.Start);
                return;
            }

            var typeReference = typeResolutionService.ResolveTypeName(
                view.TextBuffer,
                target.Span.Start,
                target.QualifiedName);

            if (typeReference == null)
                return;

            var symbol = new AxamlNavigableSymbol(target.Span, typeReference, serviceProvider, workspace);
            symbol.Navigate(PredefinedNavigableRelationships.Definition);
        }

        private async Task NavigateToBindingMemberAsync(AxamlSymbolTarget target, SnapshotPoint caretPoint)
        {
            string dataTypeName = AxamlSymbolSpanResolver.FindEnclosingDataTypeName(caretPoint);
            if (dataTypeName == null)
                return; // no x:DataType in scope - nothing to resolve the binding against

            var typeReference = typeResolutionService.ResolveTypeName(view.TextBuffer, caretPoint, dataTypeName);
            if (typeReference == null)
                return;

            var pathSegments = target.QualifiedName.Split('.');

            await ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
             {
                 var location = await Task.Run(() =>
                     RoslynSymbolNavigator.FindMemberLocationAsync(
                         workspace, typeReference.FullyQualifiedTypeName, pathSegments, CancellationToken.None));

                 if (location == null)
                     return;

                 await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                 RoslynSymbolNavigator.NavigateToLocation(serviceProvider, location);
             });
        }
    }
}
