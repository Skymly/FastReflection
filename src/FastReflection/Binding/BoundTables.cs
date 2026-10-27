using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using FastReflection.Emit;

namespace FastReflection.Binding
{
    /// <summary>
    /// The bindable domain of one type accessor: methods, properties (indexers included),
    /// fields, and constructors at the accessor's visibility, grouped for name lookup.
    /// Built once per accessor, on the first bound call. Same-signature members keep the
    /// most-derived declaration (overrides and same-signature <c>new</c> shadows).
    /// </summary>
    internal sealed class BoundTables
    {
        [DynamicallyAccessedMembers(TypeAccessor.DiscoveryMemberTypes)]
        internal readonly Type Owner;
        internal readonly Dictionary<string, MethodInfo[]> Methods;
        internal readonly Dictionary<string, PropertyInfo[]> Properties;
        internal readonly Dictionary<string, FieldInfo> Fields;
        internal readonly ConstructorInfo[] Ctors;
        internal readonly string[] MemberNames;

        private BoundTables(
            [DynamicallyAccessedMembers(TypeAccessor.DiscoveryMemberTypes)]
            Type owner,
            Dictionary<string, MethodInfo[]> methods,
            Dictionary<string, PropertyInfo[]> properties,
            Dictionary<string, FieldInfo> fields,
            ConstructorInfo[] ctors,
            string[] memberNames)
        {
            Owner = owner;
            Methods = methods;
            Properties = properties;
            Fields = fields;
            Ctors = ctors;
            MemberNames = memberNames;
        }

        /// <summary>Member-discovery enumeration for the bound domain: interface types walk themselves plus inherited interfaces; other types walk the base chain, most-derived first.</summary>
        internal static Type[] CollectLevels(
            [DynamicallyAccessedMembers(TypeAccessor.DiscoveryMemberTypes)]
            Type type)
        {
            if (!type.IsInterface)
            {
                var chain = new List<Type>();
                for (var t = type; t != null; t = t.BaseType)
                    chain.Add(t);
                return chain.ToArray();
            }

            var set = new List<Type> { type };
            set.AddRange(type.GetInterfaces());
            return set.ToArray();
        }

        [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = EmitHelper.LevelEnumerationJustification)]
        [UnconditionalSuppressMessage("Trimming", "IL2065", Justification = EmitHelper.LevelEnumerationJustification)]
        internal static BoundTables Build(
            [DynamicallyAccessedMembers(TypeAccessor.DiscoveryMemberTypes)]
            Type type, bool nonPublic)
        {
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly;
            if (nonPublic) flags |= BindingFlags.NonPublic;

            var methods = new Dictionary<string, List<MethodInfo>>(StringComparer.Ordinal);
            var properties = new Dictionary<string, List<PropertyInfo>>(StringComparer.Ordinal);
            var fields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            var seenMethods = new HashSet<MethodSignature>();
            var seenProperties = new HashSet<PropertySignature>();

            foreach (var t in CollectLevels(type))
            {
                foreach (var method in t.GetMethods(flags))
                {
                    if (seenMethods.Add(new MethodSignature(method)))
                        Add(methods, method.Name, method);
                }

                foreach (var prop in t.GetProperties(flags))
                {
                    if (seenProperties.Add(new PropertySignature(prop)))
                        Add(properties, prop.Name, prop);
                }

                foreach (var field in t.GetFields(flags))
                {
                    if (!fields.ContainsKey(field.Name))
                        fields.Add(field.Name, field);
                }
            }

            // The Static flag admits the type initializer — never a bindable constructor.
            var ctors = type.IsInterface
                ? Array.Empty<ConstructorInfo>()
                : type.GetConstructors(flags).Where(c => !c.IsStatic).ToArray();

            return new BoundTables(
                type,
                ToArrays(methods),
                ToArrays(properties),
                fields,
                ctors,
                BuildMemberNames(methods, properties, fields));
        }

        private static void Add<T>(Dictionary<string, List<T>> map, string name, T value)
        {
            if (!map.TryGetValue(name, out var list))
                map[name] = list = new List<T>();
            list.Add(value);
        }

        private static Dictionary<string, T[]> ToArrays<T>(Dictionary<string, List<T>> map)
        {
            var result = new Dictionary<string, T[]>(map.Count, StringComparer.Ordinal);
            foreach (var kv in map)
                result[kv.Key] = kv.Value.ToArray();
            return result;
        }

        /// <summary>
        /// Distinct case-sensitive bindable names: methods (overloads collapse; accessor
        /// <c>get_</c>/<c>set_</c>/<c>add_</c>/<c>remove_</c> spellings excluded — the property
        /// or event name stands instead), properties, and fields. Enumeration order, deduped.
        /// </summary>
        private static string[] BuildMemberNames(
            Dictionary<string, List<MethodInfo>> methods,
            Dictionary<string, List<PropertyInfo>> properties,
            Dictionary<string, FieldInfo> fields)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var names = new List<string>();

            foreach (var kv in methods)
            {
                // Accessor methods are bound through their property/event name, never listed raw.
                if (kv.Value.All(m => m.IsSpecialName && IsAccessorName(m.Name)))
                    continue;
                if (seen.Add(kv.Key)) names.Add(kv.Key);
            }
            foreach (var name in properties.Keys)
                if (seen.Add(name)) names.Add(name);
            foreach (var name in fields.Keys)
                if (seen.Add(name)) names.Add(name);
            return names.ToArray();
        }

        private static bool IsAccessorName(string name)
            => name.StartsWith("get_", StringComparison.Ordinal)
               || name.StartsWith("set_", StringComparison.Ordinal)
               || name.StartsWith("add_", StringComparison.Ordinal)
               || name.StartsWith("remove_", StringComparison.Ordinal);

        /// <summary>Name + generic arity + parameter types — the signature C# overloads and hides on.</summary>
        private readonly struct MethodSignature : IEquatable<MethodSignature>
        {
            private readonly string _name;
            private readonly int _genericArity;
            private readonly Type[] _paramTypes;

            internal MethodSignature(MethodInfo method)
            {
                _name = method.Name;
                _genericArity = method.IsGenericMethodDefinition ? method.GetGenericArguments().Length : 0;
                _paramTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
            }

            public bool Equals(MethodSignature other)
                => _name == other._name && _genericArity == other._genericArity && _paramTypes.SequenceEqual(other._paramTypes);

            public override bool Equals(object? obj) => obj is MethodSignature other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = StringComparer.Ordinal.GetHashCode(_name) * 31 + _genericArity;
                    foreach (var t in _paramTypes) hash = hash * 17 + t.GetHashCode();
                    return hash;
                }
            }
        }

        /// <summary>Name + index-parameter types — a property's overloadable identity.</summary>
        private readonly struct PropertySignature : IEquatable<PropertySignature>
        {
            private readonly string _name;
            private readonly Type[] _indexTypes;

            internal PropertySignature(PropertyInfo prop)
            {
                _name = prop.Name;
                _indexTypes = prop.GetIndexParameters().Select(p => p.ParameterType).ToArray();
            }

            public bool Equals(PropertySignature other)
                => _name == other._name && _indexTypes.SequenceEqual(other._indexTypes);

            public override bool Equals(object? obj) => obj is PropertySignature other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = StringComparer.Ordinal.GetHashCode(_name);
                    foreach (var t in _indexTypes) hash = hash * 17 + t.GetHashCode();
                    return hash;
                }
            }
        }
    }
}
