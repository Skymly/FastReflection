using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using FastReflection.Binding;
using FastReflection.Emit;

namespace FastReflection
{
    /// <summary>
    /// The Members of one closed type at one visibility.
    /// A name is case-sensitive and identifies at most one Member.
    /// The public and non-public type accessors of the same type are different, and each is cached.
    /// A target must be non-null, and its runtime type must be this type, a subtype, or an implementation of it.
    /// A value type requires a box.
    /// </summary>
    public abstract class TypeAccessor
    {
        /// <summary>
        /// The member set a type accessor may discover over its type: instance Members,
        /// the bound-call domain (methods, indexers, constructors), and inherited interfaces.
        /// Mirrored by the ObjectReader satellite's private copy (no InternalsVisibleTo) — keep in sync.
        /// </summary>
        internal const DynamicallyAccessedMemberTypes DiscoveryMemberTypes =
            DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties
            | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields
            | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods
            | DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors
            | DynamicallyAccessedMemberTypes.Interfaces;

        internal TypeAccessor() { }

        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            [DynamicallyAccessedMembers(DiscoveryMemberTypes)]
            internal readonly Type Type;
            internal readonly bool NonPublic;

            internal CacheKey(
                [DynamicallyAccessedMembers(DiscoveryMemberTypes)]
                Type type, bool nonPublic)
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
        /// Gets the type accessor for <paramref name="type"/> at one visibility.
        /// <paramref name="allowNonPublicAccessors"/> selects the non-public type accessor; otherwise the public one.
        /// An open generic type has no type accessor.
        /// </summary>
        /// <param name="type">The closed type to access.</param>
        /// <param name="allowNonPublicAccessors">True for the non-public type accessor.</param>
        /// <returns>The cached type accessor.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="type"/> is an open generic.</exception>
        public static TypeAccessor Create(
            [DynamicallyAccessedMembers(DiscoveryMemberTypes)]
            Type type, bool allowNonPublicAccessors = false)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (type.ContainsGenericParameters)
                throw new ArgumentException("Open generic types are not supported; close the generic type first.", nameof(type));
            return s_cache.GetOrAdd(
                new CacheKey(type, allowNonPublicAccessors),
                static k => new RuntimeTypeAccessor(k.Type, k.NonPublic));
        }

        /// <summary>The closed type this type accessor was built for.</summary>
        public abstract Type Type { get; }

        /// <summary>Reads or writes the Member with this case-sensitive name on a compatible target.</summary>
        /// <param name="target">The object to read or write; must be non-null with a compatible runtime type.</param>
        /// <param name="name">The case-sensitive Member name.</param>
        /// <value>The value to write.</value>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null — or <paramref name="target"/> is null while the Member is an instance member.</exception>
        /// <exception cref="MissingMemberException">The name is not a Member of this type accessor.</exception>
        /// <exception cref="InvalidOperationException">The Member exists but cannot be read (get) or written (set). An existing Member that cannot be written is not missing.</exception>
        /// <exception cref="InvalidCastException">The target's runtime type is not this type, a subtype, or an implementation of it — or, on set, the value cannot be converted to the Member type.</exception>
        /// <remarks>A Member exception propagates unwrapped.</remarks>
        public abstract object? this[object? target, string name] { get; set; }

        /// <summary>Reads the Member at <paramref name="ordinal"/> in this type accessor. An ordinal is not a name and not a durable identity.</summary>
        /// <param name="target">The object to read; must be non-null with a compatible runtime type.</param>
        /// <param name="ordinal">The Member's index in this type accessor.</param>
        /// <returns>The Member value.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="ordinal"/> is outside this type accessor.</exception>
        /// <exception cref="InvalidOperationException">The Member cannot be read.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is null while the Member is an instance member.</exception>
        /// <exception cref="InvalidCastException">The target's runtime type does not match. A Member exception propagates unwrapped.</exception>
        public abstract object? GetValue(object? target, int ordinal);

