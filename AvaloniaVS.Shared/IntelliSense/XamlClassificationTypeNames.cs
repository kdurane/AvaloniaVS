using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace AvaloniaVS.Shared.IntelliSense
{
    internal static class XamlClassificationTypeNames
    {
        public const string MarkupExtension = "Xaml.MarkupExtension";
        public const string ValueText = "Xaml.ValueText";
    }

    internal static class XamlClassificationTypeDefinitions
    {
#pragma warning disable CS0649
        [Export(typeof(ClassificationTypeDefinition))]
        [Name(XamlClassificationTypeNames.MarkupExtension)]
        internal static ClassificationTypeDefinition MarkupExtensionDefinition;


        [Export(typeof(ClassificationTypeDefinition))]
        [Name(XamlClassificationTypeNames.ValueText)]
        internal static ClassificationTypeDefinition ValueTextDefinition;
#pragma warning restore CS0649
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = XamlClassificationTypeNames.MarkupExtension)]
    [Name(XamlClassificationTypeNames.MarkupExtension)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class XamlMarkupExtensionFormat : ClassificationFormatDefinition
    {
        public XamlMarkupExtensionFormat()
        {
            DisplayName = "XAML Markup Extension";
            ForegroundColor = Color.FromRgb(247, 246, 173);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = XamlClassificationTypeNames.ValueText)]
    [Name(XamlClassificationTypeNames.ValueText)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class XamlValueTextFormat : ClassificationFormatDefinition
    {
        public XamlValueTextFormat()
        {
            DisplayName = "XAML Value Text";
            ForegroundColor = Color.FromRgb(0xFF, 0xFF, 0xFF);
        }
    }
}
