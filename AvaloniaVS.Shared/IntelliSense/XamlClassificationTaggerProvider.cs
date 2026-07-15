using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace AvaloniaVS.Shared.IntelliSense
{
    [Export(typeof(ITaggerProvider))]
    [ContentType("xml")]
    [TagType(typeof(ClassificationTag))]
    [method: ImportingConstructor]
    internal sealed class XamlClassificationTaggerProvider(
    IClassificationTypeRegistryService classificationRegistry) : ITaggerProvider
    {
        private readonly IClassificationTypeRegistryService _classificationRegistry = classificationRegistry;

        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            if (buffer.Properties.TryGetProperty<XamlClassificationTagger>(
                typeof(XamlClassificationTagger),
                out var existing))
            {
                return (ITagger<T>)(object)existing;
            }

            var tagger = new XamlClassificationTagger(buffer, _classificationRegistry);
            buffer.Properties.AddProperty(typeof(XamlClassificationTagger), tagger);

            return (ITagger<T>)(object)tagger;
        }
    }
}
