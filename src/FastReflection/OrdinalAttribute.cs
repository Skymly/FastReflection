namespace FastReflection
{
    /// <summary>Orders a Member within every type accessor that holds it: Members carrying this attribute come first, ascending by <see cref="Value"/>; Members without it follow; equal values keep enumeration order. The value is an ordering input, never the Member's <see cref="Member.Ordinal"/>.</summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class OrdinalAttribute : Attribute
    {
        /// <summary>Creates the ordering attribute with the given value.</summary>
        /// <param name="value">The ordering key; lower values sort earlier.</param>
        public OrdinalAttribute(int value) => Value = value;

        /// <summary>The ordering key. An ordering input only — never the Member's <see cref="Member.Ordinal"/>.</summary>
        public int Value { get; }
    }
}
