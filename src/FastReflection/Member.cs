using System.Reflection;

namespace FastReflection
{
    /// <summary>An instance property or field in one <see cref="TypeAccessor"/>, identified by a case-sensitive name. Not a method, event, indexer, constant, or static member.</summary>
    public sealed class Member
    {
        internal Member(MemberInfo info, Type memberType, int ordinal, bool canRead, bool canWrite)
        {
            MemberInfo = info;
            Name = info.Name;
            Type = memberType;
            Ordinal = ordinal;
            CanRead = canRead;
            CanWrite = canWrite;
        }

        /// <summary>Case-sensitive name of this Member in its type accessor.</summary>
        public string Name { get; }

        /// <summary>Property type or field type.</summary>
        public Type Type { get; }

        /// <summary>The underlying <see cref="PropertyInfo"/> or <see cref="FieldInfo"/> — exposed for attribute/metadata inspection.</summary>
        public MemberInfo MemberInfo { get; }

        /// <summary>Index of this Member in its type accessor. Not a name, and not an identity outside that type accessor.</summary>
        public int Ordinal { get; }

        /// <summary>Whether this type accessor can read this Member. The Member remains a Member when this is false.</summary>
        public bool CanRead { get; }

        /// <summary>Whether this type accessor can write this Member, including an init-only property after construction. False does not mean no other access can write the property.</summary>
        public bool CanWrite { get; }

        /// <summary>True when the member is a property.</summary>
        public bool IsProperty => MemberInfo is PropertyInfo;

        /// <summary>True when the member is a field.</summary>
        public bool IsField => MemberInfo is FieldInfo;

        /// <summary>Whether the member carries an attribute of <paramref name="attributeType"/>.</summary>
        /// <param name="attributeType">The attribute type to look for.</param>
        /// <param name="inherit">True to search the inheritance chain.</param>
        /// <returns>True when the attribute is present.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="attributeType"/> is null.</exception>
        public bool IsDefined(Type attributeType, bool inherit = true)
            => MemberInfo.IsDefined(attributeType, inherit);

        /// <inheritdoc />
        public override string ToString() => $"{Type.Name} {Name}";
    }
}
