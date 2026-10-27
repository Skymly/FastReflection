using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;

namespace FastReflection.XmlDocs
{
    /// <summary>
    /// Read engine behind <see cref="XmlDocsExtensions"/>: resolves an assembly's XML documentation
    /// file (beside the assembly, then the app base directory, then the NuGet package cache when
    /// enabled), caches parsed files per assembly, generates XML documentation member names, and
    /// flattens elements to text. The per-assembly cache is a <see cref="ConditionalWeakTable{TKey, TValue}"/>
    /// so an entry releases with its Assembly — the unload-safety contract the cache research set
    /// for every cache in the project. Every IO or parse failure resolves to "no docs".
    /// </summary>
    internal static class XmlDocsReader
    {
        private static readonly ConditionalWeakTable<Assembly, AssemblyDocs> Cache = new();

        internal static XmlDocsFile? GetFile(Assembly assembly, XmlDocsOptions? options)
        {
            // Per-assembly entries keyed by what the file resolution depends on: an explicit
            // PathToXmlFile, or the automatic resolution mode (with/without the external probe).
            var key = options?.PathToXmlFile is { } path
                ? "P:" + path
                : options is null || options.ResolveExternalXmlDocs ? "A:1" : "A:0";

            return Cache.GetValue(assembly, _ => new AssemblyDocs())
                .Files.GetOrAdd(key, _ => new DocsSlot(Load(assembly, options)))
                .File;
        }

        private static XmlDocsFile? Load(Assembly assembly, XmlDocsOptions? options)
        {
            try
            {
                var path = options?.PathToXmlFile;
                if (path is null)
                {
                    var name = assembly.GetName().Name;
                    if (string.IsNullOrEmpty(name))
                        return null;

                    path = ResolvePath(assembly, name + ".xml", options?.ResolveExternalXmlDocs ?? true);
                }

                return path is not null && File.Exists(path) ? XmlDocsFile.Load(path) : null;
            }
            catch
            {
                // Missing, unreadable or malformed files are not exception conditions —
                // every read degrades to an empty string or a null element.
                return null;
            }
        }

        [UnconditionalSuppressMessage("SingleFile", "IL3000:Assembly.Location",
            Justification = "An empty Location is the expected single-file/AOT case — the read then falls back to the app base directory.")]
        private static string? ResolvePath(Assembly assembly, string fileName, bool resolveExternal)
        {
            // Beside the assembly. Assembly.Location is empty under single-file and
            // Native AOT publishing, in which case the base directory holds the file.
            var location = assembly.Location;
            if (!string.IsNullOrEmpty(location))
            {
                var path = Path.Combine(Path.GetDirectoryName(location)!, fileName);
                if (File.Exists(path))
                    return path;
            }

            var basePath = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(basePath))
                return basePath;

            return resolveExternal ? ResolveExternalPath(assembly.GetName(), fileName) : null;
        }

