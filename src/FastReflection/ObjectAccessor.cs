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
        /// <param name="target">The object to wrap.</param>
        /// <param name="allowNonPublicAccessors">True for the non-public type accessor.</param>
        /// <returns>The bound object accessor.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
        /// <exception cref="ArgumentException">The target's runtime type is an open generic.</exception>
        public static ObjectAccessor Create(object target, bool allowNonPublicAccessors = false)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var accessor = TypeAccessor.Create(target.GetType(), allowNonPublicAccessors);
            return new TypeAccessorAdapter(target, accessor);
        }

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
    }
}
