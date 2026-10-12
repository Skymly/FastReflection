namespace FastReflection
{
    /// <summary>
    /// Per-instance member accessor: wraps a target object so callers read/write
    /// members by name without repeating the target. Backed by a cached <see cref="TypeAccessor"/>.
    /// </summary>
    public abstract class ObjectAccessor
    {
        /// <summary>Name-indexed member access on the wrapped target.</summary>
        /// <exception cref="MissingMemberException">Unknown member, or member not readable/writable.</exception>
        public abstract object? this[string name] { get; set; }

        /// <summary>The wrapped object.</summary>
        public abstract object? Target { get; }

        /// <summary>Creates an accessor for <paramref name="target"/>.</summary>
        public static ObjectAccessor Create(object target, bool allowNonPublicAccessors = false)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var accessor = TypeAccessor.Create(target.GetType(), allowNonPublicAccessors);
            return new TypeAccessorAdapter(target, accessor);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
            => obj is ObjectAccessor other ? Equals(Target, other.Target) : Equals(Target, obj);

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
