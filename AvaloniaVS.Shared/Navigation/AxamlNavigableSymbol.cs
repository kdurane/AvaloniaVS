using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Ide.CompletionEngine;
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
        private readonly IServiceProvider _serviceProvider = serviceProvider;
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
                var sourceLocation = symbol?.Locations.FirstOrDefault(l => l.IsInSource);

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (sourceLocation != null)
                {
                    RoslynSymbolNavigator.NavigateToLocation(_serviceProvider, sourceLocation);
                    return;
                }

                if (_typeReference.AssemblyPaths == null)
                {
                    RoslynSymbolNavigator.ShowStatusBarMessage(_serviceProvider, $"No source available for '{_typeReference.FullyQualifiedTypeName}'.");
                    return;
                }

                RoslynSymbolNavigator.ShowStatusBarMessage(_serviceProvider, $"Generating definition for {ShortName(_typeReference.FullyQualifiedTypeName)}...");

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var stub = await Task.Run(() =>
                    DnlibStubGenerator.GenerateStub(_typeReference.FullyQualifiedTypeName, _typeReference.AssemblyPaths, _typeReference.DocCache));

                if (stub != null && DnlibStubGenerator.NavigateToGeneratedStub(_serviceProvider, stub, _typeReference.FullyQualifiedTypeName))
                {
                    RoslynSymbolNavigator.ClearStatusBarMessage(_serviceProvider);
                    return;
                }

                RoslynSymbolNavigator.ShowStatusBarMessage(_serviceProvider, $"No source available for '{_typeReference.FullyQualifiedTypeName}'.");
            });
        }


        private static string ShortName(string fullyQualifiedName) => fullyQualifiedName[(fullyQualifiedName.LastIndexOf('.') + 1)..];


    }
}
