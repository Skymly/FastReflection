namespace FastReflection.XmlDocs
{
    /// <summary>
    /// Options for the XML documentation reads on <see cref="XmlDocsExtensions"/>.
    /// </summary>
    public class XmlDocsOptions
    {
        /// <summary>
        /// Gets or sets a path to the XML documentation file to read instead of resolving the file
        /// beside the member's assembly. When null, the file is resolved beside the assembly location.
        /// </summary>
        public string? PathToXmlFile { get; set; }

        /// <summary>
        /// Gets or sets whether a member whose assembly has no colocated XML file falls back to the
        /// NuGet package cache (<c>%NUGET_PACKAGES%</c> or <c>~/.nuget/packages</c>). Default true.
        /// </summary>
        public bool ResolveExternalXmlDocs { get; set; } = true;
    }
}