        /// <summary>Writes the Member at <paramref name="ordinal"/> in this type accessor.</summary>
        /// <param name="target">The object to write; must be non-null with a compatible runtime type.</param>
        /// <param name="ordinal">The Member's index in this type accessor.</param>
        /// <param name="value">The value to write; converted by the CLR cast rule only — no numeric widening or narrowing.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="ordinal"/> is outside this type accessor.</exception>
        /// <exception cref="InvalidOperationException">The Member cannot be written.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is null while the Member is an instance member.</exception>
        /// <exception cref="InvalidCastException">The target's runtime type does not match, or the value cannot be converted to the Member type. A Member exception propagates unwrapped.</exception>
        public abstract void SetValue(object? target, int ordinal, object? value);

        /// <summary>Maps a case-sensitive Member name to its ordinal in this type accessor.</summary>
        /// <param name="name">The Member name.</param>
        /// <param name="ordinal">The Member's index when found.</param>
        /// <returns>False on a miss; a miss is not an exception.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
        public abstract bool TryGetOrdinal(string name, out int ordinal);

        /// <summary>The Members of this type accessor: instance properties and fields. Not methods, events, indexers, constants, or static properties and fields.</summary>
        /// <returns>The Members in ordinal order: Members carrying <see cref="OrdinalAttribute"/> come first, ascending by its value; Members without it follow; equal values keep enumeration order.</returns>
        public abstract IReadOnlyList<Member> GetMembers();

        /// <summary>Whether <see cref="CreateNew()"/> can produce an instance. Instance creation is not a Member.</summary>
        public abstract bool CreateNewSupported { get; }

        /// <summary>Produces an instance via a parameterless constructor, or the default value of a value type. The constructor call is an access; a value type's default value is not. Not a Member of this type accessor.</summary>
        /// <returns>The new instance.</returns>
        /// <exception cref="InvalidOperationException"><see cref="CreateNewSupported"/> is false.</exception>
        /// <remarks>A constructor exception propagates unwrapped.</remarks>
        public abstract object CreateNew();

        // ============================== bound calls ==============================
        // A bound call is a name lookup plus an argument signature resolved once, then run as an
        // adapted invoker through the same emit/reflection machinery — it is not an access and not
        // a Member. The resolver's sanctioned conversions are identity, reference assignment,
        // unboxing, Nullable<T>, enum↔underlying, and spec §10.2.3 primitive widening (including
        // →decimal, excluding byte→char); null applies only to nullable targets; names are
        // case-sensitive; user-defined conversions, type inference, const narrowing, and
        // extension-method search never apply. Named arguments reorder by parameter name,
        // params binds in normal or expanded form, optional parameters fill (a candidate needing
        // no substitution beats one that fills), and explicit generic type arguments bind with
        // constraint checks. The bound-invoker cache lives on this accessor, keyed by
        // (member kind, name, normalized signature); growth is bounded only by call-site variety.

        /// <summary>Invokes the method with this case-sensitive name, resolving overloads against the supplied arguments.</summary>
        /// <param name="target">The object to call; must be compatible with the resolved member — ignored when it is static.</param>
        /// <param name="name">The case-sensitive method name, including accessor spellings such as <c>get_Item</c>.</param>
        /// <param name="args">The positional arguments; must not be null.</param>
        /// <returns>The return value, boxed; null for a void method.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null — or <paramref name="args"/> is null — or <paramref name="target"/> is null on an instance method.</exception>
        /// <exception cref="MissingMemberException">No method with this name exists at this accessor's visibility.</exception>
        /// <exception cref="MissingMethodException">Candidates exist but none is applicable to the arguments.</exception>
        /// <exception cref="AmbiguousMatchException">The best match is a tie between applicable overloads.</exception>
        /// <exception cref="InvalidCastException">The target's runtime type does not match, or an argument cannot be converted by the sanctioned set. A callee exception propagates unwrapped; <see langword="ref"/>/<see langword="out"/> values are not written back.</exception>
        public abstract object? Invoke(object? target, string name, params object?[]? args);

