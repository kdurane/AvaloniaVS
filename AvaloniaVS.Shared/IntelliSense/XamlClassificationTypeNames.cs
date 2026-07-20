using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace AvaloniaVS.Shared.IntelliSense
{
    internal static class XamlClassificationTypeNames
    {
        public const string MarkupExtension = "Xaml.MarkupExtension";
        public const string NamespacePrefix = "Xaml.NamespacePrefix";
    }

    internal static class XamlClassificationTypeDefinitions
    {
#pragma warning disable CS0649
        [Export(typeof(ClassificationTypeDefinition))]
        [Name(XamlClassificationTypeNames.MarkupExtension)]
        internal static ClassificationTypeDefinition _markupExtensionDefinition;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(XamlClassificationTypeNames.NamespacePrefix)]
        internal static ClassificationTypeDefinition _namespacePrefixDefinition;
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
    [ClassificationType(ClassificationTypeNames = XamlClassificationTypeNames.NamespacePrefix)]
    [Name(XamlClassificationTypeNames.NamespacePrefix)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class XamlNamespacePrefixFormat : ClassificationFormatDefinition
    {
        public XamlNamespacePrefixFormat()
        {
            DisplayName = "XAML Namespace Prefix";
            ForegroundColor = Color.FromRgb(0x4E, 0xC9, 0xB0);
        }
    }
}
