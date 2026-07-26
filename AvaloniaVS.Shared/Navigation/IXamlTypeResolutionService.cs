using System.Collections.Generic;
using Avalonia.Ide.CompletionEngine;
using AvaloniaVS.Shared.Services;
using Microsoft.VisualStudio.Text;

namespace AvaloniaVS.Shared.Navigation
{
    /// <summary>
    /// Abstraction over the xmlns -> CLR type resolution you already built for the
    /// namespace-completion feature. Wire your existing lookup in behind this interface
    /// so both features share one source of truth for "what does this prefixed name
    /// actually mean" - you don't want two competing xmlns resolvers drifting apart.
    /// </summary>
    public interface IXamlTypeResolutionService
    {
        /// <param name="buffer">The AXAML buffer, used to read the xmlns declarations in scope.</param>
        /// <param name="point">Where in the buffer the reference occurs (xmlns scoping can vary by element).</param>
        /// <param name="qualifiedName">e.g. "local:MyControl" or "vm:MainViewModel" (no prefix = default xmlns).</param>
        /// <returns>Null if the prefix/name can't be resolved against any xmlns in scope.</returns>
        XamlTypeReference ResolveTypeName(ITextBuffer buffer, SnapshotPoint point, string qualifiedName);
    }

    /// <summary>
    /// Result of resolving a XAML-qualified name to a CLR type.
    /// </summary>
    public sealed class XamlTypeReference(string fullyQualifiedTypeName, string assemblyName = null,
    MetadataType metadataType = null, IReadOnlyList<string> assemblyPaths = null, XmlDocCache docCache = null)
    {
        /// <summary>
        /// Fully-qualified CLR name in Roslyn "metadata name" form, e.g.
        /// "MyApp.ViewModels.MainViewModel". Nested types use '+' (Outer+Inner),
        /// generics use the `N arity suffix - match whatever your existing
        /// namespace-suggestion resolver already produces.
        /// </summary>
        public string FullyQualifiedTypeName { get; } = fullyQualifiedTypeName;

        /// <summary>
        /// Assembly the type lives in, if known. Can be left null - the navigator
        /// searches every project in the solution regardless, since a project's
        /// own assembly name isn't always the interesting bit for a source-jump.
        /// </summary>
        public string AssemblyName { get; } = assemblyName;
        public MetadataType MetadataType { get; } = metadataType;
        public IReadOnlyList<string> AssemblyPaths { get; } = assemblyPaths;
        public XmlDocCache DocCache { get; } = docCache;
    }

}