        /// <summary>Invokes the method with this case-sensitive name, with named arguments and explicit generic type arguments.</summary>
        /// <param name="target">The object to call; must be compatible with the resolved member — ignored when it is static.</param>
        /// <param name="name">The case-sensitive method name.</param>
        /// <param name="args">The positional arguments; named arguments fill parameters they do not cover.</param>
        /// <param name="namedArgs">Named arguments matched to parameter names, case-sensitively; an unmatched name makes the candidate inapplicable.</param>
        /// <param name="typeArguments">Explicit type arguments for a generic method definition, with constraint checks; generic type inference never applies.</param>
        /// <returns>The return value, boxed; null for a void method.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null — or <paramref name="args"/> is null — or <paramref name="target"/> is null on an instance method.</exception>
        /// <exception cref="MissingMemberException">No method with this name exists at this accessor's visibility.</exception>
        /// <exception cref="MissingMethodException">Candidates exist but none is applicable, including an unmatched named argument or bad arity.</exception>
        /// <exception cref="AmbiguousMatchException">The best match is a tie between applicable overloads.</exception>
        /// <exception cref="InvalidCastException">The target's runtime type does not match, or an argument cannot be converted by the sanctioned set. A callee exception propagates unwrapped; <see langword="ref"/>/<see langword="out"/> values are not written back.</exception>
        public abstract object? Invoke(object? target, string name, object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs, Type[]? typeArguments);

        /// <summary><see cref="Invoke(object?, string, object?[])"/> that returns false on a name miss or a failed bind instead of throwing. Every later stage — null <paramref name="args"/>, target rules, conversion, callee — propagates unchanged.</summary>
        /// <param name="target">The object to call; ignored when the resolved member is static.</param>
        /// <param name="name">The case-sensitive method name.</param>
        /// <param name="args">The positional arguments.</param>
        /// <param name="result">The return value, boxed; null when the bind fails.</param>
        /// <returns>False when the name is null or absent, no candidate is applicable, or the best match is a tie; true on a completed call.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="args"/> is null — or <paramref name="target"/> is null on an instance method.</exception>
        /// <exception cref="InvalidCastException">The target's runtime type does not match, or an argument cannot be converted. A callee exception propagates unwrapped.</exception>
        public abstract bool TryInvoke(object? target, string name, object?[]? args, out object? result);

        /// <summary><see cref="Invoke(object?, string, object?[], IReadOnlyDictionary{string, object?}, Type[])"/> that returns false on a name miss or a failed bind instead of throwing.</summary>
        /// <param name="target">The object to call; ignored when the resolved member is static.</param>
        /// <param name="name">The case-sensitive method name.</param>
        /// <param name="args">The positional arguments.</param>
        /// <param name="namedArgs">Named arguments matched to parameter names, case-sensitively.</param>
        /// <param name="typeArguments">Explicit type arguments for a generic method definition.</param>
        /// <param name="result">The return value, boxed; null when the bind fails.</param>
        /// <returns>False when the name is null or absent, no candidate is applicable, or the best match is a tie; true on a completed call.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="args"/> is null — or <paramref name="target"/> is null on an instance method.</exception>
        /// <exception cref="InvalidCastException">The target's runtime type does not match, or an argument cannot be converted. A callee exception propagates unwrapped.</exception>
        public abstract bool TryInvoke(object? target, string name, object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs, Type[]? typeArguments, out object? result);

