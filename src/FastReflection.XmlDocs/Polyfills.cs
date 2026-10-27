// Polyfilled BCL attributes for downlevel target frameworks.
// The linker/trimmer recognizes these attributes by full name, so internal
// definitions are sufficient for annotation purposes on net462/netstandard2.0.
// Mirrors the core's Polyfills.cs; the satellite has no InternalsVisibleTo and
// carries its own copy.

#if !NET5_0_OR_GREATER
namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
    internal sealed class UnconditionalSuppressMessageAttribute : Attribute
    {
        public UnconditionalSuppressMessageAttribute(string category, string checkId)
        {
            Category = category;
            CheckId = checkId;
        }

        public string Category { get; }
        public string CheckId { get; }
        public string? Scope { get; set; }
        public string? Target { get; set; }
        public string? MessageId { get; set; }
        public string? Justification { get; set; }
    }
}
#endif
