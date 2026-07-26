using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace AvaloniaVS.Shared.Navigation
{
    internal static class RoslynSymbolNavigator
    {
        public static async Task<ISymbol> FindSymbolAsync(VisualStudioWorkspace workspace, string fullyQualifiedTypeName, CancellationToken cancellationToken)
        {
            ISymbol metadataOnlyFallback = null;

            foreach (var project in workspace.CurrentSolution.Projects)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Compilation compilation;
                try
                { compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch { continue; }

                var symbol = compilation?.GetTypeByMetadataName(fullyQualifiedTypeName);
                if (symbol == null)
                    continue;

                if (symbol.Locations.Any())
                    return symbol;

                metadataOnlyFallback ??= symbol;
            }

            return metadataOnlyFallback;
        }

        public static bool NavigateToLocation(IServiceProvider serviceProvider, Location location)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var lineSpan = location.GetLineSpan();
            string filePath = lineSpan.Path;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            int line = lineSpan.StartLinePosition.Line;
            int column = lineSpan.StartLinePosition.Character;

            int hr = VsShellUtilities.TryOpenDocument(
                serviceProvider,
                filePath,
                VSConstants.LOGVIEWID.TextView_guid,
                out _,
                out _,
                out var frame);

            if (ErrorHandler.Failed(hr) || frame == null)
                return false;

            var vsTextView = VsShellUtilities.GetTextView(frame);
            vsTextView.SetCaretPos(line, column);
            vsTextView.CenterLines(line, 1);
            frame?.Show();
            return true;
        }

        public static bool NavigateToSymbol(IServiceProvider serviceProvider, ISymbol symbol)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var sourceLocation = symbol?.Locations.FirstOrDefault(l => l.IsInSource);
            if (sourceLocation == null)
            {
                if (symbol != null)
                    ShowStatusBarMessage(serviceProvider, $"No source available for '{symbol.Name}' - it's defined in a referenced assembly.");
                return false;
            }

            return NavigateToLocation(serviceProvider, sourceLocation);
        }

        public static void ShowStatusBarMessage(IServiceProvider serviceProvider, string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (serviceProvider.GetService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
            {
                statusBar.IsFrozen(out int frozen);
                if (frozen == 0)
                    statusBar.SetText(message);
            }
        }

        public static async Task<Location> FindMemberLocationAsync(
            VisualStudioWorkspace workspace, string rootTypeName, IReadOnlyList<string> pathSegments, CancellationToken cancellationToken)
        {
            if (pathSegments.Count == 0)
                return null;

            foreach (var project in workspace.CurrentSolution.Projects)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Compilation compilation;
                try
                { compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false); }
                catch { continue; }

                var currentType = compilation?.GetTypeByMetadataName(rootTypeName);
                if (currentType == null)
                    continue;

                ISymbol member = null;
                for (int i = 0; i < pathSegments.Count; i++)
                {
                    member = FindMember(currentType, pathSegments[i]);
                    if (member == null)
                        break;

                    if (i < pathSegments.Count - 1)
                    {
                        if (GetMemberType(member) is not INamedTypeSymbol next)
                        { member = null; break; }
                        currentType = next;
                    }
                }

                if (member == null)
                    continue;

                var sourceLocation = member.Locations.FirstOrDefault(l => l.IsInSource);
                if (sourceLocation == null)
                    continue;

                if (IsGeneratedLocation(sourceLocation) && member is IPropertySymbol generatedProperty)
                {
                    // [ObservableProperty] members are declared in the .g.cs file the generator
                    // produces - jump to the backing field that actually triggers it instead.
                    var backingField = FindObservablePropertyBackingField(generatedProperty);
                    var fieldLocation = backingField?.Locations.FirstOrDefault(l => l.IsInSource && !IsGeneratedLocation(l));
                    if (fieldLocation != null)
                        return fieldLocation;
                }

                return sourceLocation; // real source, or generated with no resolvable field - better than nothing
            }

            return null;
        }

        private static bool IsGeneratedLocation(Location location)
        {
            string path = location.SourceTree?.FilePath;
            return !string.IsNullOrEmpty(path) &&
                   (path.IndexOf(".g.cs", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("SourceGenerators", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static IFieldSymbol FindObservablePropertyBackingField(IPropertySymbol property)
        {
            foreach (var field in property.ContainingType.GetMembers().OfType<IFieldSymbol>())
            {
                bool hasAttribute = field.GetAttributes()
                    .Any(a => a.AttributeClass?.Name == "ObservablePropertyAttribute");
                if (!hasAttribute)
                    continue;

                if (string.Equals(ExpectedPropertyName(field.Name), property.Name, StringComparison.Ordinal))
                    return field;
            }
            return null;
        }

        private static string ExpectedPropertyName(string fieldName)
        {
            string trimmed = fieldName.TrimStart('_');
            if (trimmed.Length == 0)
                return trimmed;
            return char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
        }

        private static ISymbol FindMember(INamedTypeSymbol type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var member = t.GetMembers(name).FirstOrDefault(m => m is IPropertySymbol or IFieldSymbol);
                if (member != null)
                    return member;
            }
            return null;
        }

        private static ITypeSymbol GetMemberType(ISymbol member) => member switch
        {
            IPropertySymbol p => p.Type,
            IFieldSymbol f => f.Type,
            _ => null
        };

        public static void ClearStatusBarMessage(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (serviceProvider.GetService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
                statusBar.Clear();
        }
    }
}