        /// <summary>Reads the property or field with this case-sensitive name — instance or static, indexer included — resolved against the supplied index arguments.</summary>
        /// <param name="target">The object to read; ignored when the resolved member is static.</param>
        /// <param name="name">The case-sensitive property or field name; the indexer binds under its item name (typically <c>Item</c>).</param>
        /// <param name="index">The index arguments for an indexer; empty for an ordinary member; must not be null.</param>
        /// <returns>The member value.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null — or <paramref name="index"/> is null — or <paramref name="target"/> is null on an instance member.</exception>
        /// <exception cref="MissingMemberException">No property or field with this name exists at this accessor's visibility.</exception>
        /// <exception cref="MissingMethodException">The name exists but no getter is applicable, including bad index arity.</exception>
        /// <exception cref="AmbiguousMatchException">The best match is a tie between applicable indexer overloads.</exception>
        /// <exception cref="InvalidCastException">The target's runtime type does not match, or an index argument cannot be converted. A getter exception propagates unwrapped.</exception>
        /// <remarks>This is a bound call, not a Member read: the domain adds statics and indexers to the instance Member domain, and arguments convert by the sanctioned set.</remarks>
        public abstract object? GetValue(object? target, string name, params object?[]? index);

        /// <summary>Writes the property or field with this case-sensitive name — instance or static, indexer included — resolved against the value and supplied index arguments.</summary>
        /// <param name="target">The object to write; ignored when the resolved member is static.</param>
        /// <param name="name">The case-sensitive property or field name; the indexer binds under its item name.</param>
        /// <param name="value">The value to write; converted by the sanctioned set.</param>
        /// <param name="index">The index arguments for an indexer; empty for an ordinary member; must not be null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null — or <paramref name="index"/> is null — or <paramref name="target"/> is null on an instance member.</exception>
        /// <exception cref="MissingMemberException">No property or field with this name exists at this accessor's visibility.</exception>
        /// <exception cref="MissingMethodException">The name exists but no setter is applicable — including a get-only property and bad index arity.</exception>
        /// <exception cref="AmbiguousMatchException">The best match is a tie between applicable indexer overloads.</exception>
        /// <exception cref="InvalidCastException">The target's runtime type does not match, or an argument cannot be converted. A setter exception propagates unwrapped.</exception>
        /// <remarks>A bound call, not a Member write: boxed value-type writes persist through the box per ADR-0001.</remarks>
        public abstract void SetValue(object? target, string name, object? value, params object?[]? index);

        /// <summary><see cref="GetValue(object?, string, object?[])"/> that returns false on a name miss or a failed bind instead of throwing.</summary>
        /// <param name="target">The object to read; ignored when the resolved member is static.</param>
        /// <param name="name">The case-sensitive property or field name.</param>
        /// <param name="index">The index arguments for an indexer.</param>
        /// <param name="value">The member value; null when the bind fails.</param>
        /// <returns>False when the name is null or absent, no getter is applicable, or the best match is a tie.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="index"/> is null — or <paramref name="target"/> is null on an instance member.</exception>
        /// <exception cref="InvalidCastException">The target's runtime type does not match, or an index argument cannot be converted. A getter exception propagates unwrapped.</exception>
        public abstract bool TryGetValue(object? target, string name, object?[]? index, out object? value);

        /// <summary><see cref="SetValue(object?, string, object?, object?[])"/> that returns false on a name miss or a failed bind instead of throwing.</summary>
        /// <param name="target">The object to write; ignored when the resolved member is static.</param>
        /// <param name="name">The case-sensitive property or field name.</param>
        /// <param name="value">The value to write; converted by the sanctioned set.</param>
        /// <param name="index">The index arguments for an indexer.</param>
        /// <returns>False when the name is null or absent, no setter is applicable, or the best match is a tie.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="index"/> is null — or <paramref name="target"/> is null on an instance member.</exception>
        /// <exception cref="InvalidCastException">The target's runtime type does not match, or an argument cannot be converted. A setter exception propagates unwrapped.</exception>
        public abstract bool TrySetValue(object? target, string name, object? value, object?[]? index);

