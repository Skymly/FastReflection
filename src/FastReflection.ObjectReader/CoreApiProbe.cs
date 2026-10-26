namespace FastReflection.ObjectReader;

// The DbDataReader surface lands in #36. This probe keeps the ProjectReference to the core
// exercised through public API only — the core grants no InternalsVisibleTo to satellites
// (ADR-0002/ADR-0003), so any internal reach fails this assembly's compile.
internal static class CoreApiProbe
{
    internal static readonly Type AccessorType = typeof(TypeAccessor);
}
