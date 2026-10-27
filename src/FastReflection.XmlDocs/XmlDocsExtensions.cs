using System.Reflection;
using System.Xml.Linq;

namespace FastReflection.XmlDocs
{
    /// <summary>
    /// Reads XML documentation files for reflection objects — the summary, remarks, tag, element and
    /// parameter docs of a member, and the resolved document element of an assembly. XML files resolve
    /// beside the member's assembly (or via <see cref="XmlDocsOptions.PathToXmlFile"/>); parsed files
    /// are cached internally per assembly and release with it. The no-argument reads use a default
    /// <see cref="XmlDocsOptions"/>; <see cref="Type"/> receivers bind through <see cref="MemberInfo"/>.
    /// </summary>
    public static class XmlDocsExtensions
    {
        /// <summary>Returns the contents of the "summary" tag for the specified member.</summary>
        /// <param name="member">The reflected member.</param>
        /// <returns>The contents of the "summary" tag, or an empty string.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="member"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="member"/> is not a type, method, constructor, property, field or event.</exception>
        public static string GetXmlDocsSummary(this MemberInfo member)
            => GetXmlDocsSummary(member, options: null);

        /// <summary>Returns the contents of the "summary" tag for the specified member.</summary>
        /// <param name="member">The reflected member.</param>
        /// <param name="options">The XML docs reading options.</param>
        /// <returns>The contents of the "summary" tag, or an empty string.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="member"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="member"/> is not a type, method, constructor, property, field or event.</exception>
        public static string GetXmlDocsSummary(this MemberInfo member, XmlDocsOptions? options)
            => GetXmlDocsTag(member, "summary", options);

        /// <summary>Returns the contents of the "remarks" tag for the specified member.</summary>
        /// <param name="member">The reflected member.</param>
        /// <returns>The contents of the "remarks" tag, or an empty string.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="member"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="member"/> is not a type, method, constructor, property, field or event.</exception>
        public static string GetXmlDocsRemarks(this MemberInfo member)
            => GetXmlDocsRemarks(member, options: null);

        /// <summary>Returns the contents of the "remarks" tag for the specified member.</summary>
        /// <param name="member">The reflected member.</param>
        /// <param name="options">The XML docs reading options.</param>
        /// <returns>The contents of the "remarks" tag, or an empty string.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="member"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="member"/> is not a type, method, constructor, property, field or event.</exception>
        public static string GetXmlDocsRemarks(this MemberInfo member, XmlDocsOptions? options)
            => GetXmlDocsTag(member, "remarks", options);

        /// <summary>Returns the contents of an XML documentation tag for the specified member.</summary>
        /// <param name="member">The reflected member.</param>
        /// <param name="tagName">Name of the tag.</param>
        /// <returns>The contents of the tag, or an empty string.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="member"/> or <paramref name="tagName"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="member"/> is not a type, method, constructor, property, field or event.</exception>
        public static string GetXmlDocsTag(this MemberInfo member, string tagName)
            => GetXmlDocsTag(member, tagName, options: null);

        /// <summary>Returns the contents of an XML documentation tag for the specified member.</summary>
        /// <param name="member">The reflected member.</param>
        /// <param name="tagName">Name of the tag.</param>
        /// <param name="options">The XML docs reading options.</param>
        /// <returns>The contents of the tag, or an empty string.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="member"/> or <paramref name="tagName"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="member"/> is not a type, method, constructor, property, field or event.</exception>
        public static string GetXmlDocsTag(this MemberInfo member, string tagName, XmlDocsOptions? options)
        {
            if (member is null)
                throw new ArgumentNullException(nameof(member));
            if (tagName is null)
                throw new ArgumentNullException(nameof(tagName));

            var element = GetXmlDocsElement(member, options);
            return XmlDocsReader.Flatten(element?.Element(tagName));
        }

        /// <summary>Returns the member's XML documentation element.</summary>
        /// <param name="member">The reflected member.</param>
        /// <returns>The member's element, or null when no documentation file or entry resolves.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="member"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="member"/> is not a type, method, constructor, property, field or event.</exception>
        public static XElement? GetXmlDocsElement(this MemberInfo member)
            => GetXmlDocsElement(member, options: null);

        /// <summary>Returns the member's XML documentation element.</summary>
        /// <param name="member">The reflected member.</param>
        /// <param name="options">The XML docs reading options.</param>
        /// <returns>The member's element, or null when no documentation file or entry resolves.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="member"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="member"/> is not a type, method, constructor, property, field or event.</exception>
        public static XElement? GetXmlDocsElement(this MemberInfo member, XmlDocsOptions? options)
        {
            if (member is null)
                throw new ArgumentNullException(nameof(member));

            var file = XmlDocsReader.GetFile(member.Module.Assembly, options);
            if (file is null)
                return null;

            var element = file.Find(XmlDocsReader.GetMemberElementName(member));
            return element is null ? null : new XElement(element);
        }

        /// <summary>Returns the contents of the "returns" or "param" tag for the specified parameter.</summary>
        /// <param name="parameter">The reflected parameter or return value.</param>
        /// <returns>The contents of the "returns" or "param" tag, or an empty string.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="parameter"/> is null.</exception>
        public static string GetXmlDocs(this ParameterInfo parameter)
            => GetXmlDocs(parameter, options: null);

        /// <summary>Returns the contents of the "returns" or "param" tag for the specified parameter.</summary>
        /// <param name="parameter">The reflected parameter or return value.</param>
        /// <param name="options">The XML docs reading options.</param>
        /// <returns>The contents of the "returns" or "param" tag, or an empty string.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="parameter"/> is null.</exception>
        public static string GetXmlDocs(this ParameterInfo parameter, XmlDocsOptions? options)
        {
            if (parameter is null)
                throw new ArgumentNullException(nameof(parameter));

            var element = GetXmlDocsElement(parameter.Member, options);
            if (element is null)
                return string.Empty;

            XElement? doc = null;
            if (parameter.IsRetval || string.IsNullOrEmpty(parameter.Name))
            {
                doc = element.Element("returns");
            }
            else
            {
                foreach (var param in element.Elements("param"))
                {
                    if (param.Attribute("name")?.Value == parameter.Name)
                    {
                        doc = param;
                        break;
                    }
                }
            }

            return XmlDocsReader.Flatten(doc);
        }

        /// <summary>Returns the root element of the assembly's resolved XML documentation file.</summary>
        /// <param name="assembly">The assembly whose documentation file to resolve.</param>
        /// <returns>The document's root element, or null when no documentation file resolves.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is null.</exception>
        public static XElement? GetXmlDocsElement(this Assembly assembly)
            => GetXmlDocsElement(assembly, options: null);

        /// <summary>Returns the root element of the assembly's resolved XML documentation file.</summary>
        /// <param name="assembly">The assembly whose documentation file to resolve.</param>
        /// <param name="options">The XML docs reading options.</param>
        /// <returns>The document's root element, or null when no documentation file resolves.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is null.</exception>
        public static XElement? GetXmlDocsElement(this Assembly assembly, XmlDocsOptions? options)
        {
            if (assembly is null)
                throw new ArgumentNullException(nameof(assembly));

            var root = XmlDocsReader.GetFile(assembly, options)?.Document.Root;
            return root is null ? null : new XElement(root);
        }
    }
}