        /// <summary>Produces an instance by resolving a constructor against the supplied arguments. A value type with no applicable declared constructor yields its default value.</summary>
        /// <param name="args">The positional arguments; must not be null.</param>
        /// <param name="namedArgs">Named arguments matched to parameter names, case-sensitively.</param>
        /// <returns>The new instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="args"/> is null.</exception>
        /// <exception cref="MissingMethodException">No declared constructor is applicable, including an unmatched named argument or bad arity.</exception>
        /// <exception cref="AmbiguousMatchException">The best match is a tie between applicable constructors.</exception>
        /// <exception cref="InvalidCastException">An argument cannot be converted by the sanctioned set. A constructor exception propagates unwrapped.</exception>
        /// <remarks><see cref="CreateNewSupported"/> keeps its parameterless meaning; this overload does not change it.</remarks>
        public abstract object CreateNew(object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs = null);

        /// <summary><see cref="CreateNew(object?[], IReadOnlyDictionary{string, object?})"/> that returns false on a failed bind instead of throwing.</summary>
        /// <param name="args">The positional arguments.</param>
        /// <param name="namedArgs">Named arguments matched to parameter names, case-sensitively.</param>
        /// <param name="result">The new instance; null when the bind fails.</param>
        /// <returns>False when no declared constructor is applicable or the best match is a tie.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="args"/> is null.</exception>
        /// <exception cref="InvalidCastException">An argument cannot be converted. A constructor exception propagates unwrapped.</exception>
        public abstract bool TryCreateNew(object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs, out object? result);

