using System.Reflection;

namespace FastReflection
{
    /// <summary>Describes a property or field reachable through a <see cref="TypeAccessor"/>.</summary>
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

        /// <summary>Member name.</summary>
        public string Name { get; }

        /// <summary>Property type or field type.</summary>
        public Type Type { get; }

        /// <summary>The underlying <see cref="PropertyInfo"/> or <see cref="FieldInfo"/> — exposed for attribute/metadata inspection.</summary>
        public MemberInfo MemberInfo { get; }

        /// <summary>Index into the accessor's member table; usable with <see cref="TypeAccessor.GetValue(object?, int)"/>.</summary>
        public int Ordinal { get; }

        /// <summary>Whether the member can be read through this accessor.</summary>
        public bool CanRead { get; }

        /// <summary>Whether the member can be written through this accessor.</summary>
        public bool CanWrite { get; }

        /// <summary>True when the member is a property.</summary>
        public bool IsProperty => MemberInfo is PropertyInfo;

        /// <summary>True when the member is a field.</summary>
        public bool IsField => MemberInfo is FieldInfo;

        /// <summary>Whether the member carries an attribute of <paramref name="attributeType"/>.</summary>
        public bool IsDefined(Type attributeType, bool inherit = true)
            => MemberInfo.IsDefined(attributeType, inherit);

        /// <inheritdoc />
        public override string ToString() => $"{Type.Name} {Name}";
    }
}
