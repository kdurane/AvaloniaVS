using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using dnlib.DotNet;

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

        public string TryGetMethodSummary(string declaringTypeFullName, MethodDef method)
        {
            if (declaringTypeFullName == null || method == null)
            {
                return null;
            }

            string docId;
            try
            {
                docId = BuildMethodDocId(declaringTypeFullName, method);
            }
            catch
            {
                return null;
            }

            if (docId == null || !_summaries.TryGetValue(docId, out var baseSummary))
            {
                return null;
            }

            // Parameter/return docs are stored separately in the raw XML today (only <summary>
            // is captured by LoadFile); the base summary is all we can surface without extending
            // the loader to also capture <param>/<returns>. Returning it is still strictly better
            // than nothing, and keeps this method additive rather than a rewrite of LoadFile.
            return baseSummary;
        }

        private static string BuildMethodDocId(string declaringTypeFullName, MethodDef method)
        {
            var sb = new StringBuilder("M:");
            sb.Append(declaringTypeFullName.Replace('/', '.')); // nested types use '/' in dnlib's FullName in some cases

            sb.Append('.');
            sb.Append(method.IsConstructor ? (method.IsStatic ? "#cctor" : "#ctor") : method.Name.String);

            var methodGenericCount = method.GenericParameters.Count;
            if (methodGenericCount > 0)
            {
                sb.Append("``").Append(methodGenericCount);
            }

            var parameters = method.Parameters
                .Where(p => !p.IsHiddenThisParameter && !p.IsReturnTypeParameter)
                .ToList();

            if (parameters.Count > 0)
            {
                sb.Append('(');
                sb.Append(string.Join(",", parameters.Select(p => BuildDocParamName(p.Type))));
                sb.Append(')');
            }

            if (method.Name == "op_Implicit" || method.Name == "op_Explicit")
            {
                sb.Append('~').Append(BuildDocParamName(method.ReturnType));
            }

            return sb.ToString();
        }

        // Mirrors the (simplified) rules from the C# language spec Annex for doc-ID param types:
        // generic type params -> `0, `1..., generic method params -> ``0, ``1...,
        // arrays -> [], by-ref -> @ suffix, generic instantiations -> {Arg1,Arg2}.
        private static string BuildDocParamName(TypeSig sig)
        {
            switch (sig)
            {
                case null:
                    return "System.Object";

                case ByRefSig byRefSig:
                    return BuildDocParamName(byRefSig.Next) + "@";

                case SZArraySig arr:
                    return BuildDocParamName(arr.Next) + "[]";

                case ArraySig arr:
                    return BuildDocParamName(arr.Next) + "[" + new string(',', (int)arr.Rank - 1) + "]";

                case PtrSig ptrSig:
                    return BuildDocParamName(ptrSig.Next) + "*";

                case GenericVar genericVar:
                    return "`" + genericVar.Number;

                case GenericMVar genericMVar:
                    return "``" + genericMVar.Number;

                case GenericInstSig gis:
                    var baseName = gis.GenericType.TypeDefOrRef.FullName;
                    var tickIndex = baseName.IndexOf('`');
                    if (tickIndex > 0)
                        baseName = baseName[..tickIndex];
                    var args = string.Join(",", gis.GenericArguments.Select(BuildDocParamName));
                    return $"{baseName}{{{args}}}";

                default:
                    return sig.FullName;
            }
        }

    }
}
