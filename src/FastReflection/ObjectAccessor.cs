using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace FastReflection
{
    /// <summary>
    /// A <see cref="TypeAccessor"/> of one object's runtime type, at one visibility, bound to that object.
    /// Not a type accessor of the variable's declared type.
    /// </summary>
    public abstract class ObjectAccessor
    {
        internal ObjectAccessor() { }

        /// <summary>Reads or writes the Member with this case-sensitive name on the bound object.</summary>
        /// <param name="name">The case-sensitive Member name.</param>
        /// <value>The value to write.</value>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
        /// <exception cref="MissingMemberException">The name is not a Member of this type accessor.</exception>
        /// <exception cref="InvalidOperationException">The Member exists but cannot be read (get) or written (set). An existing Member that cannot be written is not missing.</exception>
        /// <exception cref="InvalidCastException">On set, the value cannot be converted to the Member type. A Member exception propagates unwrapped.</exception>
        public abstract object? this[string name] { get; set; }

        /// <summary>The wrapped object.</summary>
        public abstract object? Target { get; }

        /// <summary>Binds <paramref name="target"/> to the type accessor of its runtime type. <paramref name="allowNonPublicAccessors"/> selects the non-public type accessor.</summary>
        /// <param name="target">The object to wrap. Its runtime type must keep the same member set <see cref="TypeAccessor.Create"/> requires; the requirement is contractual because an <see cref="object"/> parameter cannot carry it.</param>
        /// <param name="allowNonPublicAccessors">True for the non-public type accessor.</param>
        /// <returns>The bound object accessor.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
        /// <exception cref="ArgumentException">The target's runtime type is an open generic.</exception>
        [UnconditionalSuppressMessage("Trimming", "IL2072",
            Justification = "The caller's target runtime type must keep the TypeAccessor.Create discovery union; DynamicallyAccessedMembers cannot be applied to an object parameter, so the requirement is contractual (IL2098).")]
        public static ObjectAccessor Create(object target, bool allowNonPublicAccessors = false)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var accessor = TypeAccessor.Create(target.GetType(), allowNonPublicAccessors);
            return new TypeAccessorAdapter(target, accessor);
        }

        // ============================== bound calls ==============================
        // The bound-call surface of the underlying type accessor, with the wrapped object as the
        // target — same resolver, trigger order, and bound-invoker cache as TypeAccessor.

        /// <summary>Invokes the method with this case-sensitive name on the wrapped object, resolving overloads against the supplied arguments. A static member binds target-free.</summary>
        /// <param name="name">The case-sensitive method name.</param>
        /// <param name="args">The positional arguments; must not be null.</param>
        /// <returns>The return value, boxed; null for a void method.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null — or <paramref name="args"/> is null.</exception>
        /// <exception cref="MissingMemberException">No method with this name exists at this accessor's visibility.</exception>
        /// <exception cref="MissingMethodException">Candidates exist but none is applicable to the arguments.</exception>
        /// <exception cref="AmbiguousMatchException">The best match is a tie between applicable overloads.</exception>
        /// <exception cref="InvalidCastException">An argument cannot be converted by the sanctioned set. A callee exception propagates unwrapped; <see langword="ref"/>/<see langword="out"/> values are not written back.</exception>
        public abstract object? Invoke(string name, params object?[]? args);

        /// <summary>Invokes the method with this case-sensitive name on the wrapped object, with named arguments and explicit generic type arguments.</summary>
        /// <param name="name">The case-sensitive method name.</param>
        /// <param name="args">The positional arguments; named arguments fill parameters they do not cover.</param>
        /// <param name="namedArgs">Named arguments matched to parameter names, case-sensitively; an unmatched name makes the candidate inapplicable.</param>
        /// <param name="typeArguments">Explicit type arguments for a generic method definition, with constraint checks; generic type inference never applies.</param>
        /// <returns>The return value, boxed; null for a void method.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null — or <paramref name="args"/> is null.</exception>
        /// <exception cref="MissingMemberException">No method with this name exists at this accessor's visibility.</exception>
        /// <exception cref="MissingMethodException">Candidates exist but none is applicable, including an unmatched named argument or bad arity.</exception>
        /// <exception cref="AmbiguousMatchException">The best match is a tie between applicable overloads.</exception>
        /// <exception cref="InvalidCastException">An argument cannot be converted by the sanctioned set. A callee exception propagates unwrapped.</exception>
        public abstract object? Invoke(string name, object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs, Type[]? typeArguments);

        /// <summary><see cref="Invoke(string, object?[])"/> that returns false on a name miss or a failed bind instead of throwing. Every later stage propagates unchanged.</summary>
        /// <param name="name">The case-sensitive method name.</param>
        /// <param name="args">The positional arguments.</param>
        /// <param name="result">The return value, boxed; null when the bind fails.</param>
        /// <returns>False when the name is null or absent, no candidate is applicable, or the best match is a tie.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="args"/> is null.</exception>
        /// <exception cref="InvalidCastException">An argument cannot be converted. A callee exception propagates unwrapped.</exception>
        public abstract bool TryInvoke(string name, object?[]? args, out object? result);

        /// <summary><see cref="Invoke(string, object?[], IReadOnlyDictionary{string, object?}, Type[])"/> that returns false on a name miss or a failed bind instead of throwing.</summary>
        /// <param name="name">The case-sensitive method name.</param>
        /// <param name="args">The positional arguments.</param>
        /// <param name="namedArgs">Named arguments matched to parameter names, case-sensitively.</param>
        /// <param name="typeArguments">Explicit type arguments for a generic method definition.</param>
        /// <param name="result">The return value, boxed; null when the bind fails.</param>
        /// <returns>False when the name is null or absent, no candidate is applicable, or the best match is a tie.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="args"/> is null.</exception>
        /// <exception cref="InvalidCastException">An argument cannot be converted. A callee exception propagates unwrapped.</exception>
        public abstract bool TryInvoke(string name, object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs, Type[]? typeArguments, out object? result);

        /// <summary>Reads the property or field with this case-sensitive name on the wrapped object — instance or static, indexer included — resolved against the supplied index arguments.</summary>
        /// <param name="name">The case-sensitive property or field name; the indexer binds under its item name.</param>
        /// <param name="index">The index arguments for an indexer; empty for an ordinary member; must not be null.</param>
        /// <returns>The member value.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null — or <paramref name="index"/> is null.</exception>
        /// <exception cref="MissingMemberException">No property or field with this name exists at this accessor's visibility.</exception>
        /// <exception cref="MissingMethodException">The name exists but no getter is applicable, including bad index arity.</exception>
        /// <exception cref="AmbiguousMatchException">The best match is a tie between applicable indexer overloads.</exception>
        /// <exception cref="InvalidCastException">An index argument cannot be converted. A getter exception propagates unwrapped.</exception>
        public abstract object? GetValue(string name, params object?[]? index);

        /// <summary>Writes the property or field with this case-sensitive name on the wrapped object — instance or static, indexer included — resolved against the value and supplied index arguments.</summary>
        /// <param name="name">The case-sensitive property or field name; the indexer binds under its item name.</param>
        /// <param name="value">The value to write; converted by the sanctioned set.</param>
        /// <param name="index">The index arguments for an indexer; empty for an ordinary member; must not be null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null — or <paramref name="index"/> is null.</exception>
        /// <exception cref="MissingMemberException">No property or field with this name exists at this accessor's visibility.</exception>
        /// <exception cref="MissingMethodException">The name exists but no setter is applicable — including a get-only property and bad index arity.</exception>
        /// <exception cref="AmbiguousMatchException">The best match is a tie between applicable indexer overloads.</exception>
        /// <exception cref="InvalidCastException">An argument cannot be converted. A setter exception propagates unwrapped.</exception>
        public abstract void SetValue(string name, object? value, params object?[]? index);

        /// <summary><see cref="GetValue(string, object?[])"/> that returns false on a name miss or a failed bind instead of throwing.</summary>
        /// <param name="name">The case-sensitive property or field name.</param>
        /// <param name="index">The index arguments for an indexer.</param>
        /// <param name="value">The member value; null when the bind fails.</param>
        /// <returns>False when the name is null or absent, no getter is applicable, or the best match is a tie.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="index"/> is null.</exception>
        /// <exception cref="InvalidCastException">An index argument cannot be converted. A getter exception propagates unwrapped.</exception>
        public abstract bool TryGetValue(string name, object?[]? index, out object? value);

        /// <summary><see cref="SetValue(string, object?, object?[])"/> that returns false on a name miss or a failed bind instead of throwing.</summary>
        /// <param name="name">The case-sensitive property or field name.</param>
        /// <param name="value">The value to write; converted by the sanctioned set.</param>
        /// <param name="index">The index arguments for an indexer.</param>
        /// <returns>False when the name is null or absent, no setter is applicable, or the best match is a tie.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="index"/> is null.</exception>
        /// <exception cref="InvalidCastException">An argument cannot be converted. A setter exception propagates unwrapped.</exception>
        public abstract bool TrySetValue(string name, object? value, object?[]? index);

        /// <summary>Produces an instance of the wrapped object's runtime type by resolving a constructor against the supplied arguments.</summary>
        /// <param name="args">The positional arguments; must not be null.</param>
        /// <param name="namedArgs">Named arguments matched to parameter names, case-sensitively.</param>
        /// <returns>The new instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="args"/> is null.</exception>
        /// <exception cref="MissingMethodException">No declared constructor is applicable, including an unmatched named argument or bad arity.</exception>
        /// <exception cref="AmbiguousMatchException">The best match is a tie between applicable constructors.</exception>
        /// <exception cref="InvalidCastException">An argument cannot be converted by the sanctioned set. A constructor exception propagates unwrapped.</exception>
        public abstract object CreateNew(object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs = null);

        /// <summary><see cref="CreateNew(object?[], IReadOnlyDictionary{string, object?})"/> that returns false on a failed bind instead of throwing.</summary>
        /// <param name="args">The positional arguments.</param>
        /// <param name="namedArgs">Named arguments matched to parameter names, case-sensitively.</param>
        /// <param name="result">The new instance; null when the bind fails.</param>
        /// <returns>False when no declared constructor is applicable or the best match is a tie.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="args"/> is null.</exception>
        /// <exception cref="InvalidCastException">An argument cannot be converted. A constructor exception propagates unwrapped.</exception>
        public abstract bool TryCreateNew(object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs, out object? result);

        /// <summary>The distinct case-sensitive names bindable at this accessor's visibility: methods, properties, fields, and the indexer item name.</summary>
        /// <returns>The bindable names, in enumeration order.</returns>
        public abstract IReadOnlyList<string> GetMemberNames();

        /// <inheritdoc />
        public override bool Equals(object? obj)
            => obj is ObjectAccessor other && Equals(Target, other.Target);

        /// <inheritdoc />
        public override int GetHashCode() => Target?.GetHashCode() ?? 0;

        /// <inheritdoc />
        public override string? ToString() => Target?.ToString();
    }

    internal sealed class TypeAccessorAdapter : ObjectAccessor
    {
        private readonly object _target;
        private readonly TypeAccessor _accessor;

        internal TypeAccessorAdapter(object target, TypeAccessor accessor)
        {
            _target = target;
            _accessor = accessor;
        }

        public override object? Target => _target;

        public override object? this[string name]
        {
            get => _accessor[_target, name];
            set => _accessor[_target, name] = value;
        }

        public override object? Invoke(string name, params object?[]? args)
            => _accessor.Invoke(_target, name, args);

        public override object? Invoke(string name, object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs, Type[]? typeArguments)
            => _accessor.Invoke(_target, name, args, namedArgs, typeArguments);

        public override bool TryInvoke(string name, object?[]? args, out object? result)
            => _accessor.TryInvoke(_target, name, args, out result);

        public override bool TryInvoke(string name, object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs, Type[]? typeArguments, out object? result)
            => _accessor.TryInvoke(_target, name, args, namedArgs, typeArguments, out result);

        public override object? GetValue(string name, params object?[]? index)
            => _accessor.GetValue(_target, name, index);

        public override void SetValue(string name, object? value, params object?[]? index)
            => _accessor.SetValue(_target, name, value, index);

        public override bool TryGetValue(string name, object?[]? index, out object? value)
            => _accessor.TryGetValue(_target, name, index, out value);

        public override bool TrySetValue(string name, object? value, object?[]? index)
            => _accessor.TrySetValue(_target, name, value, index);

        public override object CreateNew(object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs = null)
            => _accessor.CreateNew(args, namedArgs);

        public override bool TryCreateNew(object?[]? args, IReadOnlyDictionary<string, object?>? namedArgs, out object? result)
            => _accessor.TryCreateNew(args, namedArgs, out result);

        public override IReadOnlyList<string> GetMemberNames()
            => _accessor.GetMemberNames();
    }
}