        // External-docs resolution: the NuGet package cache keeps <PackageId>.xml beside each
        // lib/ref asset, so a package's docs resolve even when the consuming app did not copy them.
        private static string? ResolveExternalPath(AssemblyName assemblyName, string fileName)
        {
            try
            {
                var root = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
                if (string.IsNullOrEmpty(root))
                    root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

                // The global-packages cache lowercases every package id on disk — probe the
                // lowercase path or the read misses on case-sensitive filesystems.
                var packageDirectory = Path.Combine(root, assemblyName.Name!.ToLowerInvariant());
                if (!Directory.Exists(packageDirectory))
                    return null;

                // Package layout is <package>/<version>/.../<file> — take the newest version
                // directory, not the lexical max (ordinal sorts "9.x" after "10.x").
                return Directory.GetFiles(packageDirectory, fileName, SearchOption.AllDirectories)
                    .OrderByDescending(path => PackageVersion(path, packageDirectory))
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        // The segment after <package>/ is the version directory. NuGet versions are SemVer —
        // a leading numeric prefix parses as Version; anything else (or a prerelease tag)
        // sorts below released versions.
        private static Version PackageVersion(string path, string packageDirectory)
        {
            var separator = packageDirectory.Length;
            var end = path.IndexOf(Path.DirectorySeparatorChar, separator + 1);
            if (end < 0)
                end = path.IndexOf(Path.AltDirectorySeparatorChar, separator + 1);
            var segment = end < 0 ? path.Substring(separator + 1) : path.Substring(separator + 1, end - separator - 1);
            var length = 0;
            while (length < segment.Length && (char.IsDigit(segment[length]) || segment[length] == '.'))
                length++;
            return Version.TryParse(segment.Substring(0, length), out var version) ? version : new Version(0, 0);
        }

        /// <summary>
        /// Builds the XML documentation member name (<c>T:</c>/<c>M:</c>/<c>P:</c>/<c>F:</c>/<c>E:</c> form)
        /// the compiler writes for <paramref name="member"/>.
        /// </summary>
        /// <exception cref="ArgumentException">The member kind has no documentation entry.</exception>
        internal static string GetMemberElementName(MemberInfo member)
        {
            switch (member)
            {
                case Type type:
                    return "T:" + GetDefinitionName(type);

                case MethodBase method:
                    return GetMethodElementName(method);

                case PropertyInfo property:
                    var resolvedProperty = OnDefinition(property);
                    return GetMemberElementName('P', resolvedProperty,
                        resolvedProperty is PropertyInfo indexer ? GetSignature(indexer.GetIndexParameters()) : null);

                case FieldInfo field:
                    return GetMemberElementName('F', OnDefinition(field), null);

                case EventInfo @event:
                    return GetMemberElementName('E', OnDefinition(@event), null);

                default:
                    throw new ArgumentException("The member kind has no XML documentation entry.", nameof(member));
            }
        }

        private static string GetMemberElementName(char prefix, MemberInfo member, string? signature)
        {
            var sb = new StringBuilder().Append(prefix).Append(':')
                .Append(GetDefinitionName(member.DeclaringType!)).Append('.')
                .Append(member.Name);
            if (signature is not null)
                sb.Append('(').Append(signature).Append(')');
            return sb.ToString();
        }

        private static string GetMethodElementName(MethodBase method)
        {
            var resolved = (MethodBase)OnDefinition(method);
            var sb = new StringBuilder("M:")
                .Append(GetDefinitionName(resolved.DeclaringType!)).Append('.')
                .Append(resolved is ConstructorInfo ? (resolved.IsStatic ? "#cctor" : "#ctor") : resolved.Name);
            var signature = GetSignature(resolved.GetParameters());
            if (signature is not null)
                sb.Append('(').Append(signature).Append(')');
            if (resolved is MethodInfo conversion && (conversion.Name is "op_Implicit" or "op_Explicit"))
                sb.Append('~').Append(GetSignatureTypeName(conversion.ReturnType));
            return sb.ToString();
        }

        private static string? GetSignature(ParameterInfo[] parameters)
        {
            if (parameters.Length == 0)
                return null;
            var sb = new StringBuilder();
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(GetSignatureTypeName(parameters[i].ParameterType));
            }

            return sb.ToString();
        }

        // Members reflected through a constructed type are documented on the generic definition —
        // the compiler emits signatures in terms of the type parameters, not the type arguments.
        // The metadata token is definition-scoped, so ResolveMember lands on the definition.
        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "Best-effort resolution of a closed-generic member to its definition for doc naming; under trimming a failed resolve falls back to the constructed member, degrading the read to a miss.")]
        private static MemberInfo OnDefinition(MemberInfo member)
        {
            var declaringType = member.DeclaringType;
            if (declaringType is null || !declaringType.IsGenericType || declaringType.IsGenericTypeDefinition)
                return member;
            try
            {
                return member.Module.ResolveMember(member.MetadataToken) ?? member;
            }
            catch
            {
                // Unresolved — the read degrades to the closed member's substituted signature.
                return member;
            }
        }

        // A type name in declaring-type position: namespace.name with arity kept (`Ns.Gen`1),
        // nested '+' flattened to '.', constructed arguments stripped. Element types
        // (array/pointer/byref) keep their FullName suffix — a `T:` entry never matches them,
        // so the read is a miss rather than an alias of the element type's docs.
        private static string GetDefinitionName(Type type)
        {
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
                type = type.GetGenericTypeDefinition();

            return (type.FullName ?? type.Name).Replace('+', '.');
        }

        // A type name in signature position: `i for type parameters, ``i for method parameters,
        // @/* /[] suffixes for element types, {args} for constructed generics.
        private static string GetSignatureTypeName(Type type)
        {
            if (type.IsGenericParameter)
                return (type.DeclaringMethod is null ? "`" : "``") + type.GenericParameterPosition;

            if (type.HasElementType)
            {
                var element = GetSignatureTypeName(type.GetElementType()!);
                if (type.IsByRef)
                    return element + "@";
                if (type.IsPointer)
                    return element + "*";
                if (type.GetArrayRank() == 1)
                    return element + "[]";

                var sb = new StringBuilder(element).Append('[');
                for (var i = 0; i < type.GetArrayRank(); i++)
                {
                    if (i > 0)
                        sb.Append(',');
                    sb.Append("0:");
                }

                return sb.Append(']').ToString();
            }

            // Constructed generics read Namespace.Name{arg,arg} — FullName is null while any
            // argument is an open parameter, so the base name comes from the definition.
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                var sb = new StringBuilder(StripArity(type.GetGenericTypeDefinition().FullName!).Replace('+', '.')).Append('{');
                var arguments = type.GetGenericArguments();
                for (var i = 0; i < arguments.Length; i++)
                {
                    if (i > 0)
                        sb.Append(',');
                    sb.Append(GetSignatureTypeName(arguments[i]));
                }

                return sb.Append('}').ToString();
            }

            var name = type.FullName ?? type.Name;
            var bracket = name.IndexOf('[');
            if (bracket >= 0)
                name = name.Substring(0, bracket);
            return StripArity(name).Replace('+', '.');
        }

        // Signature position drops arity markers: List{`0}, not List`1{`0}.
        private static string StripArity(string name)
        {
            var tick = name.IndexOf('`');
            while (tick >= 0)
            {
                var end = tick + 1;
                while (end < name.Length && char.IsDigit(name[end]))
                    end++;
                name = name.Remove(tick, end - tick);
                tick = name.IndexOf('`');
            }

            return name;
        }

        /// <summary>Flattens an element's content to text — the unformatted read.</summary>
        internal static string Flatten(XElement? element)
        {
            if (element is null)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var node in element.Nodes())
                AppendNode(node, sb);

            return RemoveLineBreakWhiteSpaces(sb.ToString());
        }

        private static void AppendNode(XNode node, StringBuilder sb)
        {
            if (node is XElement element)
            {
                switch (element.Name.LocalName)
                {
                    case "see":
                    case "seealso":
                        var text = element.Value;
                        if (string.IsNullOrEmpty(text))
                        {
                            text = element.Attribute("langword")?.Value
                                ?? element.Attribute("href")?.Value
                                ?? TrimCref(element.Attribute("cref")?.Value);
                        }

                        sb.Append(text);
                        break;

                    case "paramref":
                        sb.Append(element.Attribute("name")?.Value ?? element.Value);
                        break;

                    default:
                        sb.Append(Flatten(element).Trim());
                        break;
                }
            }
            else
            {
                sb.Append(node.ToString());
            }
        }

        // <see cref="M:Ns.Type.Member(...)"/> reads as the member's bare name.
        private static string? TrimCref(string? cref)
        {
            if (cref is null)
                return null;

            var value = cref.Trim('!', ':').Trim();
            var paren = value.IndexOf('(');
            if (paren >= 0)
                value = value.Substring(0, paren);
            var dot = value.LastIndexOf('.');
            return dot >= 0 ? value.Substring(dot + 1) : value;
        }

        // Compiler doc text indents every continuation line by the tag's common indent —
        // collapse that indent so multi-line docs read as written.
        private static string RemoveLineBreakWhiteSpaces(string documentation)
        {
            if (documentation.Length == 0)
                return string.Empty;

            var doc = "\n" + documentation.Replace("\r", "").Trim('\n');
            var newline = doc.IndexOf('\n');
            var end = newline + 1;
            while (end < doc.Length && (doc[end] == ' ' || doc[end] == '\t'))
                end++;

            doc = doc.Replace(doc.Substring(newline, end - newline), "\n");
            return doc.Trim('\n');
        }

        private sealed class DocsSlot
        {
            internal readonly XmlDocsFile? File;
            internal DocsSlot(XmlDocsFile? file) => File = file;
        }

        private sealed class AssemblyDocs
        {
            // Ordinal — paths are case-sensitive on Linux, and duplicate same-file entries
            // under differing casing are harmless (the same file parses once more).
            internal readonly ConcurrentDictionary<string, DocsSlot> Files = new(StringComparer.Ordinal);
        }
    }

    /// <summary>A parsed documentation file: the document plus the member-name index.</summary>
    internal sealed class XmlDocsFile
    {
        internal readonly XDocument Document;
        private readonly Dictionary<string, XElement> _members;

        private XmlDocsFile(XDocument document)
        {
            Document = document;
            _members = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var member in document.Root?.Element("members")?.Elements("member") ?? Enumerable.Empty<XElement>())
            {
                var name = member.Attribute("name")?.Value;
                if (name is not null && !_members.ContainsKey(name))
                    _members.Add(name, member);
            }
        }

        internal static XmlDocsFile Load(string path) => new(XDocument.Load(path));

        internal XElement? Find(string name) => _members.TryGetValue(name, out var element) ? element : null;
    }
}
