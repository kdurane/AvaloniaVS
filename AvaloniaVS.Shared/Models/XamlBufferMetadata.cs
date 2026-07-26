using System.Collections.Generic;
using AvaloniaVS.Shared.Services;
using CompletionMetadata = Avalonia.Ide.CompletionEngine.Metadata;

namespace AvaloniaVS.Models
{
    internal class XamlBufferMetadata
    {
        public CompletionMetadata CompletionMetadata { get; set; }
        public XmlDocCache DocCache { get; set; }
        public IReadOnlyList<string> AssemblyPaths { get; set; }
        public bool NeedInvalidation { get; set; } = true;
    }
}
