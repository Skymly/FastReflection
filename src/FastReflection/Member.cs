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

        /// <summary>Index of this Member in its type accessor. Not a name, and not an identity outside that type accessor. An <see cref="OrdinalAttribute"/> value orders Members but is not this index.</summary>
        public int Ordinal { get; }

        /// <summary>Whether this type accessor can read this Member. The Member remains a Member when this is false.</summary>
        public bool CanRead { get; }

        /// <summary>Whether this type accessor can write this Member, including an init-only property after construction. False does not mean no other access can write the property.</summary>
        public bool CanWrite { get; }

        /// <summary>True when the member is a property.</summary>
        public bool IsProperty => MemberInfo is PropertyInfo;

        /// <summary>True when the member is a field.</summary>
        public bool IsField => MemberInfo is FieldInfo;

#pragma warning disable RS0026 // spec-mandated pair (docs/specs/1.0-public-api.md §Accessor deltas); generic arity keeps calls unambiguous
        /// <summary>The attribute of <paramref name="attributeType"/> on the member, or null when it has none.</summary>
        /// <param name="attributeType">The attribute type to look for; derived attribute types match.</param>
        /// <param name="inherit">True to search the inheritance chain, including the base declaration of an overridden property.</param>
        /// <returns>The attribute, or null on a miss.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="attributeType"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="attributeType"/> does not derive from <see cref="Attribute"/>.</exception>
        /// <exception cref="AmbiguousMatchException">More than one attribute of <paramref name="attributeType"/> applies.</exception>
        public Attribute? GetAttribute(Type attributeType, bool inherit = true)
        {
            if (attributeType == null) throw new ArgumentNullException(nameof(attributeType));
            return Attribute.GetCustomAttribute(MemberInfo, attributeType, inherit);
        }

        /// <summary>The attribute of <typeparamref name="T"/> on the member, or null when it has none.</summary>
        /// <param name="inherit">True to search the inheritance chain, including the base declaration of an overridden property.</param>
        /// <typeparam name="T">The attribute type to look for; derived attribute types match.</typeparam>
        /// <returns>The attribute, or null on a miss.</returns>
        /// <exception cref="AmbiguousMatchException">More than one attribute of <typeparamref name="T"/> applies.</exception>
        public T? GetAttribute<T>(bool inherit = true) where T : Attribute
            => (T?)GetAttribute(typeof(T), inherit);
#pragma warning restore RS0026

        /// <summary>Whether the member carries an attribute of <paramref name="attributeType"/>.</summary>
        /// <param name="attributeType">The attribute type to look for.</param>
        /// <param name="inherit">True to search the inheritance chain.</param>
        /// <returns>True when the attribute is present.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="attributeType"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="attributeType"/> does not derive from <see cref="Attribute"/>.</exception>
        public bool IsDefined(Type attributeType, bool inherit = true)
        {
            if (attributeType == null) throw new ArgumentNullException(nameof(attributeType));
            return Attribute.IsDefined(MemberInfo, attributeType, inherit);
        }

        /// <inheritdoc />
        public override string ToString() => $"{Type.Name} {Name}";
    }
}
