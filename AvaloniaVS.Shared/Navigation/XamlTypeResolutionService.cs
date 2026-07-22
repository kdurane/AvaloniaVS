using System.ComponentModel.Composition;
using System.Text.RegularExpressions;
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
    internal sealed class XamlTypeResolutionService : IXamlTypeResolutionService
    {
        // xmlns:prefix="value"  OR  xmlns="value" (prefix group absent)
        private static readonly Regex s_xmlnsDeclaration = new(
            @"xmlns(?::(?<prefix>\w+))?\s*=\s*""(?<value>[^""]+)""",
            RegexOptions.Compiled);

        public XamlTypeReference ResolveTypeName(ITextBuffer buffer, SnapshotPoint point, string qualifiedName)
        {
            if (string.IsNullOrEmpty(qualifiedName))
                return null;

            string prefix = null;
            string localName = qualifiedName;

            int colonIndex = qualifiedName.IndexOf(':');
            if (colonIndex >= 0)
            {
                prefix = qualifiedName.Substring(0, colonIndex);
                localName = qualifiedName.Substring(colonIndex + 1);
            }

            // xmlns is typically declared once, near the top of the file - scanning the
            // whole buffer text is simplest and cheap enough for a single navigation call.
            // If you need proper per-scope xmlns handling (redeclared deeper in the tree),
            // this is the spot to extend - walk from `point` upward through ancestor
            // elements instead of scanning the whole snapshot.
            string bufferText = buffer.CurrentSnapshot.GetText();

            foreach (Match match in s_xmlnsDeclaration.Matches(bufferText))
            {
                string declaredPrefix = match.Groups["prefix"].Success ? match.Groups["prefix"].Value : null;
                if (declaredPrefix != prefix)
                    continue;

                string value = match.Groups["value"].Value;
                return BuildTypeReference(value, localName);
            }

            return null;
        }

        private static XamlTypeReference BuildTypeReference(string xmlnsValue, string localName)
        {
            const string clrNamespacePrefix = "clr-namespace:";
            const string usingPrefix = "using:";

            string assembly = null;
            string ns;

            if (xmlnsValue.StartsWith(clrNamespacePrefix))
            {
                string remainder = xmlnsValue.Substring(clrNamespacePrefix.Length);
                int assemblyIndex = remainder.IndexOf(";assembly=");
                if (assemblyIndex >= 0)
                {
                    ns = remainder.Substring(0, assemblyIndex);
                    assembly = remainder.Substring(assemblyIndex + ";assembly=".Length);
                }
                else
                {
                    ns = remainder; // same-assembly form: clr-namespace:MyApp.ViewModels
                }
            }
            else if (xmlnsValue.StartsWith(usingPrefix))
            {
                ns = xmlnsValue.Substring(usingPrefix.Length); // Avalonia shorthand, assembly = current project
            }
            else if (xmlnsValue == "https://github.com/avaloniaui")
            {
                ns = "Avalonia.Controls";
            }
            else
            {
                return null;
            }

            return new XamlTypeReference($"{ns}.{localName}", assembly);
        }
    }

}
