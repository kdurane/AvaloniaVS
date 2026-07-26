using System.ComponentModel.Composition;
using AvaloniaVS.Models;
using AvaloniaVS.Shared.IntelliSense;
using Microsoft.VisualStudio.Text;

namespace AvaloniaVS.Shared.Navigation
{
    /// <summary>
    /// Resolves a prefixed XAML name (e.g. "vm:MainViewModel") to a CLR type by scanning
    /// the buffer for the matching xmlns declaration. If you already have equivalent logic
    /// in the namespace-suggestion feature, prefer wiring into that instead of maintaining
    /// two copies - this exists as a self-contained fallback/starting point.
    /// </summary>
    [Export(typeof(IXamlTypeResolutionService))]
    [method: ImportingConstructor]
    internal sealed class XamlTypeResolutionService(CompletionEngineSource completionEngineSource) : IXamlTypeResolutionService
    {
        public XamlTypeReference ResolveTypeName(ITextBuffer buffer, SnapshotPoint point, string qualifiedName)
        {
            if (string.IsNullOrEmpty(qualifiedName))
                return null;

            if (!buffer.Properties.TryGetProperty<XamlBufferMetadata>(typeof(XamlBufferMetadata), out var metadata) ||
                metadata.CompletionMetadata == null)
                return null;

            buffer.Properties.TryGetProperty("AssemblyName", out string assemblyName);

            var text = buffer.CurrentSnapshot.GetText();
            var helper = completionEngineSource.CompletionEngine.Helper;
            helper.SetMetadata(metadata.CompletionMetadata, text, assemblyName);

            var type = helper.LookupType(qualifiedName);
            if (type != null)
                return new XamlTypeReference(type.FullName, metadataType: type, assemblyPaths: metadata.AssemblyPaths, docCache: metadata.DocCache);
            return type == null ? null : new XamlTypeReference(type.FullName);
        }
    }
}
