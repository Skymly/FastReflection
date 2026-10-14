namespace FastReflection
{
    /// <summary>
    /// An access over a method: <c>(target, args) => result</c>.
    /// A static method has no target. <paramref name="args"/> may be <see langword="null"/> when the method has no parameters.
    /// <see langword="ref"/>/<see langword="out"/> values are written back into <paramref name="args"/> and boxed.
    /// A <see langword="ref"/> return is the current value, boxed, not a live reference.
    /// </summary>
    public delegate object? FastInvoker(object? target, object?[]? args);

    /// <summary>
    /// An access over a constructor: <c>args => instance</c>.
    /// <paramref name="args"/> may be <see langword="null"/> when the constructor has no parameters.
    /// </summary>
    public delegate object? FastConstructorInvoker(object?[]? args);

#if NET8_0_OR_GREATER
    /// <summary>
    /// An access over a method that uses the caller's <see cref="Span{T}"/> of <see cref="object"/>.
    /// A span of references cannot be <c>stackalloc</c>'d.
    /// Value-type arguments are still boxed. A <see langword="ref"/> return is the current value, then boxed.
    /// Falls back to <see cref="System.Reflection.MethodInvoker"/> when dynamic code is unavailable.
    /// </summary>
    public delegate object? FastSpanInvoker(object? target, Span<object?> args);

    /// <summary>
    /// An access over a constructor that uses the caller's <see cref="Span{T}"/> of <see cref="object"/>.
    /// Falls back to <see cref="System.Reflection.ConstructorInvoker"/> when dynamic code is unavailable.
    /// </summary>
    public delegate object? FastSpanConstructorInvoker(Span<object?> args);
#endif
}
