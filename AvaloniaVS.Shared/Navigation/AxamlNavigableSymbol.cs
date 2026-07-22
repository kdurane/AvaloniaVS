using System.Collections.Generic;
using System.Threading;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;

namespace AvaloniaVS.Shared.Navigation
{
    internal sealed class AxamlNavigableSymbol(SnapshotSpan symbolSpan, XamlTypeReference typeReference, System.IServiceProvider serviceProvider,
        VisualStudioWorkspace workspace) : INavigableSymbol
    {
        private readonly XamlTypeReference _typeReference = typeReference;
        private readonly System.IServiceProvider _serviceProvider = serviceProvider;
        private readonly VisualStudioWorkspace _workspace = workspace;

        public SnapshotSpan SymbolSpan { get; } = symbolSpan;

        public IEnumerable<INavigableRelationship> Relationships { get; } = [PredefinedNavigableRelationships.Definition];

        // Called on the UI thread by VS F12. Do the Roslyn lookup on a
        // background thread first, then hop back before touching any shell/editor API -
        // this is the same UI-thread-hop shape as the CreateCompletionMetadataAsync fix.
        public void Navigate(INavigableRelationship relationship)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                var symbol = await RoslynSymbolNavigator.FindSymbolAsync(_workspace, _typeReference.FullyQualifiedTypeName, CancellationToken.None);
                if (symbol == null)
                    return;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                RoslynSymbolNavigator.NavigateToSymbol(_serviceProvider, symbol);
            });
        }
    }
}
