using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

namespace AvaloniaVS.Shared.Services
{
    /// <summary>
    /// Loads and indexes the XML documentation files (AssemblyName.xml) that sit alongside
    /// referenced assemblies, keyed by the standard "T:"/"P:"/"F:" doc-comment ID.
    /// </summary>
    /// <remarks>
    /// This exists because <see cref="Avalonia.Ide.CompletionEngine.Metadata"/> is built purely
    /// from dnlib reflection and carries no description text at all - completions and, until now,
    /// Quick Info could only show structural facts (type/get-set/attached), never the actual
    /// /// summary/// prose from the source. NuGet packages ship the .xml doc file beside the
    /// .dll in the same folder, and so does any project with
    /// &lt;GenerateDocumentationFile&gt;true&lt;/GenerateDocumentationFile&gt; set, so we don't
    /// need anything beyond the same assembly path list already used for completion metadata.
    /// </remarks>
    public sealed class XmlDocCache
    {
        private readonly Dictionary<string, string> _summaries = new(StringComparer.Ordinal);

        public static XmlDocCache Build(IEnumerable<string> assemblyPaths)
        {
            var cache = new XmlDocCache();

            foreach (var assemblyPath in assemblyPaths)
            {
                string docPath;
                try
                {
                    docPath = Path.ChangeExtension(assemblyPath, ".xml");
                }
                catch (ArgumentException)
                {
                    // Malformed path - skip it rather than lose the whole cache over one bad entry.
                    continue;
                }

                if (string.IsNullOrEmpty(docPath) || !File.Exists(docPath))
                {
                    continue;
                }

                try
                {
                    cache.LoadFile(docPath);
                }
                catch (XmlException)
                {
                    // Malformed/partial doc XML (has happened with third-party packages) -
                    // don't let one bad file take the whole feature down.
                }
                catch (IOException)
                {
                }
            }

            return cache;
        }

        private void LoadFile(string docPath)
        {
            using var reader = XmlReader.Create(docPath);

            while (reader.ReadToFollowing("member"))
            {
                var name = reader.GetAttribute("name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                using var memberReader = reader.ReadSubtree();
                if (memberReader.ReadToDescendant("summary"))
                {
                    var cleaned = CleanSummary(memberReader.ReadInnerXml());

                    // First assembly wins on a collision - shouldn't happen in practice since
                    // doc-comment IDs are namespace-qualified, but be defensive.
                    if (!string.IsNullOrEmpty(cleaned) && !_summaries.ContainsKey(name))
                    {
                        _summaries[name] = cleaned;
                    }
                }
            }
        }

        private static string CleanSummary(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            // Doc comments routinely contain <see cref="..."/>, <para> etc. and whatever
            // indentation the source had - flatten to one readable line for a tooltip.
            var text = Regex.Replace(raw, "<[^>]+>", "");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text;
        }

        public string TryGetTypeSummary(string typeFullName)
        {
            if (typeFullName == null)
            {
                return null;
            }

            return _summaries.TryGetValue($"T:{typeFullName}", out var summary) ? summary : null;
        }

        public string TryGetPropertySummary(string declaringTypeFullName, string propertyName)
        {
            if (declaringTypeFullName == null || propertyName == null)
            {
                return null;
            }

            if (_summaries.TryGetValue($"P:{declaringTypeFullName}.{propertyName}", out var summary))
            {
                return summary;
            }

            // Attached properties are conventionally documented on their static backing field,
            // e.g. "public static readonly AttachedProperty<int> RowProperty = ..." - this is
            // exactly the shape the "attachedAvaloniaProperty" snippet generates.
            if (_summaries.TryGetValue($"F:{declaringTypeFullName}.{propertyName}Property", out summary))
            {
                return summary;
            }

            return null;
        }

        public string TryGetFieldSummary(string declaringTypeFullName, string propertyName)
        {
            if (declaringTypeFullName == null || propertyName == null)
            {
                return null;
            }

            if (_summaries.TryGetValue($"F:{declaringTypeFullName}.{propertyName}", out var summary))
            {
                return summary;
            }

            if (_summaries.TryGetValue($"F:{declaringTypeFullName}.{propertyName}Property", out summary))
            {
                return summary;
            }

            return null;
        }

        public string TryGetEventSummary(string declaringTypeFullName, string propertyName)
        {
            if (declaringTypeFullName == null || propertyName == null)
            {
                return null;
            }

            if (_summaries.TryGetValue($"E:{declaringTypeFullName}.{propertyName}", out var summary))
            {
                return summary;
            }

            if (_summaries.TryGetValue($"F:{declaringTypeFullName}.{propertyName}Property", out summary))
            {
                return summary;
            }

            return null;
        }
    }
}
