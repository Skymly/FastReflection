using System.Collections.Concurrent;
using System.Reflection;
using FastReflection.Emit;

namespace FastReflection
{
    /// <summary>
    /// Fast member access for a type known at runtime. Resolves a member name to a
    /// per-member compiled delegate once, then dispatches by dictionary lookup +
    /// integer ordinal — the string never reaches the hot path twice.
    /// Accessors are cached per <see cref="Type"/> and thread-safe.
    /// </summary>
    public abstract class TypeAccessor
    {
        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            internal readonly Type Type;
            internal readonly bool NonPublic;

            internal CacheKey(Type type, bool nonPublic)
            {
                Type = type;
                NonPublic = nonPublic;
            }

            public bool Equals(CacheKey other) => Type == other.Type && NonPublic == other.NonPublic;
            public override bool Equals(object? obj) => obj is CacheKey other && Equals(other);
            public override int GetHashCode() => Type.GetHashCode() ^ (NonPublic ? 1 : 0);
        }

        private static readonly ConcurrentDictionary<CacheKey, TypeAccessor> s_cache = new();

        /// <summary>
        /// Gets (creating on first use) the accessor for <paramref name="type"/>.
        /// <paramref name="allowNonPublicAccessors"/> widens the member set to non-public
        /// members and non-public property accessors.
        /// </summary>
        public static TypeAccessor Create(Type type, bool allowNonPublicAccessors = false)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (type.ContainsGenericParameters)
                throw new ArgumentException("Open generic types are not supported; close the generic type first.", nameof(type));
            return s_cache.GetOrAdd(
                new CacheKey(type, allowNonPublicAccessors),
                static k => new RuntimeTypeAccessor(k.Type, k.NonPublic));
        }

        /// <summary>The type this accessor was built for.</summary>
        public abstract Type Type { get; }

        /// <summary>Name-indexed member access (ordinal comparison, case-sensitive).</summary>
        /// <exception cref="MissingMemberException">Unknown member, or member not readable/writable.</exception>
        public abstract object? this[object? target, string name] { get; set; }

        /// <summary>Ordinal-indexed read — the allocation-free hot path for known members.</summary>
        public abstract object? GetValue(object? target, int ordinal);

        /// <summary>Ordinal-indexed write.</summary>
        public abstract void SetValue(object? target, int ordinal, object? value);

        /// <summary>Maps a member name to its ordinal for use with <see cref="GetValue(object?, int)"/>.</summary>
        public abstract bool TryGetOrdinal(string name, out int ordinal);

        /// <summary>All reachable members (properties and fields; indexer properties excluded).</summary>
        public abstract IReadOnlyList<Member> GetMembers();

        /// <summary>Whether <see cref="CreateNew"/> can produce instances.</summary>
        public abstract bool CreateNewSupported { get; }

        /// <summary>Creates an instance via the parameterless constructor (or <c>default</c> for value types).</summary>
        public abstract object CreateNew();
    }

    internal sealed class RuntimeTypeAccessor : TypeAccessor
    {
        private struct Entry
        {
            internal Func<object?, object?>? Getter;
            internal Action<object?, object?>? Setter;
        }

        private readonly Type _type;
        private readonly Dictionary<string, int> _map;
        private readonly Entry[] _entries;
        private readonly Member[] _members;
        private readonly Func<object?>? _factory;

        internal RuntimeTypeAccessor(Type type, bool allowNonPublicAccessors)
        {
            _type = type;

            var flags = BindingFlags.Instance | BindingFlags.Public;
            if (allowNonPublicAccessors) flags |= BindingFlags.NonPublic;

            _map = new Dictionary<string, int>(StringComparer.Ordinal);
            var entries = new List<Entry>();
            var members = new List<Member>();

            foreach (var t in CollectMemberTypes(type))
            {
                foreach (var prop in t.GetProperties(flags))
                {
                    if (prop.GetIndexParameters().Length != 0) continue; // indexers: use CreateInvoker on the accessor
                    var get = prop.GetGetMethod(allowNonPublicAccessors);
                    var set = prop.GetSetMethod(allowNonPublicAccessors);
                    if (get == null && set == null) continue;
                    if (_map.ContainsKey(prop.Name)) continue; // inherited-interface duplicates

                    var ordinal = members.Count;
                    entries.Add(new Entry
                    {
                        Getter = TryCreateGetter(prop, get),
                        Setter = TryCreateSetter(prop, set)
                    });
                    members.Add(new Member(prop, prop.PropertyType, ordinal, get != null, set != null));
                    _map[prop.Name] = ordinal;
                }

                foreach (var field in t.GetFields(flags))
                {
                    if (field.IsLiteral || _map.ContainsKey(field.Name)) continue;

                    var ordinal = members.Count;
                    var writable = !field.IsInitOnly;
                    entries.Add(new Entry
                    {
                        Getter = TryCreateGetter(field),
                        Setter = writable ? TryCreateSetter(field) : null
                    });
                    members.Add(new Member(field, field.FieldType, ordinal, true, writable));
                    _map[field.Name] = ordinal;
                }
            }

            _entries = entries.ToArray();
            _members = members.ToArray();
            _factory = TryCreateFactory(type, allowNonPublicAccessors);
        }

        public override Type Type => _type;

        public override object? this[object? target, string name]
        {
            get => GetValue(target, RequireOrdinal(name));
            set => SetValue(target, RequireOrdinal(name), value);
        }

        public override object? GetValue(object? target, int ordinal)
        {
            if ((uint)ordinal >= (uint)_entries.Length)
                throw new ArgumentOutOfRangeException(nameof(ordinal));
            var getter = _entries[ordinal].Getter
                ?? throw new MissingMemberException(_type.FullName, _members[ordinal].Name);
            return getter(target);
        }

        public override void SetValue(object? target, int ordinal, object? value)
        {
            if ((uint)ordinal >= (uint)_entries.Length)
                throw new ArgumentOutOfRangeException(nameof(ordinal));
            var setter = _entries[ordinal].Setter
                ?? throw new MissingMemberException(_type.FullName, _members[ordinal].Name);
            setter(target, value);
        }

        public override bool TryGetOrdinal(string name, out int ordinal)
            => _map.TryGetValue(name, out ordinal);

        public override IReadOnlyList<Member> GetMembers() => _members;

        public override bool CreateNewSupported => _factory != null;

        public override object CreateNew()
            => _factory?.Invoke()
               ?? throw new InvalidOperationException($"Type '{_type}' has no parameterless constructor.");

        private int RequireOrdinal(string name)
            => _map.TryGetValue(name, out var ordinal)
                ? ordinal
                : throw new MissingMemberException(_type.FullName, name);

        private static Type[] CollectMemberTypes(Type type)
        {
            if (!type.IsInterface) return new[] { type };
            var list = new List<Type> { type };
            list.AddRange(type.GetInterfaces());
            return list.ToArray();
        }

        private static Func<object?>? TryCreateFactory(Type type, bool nonPublic)
        {
            try { return AccessorEmitter.CreateFactory(type, nonPublic); }
            catch (ArgumentException) { return null; }
        }

        // Emit first; fall back to plain reflection when dynamic code is unavailable.

        private static Func<object?, object?>? TryCreateGetter(PropertyInfo prop, MethodInfo? get)
        {
            if (get == null) return null;
            if (!EmitHelper.DynamicCodeSupported) return target => get.Invoke(target, null);
            try { return AccessorEmitter.EmitPropertyGetter(prop, get); }
            catch { return target => get.Invoke(target, null); }
        }

        private static Action<object?, object?>? TryCreateSetter(PropertyInfo prop, MethodInfo? set)
        {
            if (set == null) return null;
            if (!EmitHelper.DynamicCodeSupported) return (target, value) => set.Invoke(target, new[] { value });
            try { return AccessorEmitter.EmitPropertySetter(prop, set); }
            catch { return (target, value) => set.Invoke(target, new[] { value }); }
        }

        private static Func<object?, object?>? TryCreateGetter(FieldInfo field)
        {
            if (!EmitHelper.DynamicCodeSupported) return field.GetValue;
            try { return AccessorEmitter.EmitFieldGetter(field); }
            catch { return field.GetValue; }
        }

        private static Action<object?, object?>? TryCreateSetter(FieldInfo field)
        {
            if (!EmitHelper.DynamicCodeSupported) return field.SetValue;
            try { return AccessorEmitter.EmitFieldSetter(field); }
            catch { return field.SetValue; }
        }
    }
}