        /// <summary>The distinct case-sensitive names bindable at this accessor's visibility: methods, properties, fields, and the indexer item name. Accessor <c>get_</c>/<c>set_</c> spellings and constructors are not listed; overloads collapse to one entry.</summary>
        /// <returns>The bindable names, in enumeration order.</returns>
        public abstract IReadOnlyList<string> GetMemberNames();
    }

    internal sealed class RuntimeTypeAccessor : TypeAccessor
    {
        private struct Entry
        {
            internal Func<object?, object?>? Getter;
            internal Action<object?, object?>? Setter;
        }

        private struct PendingMember
        {
            internal MemberInfo Info;
            internal Type MemberType;
            internal bool CanRead;
            internal bool CanWrite;
            internal Entry Entry;
            internal OrdinalAttribute? Key;
        }

        [DynamicallyAccessedMembers(TypeAccessor.DiscoveryMemberTypes)]
        private readonly Type _type;
        private readonly bool _nonPublic;
        private readonly Dictionary<string, int> _map;
        private readonly Entry[] _entries;
        private readonly Member[] _members;
        private readonly Func<object?>? _factory;
        private readonly ConcurrentDictionary<BoundSignature, BoundCall> _bound = new();
        private BoundTables? _tables;

        [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = EmitHelper.LevelEnumerationJustification)]
        [UnconditionalSuppressMessage("Trimming", "IL2065", Justification = EmitHelper.LevelEnumerationJustification)]
        internal RuntimeTypeAccessor(
            [DynamicallyAccessedMembers(TypeAccessor.DiscoveryMemberTypes)]
            Type type, bool allowNonPublicAccessors)
        {
            _type = type;
            _nonPublic = allowNonPublicAccessors;

            var flags = BindingFlags.Instance | BindingFlags.Public;
            if (allowNonPublicAccessors) flags |= BindingFlags.NonPublic;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new List<PendingMember>();

            foreach (var t in CollectMemberTypes(type))
            {
                foreach (var prop in t.GetProperties(flags))
                {
                    if (prop.GetIndexParameters().Length != 0) continue; // indexers: use CreateInvoker on the accessor
                    var get = prop.GetGetMethod(allowNonPublicAccessors);
                    var set = prop.GetSetMethod(allowNonPublicAccessors);
                    if (get == null && set == null) continue;
                    if (!seen.Add(prop.Name)) continue; // inherited-interface duplicates

                    pending.Add(new PendingMember
                    {
                        Info = prop,
                        MemberType = prop.PropertyType,
                        CanRead = get != null,
                        CanWrite = set != null,
                        Entry = new Entry
                        {
                            Getter = TryCreateGetter(prop, get),
                            Setter = TryCreateSetter(prop, set)
                        },
                        Key = ReadOrdinalKey(prop)
                    });
                }

                foreach (var field in t.GetFields(flags))
                {
                    if (field.IsLiteral || !seen.Add(field.Name)) continue;

                    var writable = !field.IsInitOnly;
                    pending.Add(new PendingMember
                    {
                        Info = field,
                        MemberType = field.FieldType,
                        CanRead = true,
                        CanWrite = writable,
                        Entry = new Entry
                        {
                            Getter = TryCreateGetter(field),
                            Setter = writable ? TryCreateSetter(field) : null
                        },
                        Key = ReadOrdinalKey(field)
                    });
                }
            }

            _map = new Dictionary<string, int>(StringComparer.Ordinal);
            _entries = new Entry[pending.Count];
            _members = new Member[pending.Count];

            // Stable sort: attributed Members first, ascending; LINQ OrderBy keeps ties in enumeration order.
            var ordinal = 0;
            foreach (var candidate in pending
                .OrderBy(r => r.Key == null ? 1 : 0)
                .ThenBy(r => r.Key?.Value ?? 0))
            {
                _entries[ordinal] = candidate.Entry;
                _members[ordinal] = new Member(candidate.Info, candidate.MemberType, ordinal, candidate.CanRead, candidate.CanWrite);
                _map[candidate.Info.Name] = ordinal;
                ordinal++;
            }

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
                ?? throw new InvalidOperationException(
                    $"Member '{_members[ordinal].Name}' of '{_type}' cannot be read by this type accessor.");
            return getter(target);
        }

        public override void SetValue(object? target, int ordinal, object? value)
        {
            if ((uint)ordinal >= (uint)_entries.Length)
                throw new ArgumentOutOfRangeException(nameof(ordinal));
            var setter = _entries[ordinal].Setter
                ?? throw new InvalidOperationException(
                    $"Member '{_members[ordinal].Name}' of '{_type}' cannot be written by this type accessor.");
            setter(target, value);
        }

        public override bool TryGetOrdinal(string name, out int ordinal)
            => _map.TryGetValue(name, out ordinal);

        public override IReadOnlyList<Member> GetMembers() => _members;

        public override bool CreateNewSupported => _factory != null;

        public override object CreateNew()
            => _factory?.Invoke()
               ?? throw new InvalidOperationException($"Type '{_type}' has no parameterless constructor.");

        // ============================== bound calls ==============================

        public override object? Invoke(object? target, string name, params object?[]? args)
            => BoundInvoke(target, name, args, null, null);

        public override object? Invoke(object? target, string name, object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs, Type[]? typeArguments)
            => BoundInvoke(target, name, args, namedArgs, typeArguments);

        public override bool TryInvoke(object? target, string name, object?[]? args, out object? result)
            => TryBoundInvoke(target, name, args, null, null, out result);

        public override bool TryInvoke(object? target, string name, object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs, Type[]? typeArguments, out object? result)
            => TryBoundInvoke(target, name, args, namedArgs, typeArguments, out result);

        public override object? GetValue(object? target, string name, params object?[]? index)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            var call = GetBoundCall(new BoundSignature(BoundKind.Get, name, SigTypes(index), null, null, null));
            return call.Run(target, index, null);
        }

        public override void SetValue(object? target, string name, object? value, params object?[]? index)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            var combined = CombinedArgs(index, value);
            var call = GetBoundCall(new BoundSignature(BoundKind.Set, name, SigTypes(combined), null, null, null));
            call.Run(target, index, combined, null);
        }

        public override bool TryGetValue(object? target, string name, object?[]? index, out object? value)
        {
            value = null;
            if (name == null) return false;
            BoundCall call;
            try
            {
                call = GetBoundCall(new BoundSignature(BoundKind.Get, name, SigTypes(index), null, null, null));
            }
            catch (Exception e) when (IsBindFailure(e))
            {
                return false;
            }
            value = call.Run(target, index, null);
            return true;
        }

        public override bool TrySetValue(object? target, string name, object? value, object?[]? index)
        {
            if (name == null) return false;
            var combined = CombinedArgs(index, value);
            BoundCall call;
            try
            {
                call = GetBoundCall(new BoundSignature(BoundKind.Set, name, SigTypes(combined), null, null, null));
            }
            catch (Exception e) when (IsBindFailure(e))
            {
                return false;
            }
            call.Run(target, index, combined, null);
            return true;
        }

        public override object CreateNew(object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs = null)
        {
            SplitNamed(namedArgs, out var names, out var namedTypes, out var namedValues);
            var call = GetBoundCall(new BoundSignature(BoundKind.Ctor, string.Empty, SigTypes(args), names, namedTypes, null));
            return call.Run(null, args, namedValues)!;
        }

        public override bool TryCreateNew(object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs, out object? result)
        {
            result = null;
            BoundCall call;
            object?[]? namedValues;
            try
            {
                SplitNamed(namedArgs, out var names, out var namedTypes, out namedValues);
                call = GetBoundCall(new BoundSignature(BoundKind.Ctor, string.Empty, SigTypes(args), names, namedTypes, null));
            }
            catch (Exception e) when (IsBindFailure(e))
            {
                return false;
            }
            result = call.Run(null, args, namedValues);
            return true;
        }

        public override IReadOnlyList<string> GetMemberNames() => Tables.MemberNames;

        private object? BoundInvoke(object? target, string name, object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs, Type[]? typeArguments)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            SplitNamed(namedArgs, out var names, out var namedTypes, out var namedValues);
            var call = GetBoundCall(new BoundSignature(BoundKind.Invoke, name, SigTypes(args), names, namedTypes, typeArguments));
            return call.Run(target, args, namedValues);
        }

        private bool TryBoundInvoke(object? target, string name, object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs, Type[]? typeArguments, out object? result)
        {
            result = null;
            if (name == null) return false;
            BoundCall call;
            object?[]? namedValues;
            try
            {
                SplitNamed(namedArgs, out var names, out var namedTypes, out namedValues);
                call = GetBoundCall(new BoundSignature(BoundKind.Invoke, name, SigTypes(args), names, namedTypes, typeArguments));
            }
            catch (Exception e) when (IsBindFailure(e))
            {
                return false;
            }
            result = call.Run(target, args, namedValues);
            return true;
        }

        // Stages 1–2 of the bound-call trigger order — the only failures Try* swallows.
        // MissingMethodException is a MissingMemberException subclass; both name the bind-miss set.
        private static bool IsBindFailure(Exception e)
            => e is MissingMemberException or AmbiguousMatchException;

        private BoundTables Tables => _tables ??= BoundTables.Build(_type, _nonPublic);

        private BoundCall GetBoundCall(in BoundSignature signature)
        {
            if (_bound.TryGetValue(signature, out var call)) return call;
            return _bound.GetOrAdd(signature, ResolveBound);
        }

        private BoundCall ResolveBound(BoundSignature signature)
        {
            var tables = Tables;
            switch (signature.Kind)
            {
                case BoundKind.Invoke:
                    return BoundResolver.ResolveInvoke(tables, signature);
                case BoundKind.Get:
                    return BoundResolver.ResolveGetSet(tables, false, signature, _nonPublic);
                case BoundKind.Set:
                    return BoundResolver.ResolveGetSet(tables, true, signature, _nonPublic);
                default:
                    return BoundResolver.ResolveCtor(tables, signature);
            }
        }

        /// <summary>The setter's argument array: index arguments plus the trailing value.</summary>
        private static object?[] CombinedArgs(object?[]? index, object? value)
        {
            var combined = new object?[(index?.Length ?? 0) + 1];
            if (index != null) Array.Copy(index, combined, index.Length);
            combined[combined.Length - 1] = value;
            return combined;
        }

        /// <summary>Runtime argument types for the signature key; a null argument contributes a null slot.</summary>
        private static Type?[]? SigTypes(object?[]? args)
        {
            if (args == null) return null;
            var types = new Type?[args.Length];
            for (var i = 0; i < args.Length; i++)
                types[i] = args[i]?.GetType();
            return types;
        }

        /// <summary>Splits a named-argument dictionary into the signature's names/types and the call's value array, all in supplied order.</summary>
        private static void SplitNamed(IReadOnlyDictionary<string, object?>? namedArgs, out string[]? names, out Type?[]? types, out object?[]? values)
        {
            names = null;
            types = null;
            values = null;
            if (namedArgs == null) return;

            names = new string[namedArgs.Count];
            types = new Type?[namedArgs.Count];
            values = new object?[namedArgs.Count];
            var i = 0;
            foreach (var kv in namedArgs)
            {
                names[i] = kv.Key;
                types[i] = kv.Value?.GetType();
                values[i] = kv.Value;
                i++;
            }
        }

        private int RequireOrdinal(string name)
            => _map.TryGetValue(name, out var ordinal)
                ? ordinal
                : throw new MissingMemberException(_type.FullName, name);

        private static OrdinalAttribute? ReadOrdinalKey(MemberInfo member)
            => (OrdinalAttribute?)Attribute.GetCustomAttribute(member, typeof(OrdinalAttribute), inherit: true);

        private static Type[] CollectMemberTypes(
            [DynamicallyAccessedMembers(TypeAccessor.DiscoveryMemberTypes)]
            Type type)
        {
            if (!type.IsInterface) return new[] { type };
            var list = new List<Type> { type };
            list.AddRange(type.GetInterfaces());
            return list.ToArray();
        }

        private static Func<object?>? TryCreateFactory(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
            Type type, bool nonPublic)
        {
            try { return AccessorEmitter.CreateFactory(type, nonPublic); }
            catch (ArgumentException) { return null; }
        }

        // Emit first; fall back to plain reflection when dynamic code is unavailable.

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = AccessorEmitter.EmitSiteJustification)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = AccessorEmitter.EmitSiteJustification)]
        private static Func<object?, object?>? TryCreateGetter(PropertyInfo prop, MethodInfo? get)
        {
            if (get == null) return null;
            if (!EmitHelper.DynamicCodeSupported) return target => ReflectionFallback.Get(get, target);
            return EmitHelper.EmitOrFallback(
                () => AccessorEmitter.EmitPropertyGetter(prop, get),
                (Func<object?, object?>)(target => ReflectionFallback.Get(get, target)));
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = AccessorEmitter.EmitSiteJustification)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = AccessorEmitter.EmitSiteJustification)]
        private static Action<object?, object?>? TryCreateSetter(PropertyInfo prop, MethodInfo? set)
        {
            if (set == null) return null;
            if (!EmitHelper.DynamicCodeSupported) return (target, value) => ReflectionFallback.Set(set, target, value);
            return EmitHelper.EmitOrFallback(
                () => AccessorEmitter.EmitPropertySetter(prop, set),
                (Action<object?, object?>)((target, value) => ReflectionFallback.Set(set, target, value)));
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = AccessorEmitter.EmitSiteJustification)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = AccessorEmitter.EmitSiteJustification)]
        private static Func<object?, object?>? TryCreateGetter(FieldInfo field)
        {
            if (!EmitHelper.DynamicCodeSupported) return target => ReflectionFallback.Get(field, target);
            return EmitHelper.EmitOrFallback(
                () => AccessorEmitter.EmitFieldGetter(field),
                (Func<object?, object?>)(target => ReflectionFallback.Get(field, target)));
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = AccessorEmitter.EmitSiteJustification)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = AccessorEmitter.EmitSiteJustification)]
        private static Action<object?, object?>? TryCreateSetter(FieldInfo field)
        {
            if (!EmitHelper.DynamicCodeSupported) return (target, value) => ReflectionFallback.Set(field, target, value);
            return EmitHelper.EmitOrFallback(
                () => AccessorEmitter.EmitFieldSetter(field),
                (Action<object?, object?>)((target, value) => ReflectionFallback.Set(field, target, value)));
        }
    }
}
