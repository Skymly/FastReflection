using System.Reflection;

namespace FastReflection.Binding
{
    internal enum BoundKind : byte
    {
        Invoke = 0,
        Get = 1,
        Set = 2,
        Ctor = 3,
    }

    /// <summary>
    /// The bound-invoker cache key: (member kind, name, normalized signature). The signature is
    /// runtime argument types + named-argument names/order + explicit type arguments. A null
    /// <see cref="ArgTypes"/> element marks a null literal. Strong <see cref="Type"/> keys per ADR-0002.
    /// </summary>
    internal readonly struct BoundSignature : IEquatable<BoundSignature>
    {
        internal readonly BoundKind Kind;
        internal readonly string Name;
        internal readonly Type?[]? ArgTypes;
        internal readonly string[]? Named;
        internal readonly Type?[]? NamedTypes;
        internal readonly Type[]? TypeArguments;

        internal BoundSignature(BoundKind kind, string name, Type?[]? argTypes, string[]? named, Type?[]? namedTypes, Type[]? typeArguments)
        {
            Kind = kind;
            Name = name;
            ArgTypes = argTypes;
            Named = named;
            NamedTypes = namedTypes;
            TypeArguments = typeArguments;
        }

        public bool Equals(BoundSignature other)
            => Kind == other.Kind
               && Name == other.Name
               && SeqEquals(ArgTypes, other.ArgTypes)
               && SeqEquals(Named, other.Named)
               && SeqEquals(NamedTypes, other.NamedTypes)
               && SeqEquals(TypeArguments, other.TypeArguments);

        public override bool Equals(object? obj) => obj is BoundSignature other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Kind * 31 + StringComparer.Ordinal.GetHashCode(Name);
                if (ArgTypes != null)
                    foreach (var t in ArgTypes) hash = hash * 17 + (t?.GetHashCode() ?? 0);
                if (Named != null)
                    foreach (var n in Named) hash = hash * 17 + StringComparer.Ordinal.GetHashCode(n);
                if (NamedTypes != null)
                    foreach (var t in NamedTypes) hash = hash * 17 + (t?.GetHashCode() ?? 0);
                if (TypeArguments != null)
                    foreach (var t in TypeArguments) hash = hash * 17 + t.GetHashCode();
                return hash;
            }
        }

        private static bool SeqEquals<T>(T[]? a, T[]? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
                if (!Equals(a[i], b[i])) return false;
            return true;
        }
    }
}
