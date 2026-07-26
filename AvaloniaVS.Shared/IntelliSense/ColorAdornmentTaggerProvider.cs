using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace AvaloniaVS.Shared.IntelliSense
{
    [Export(typeof(IViewTaggerProvider))]
    [ContentType("xml")]
    [TagType(typeof(IntraTextAdornmentTag))]
    [method: ImportingConstructor]
    internal sealed class ColorAdornmentTaggerProvider(IClassificationTypeRegistryService classificationRegistry) : IViewTaggerProvider
    {
        public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
            if (textView.TextBuffer != buffer)
                return null;

            if (!buffer.Properties.TryGetProperty<XamlClassificationTagger>(typeof(XamlClassificationTagger), out var classificationTagger))
            {
                classificationTagger = new XamlClassificationTagger(buffer, classificationRegistry);
                buffer.Properties.AddProperty(typeof(XamlClassificationTagger), classificationTagger);
            }

            if (buffer.Properties.TryGetProperty<ColorAdornmentTagger>(typeof(ColorAdornmentTagger), out var existing))
                return (ITagger<T>)(object)existing;

            var tagger = new ColorAdornmentTagger(classificationTagger);
            buffer.Properties.AddProperty(typeof(ColorAdornmentTagger), tagger);
            return (ITagger<T>)(object)tagger;
        }
    }
}
