namespace FastReflection
{
    /// <summary>
    /// An access over a method: <c>(target, args) => result</c>.
    /// A static method has no target. <paramref name="args"/> may be <see langword="null"/> when the method has no parameters.
    /// <see langword="ref"/>/<see langword="out"/> values are written back into <paramref name="args"/> and boxed.
    /// A <see langword="ref"/> return is the current value, boxed, not a live reference.
    /// </summary>
    /// <param name="target">The object to call; ignored on a static method.</param>
    /// <param name="args">The arguments, and the by-ref write-backs.</param>
    /// <returns>The return value, boxed; null for a void method.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null on an instance method, or <paramref name="args"/> is null when the method has parameters.</exception>
    /// <exception cref="ArgumentException"><paramref name="args"/> has the wrong length.</exception>
    /// <exception cref="InvalidCastException">The target's runtime type does not match, or an argument or by-ref write-back cannot be converted. A callee exception propagates unwrapped.</exception>
    public delegate object? FastInvoker(object? target, object?[]? args);

    /// <summary>
    /// An access over a constructor: <c>args => instance</c>.
    /// <paramref name="args"/> may be <see langword="null"/> when the constructor has no parameters.
    /// </summary>
    /// <param name="args">The arguments, and the by-ref write-backs.</param>
    /// <returns>The new instance, boxed for a value type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is null when the constructor has parameters.</exception>
    /// <exception cref="ArgumentException"><paramref name="args"/> has the wrong length.</exception>
    /// <exception cref="InvalidCastException">An argument or by-ref write-back cannot be converted. A callee exception propagates unwrapped.</exception>
    public delegate object? FastConstructorInvoker(object?[]? args);

#if NET8_0_OR_GREATER
    /// <summary>
    /// An access over a method that uses the caller's <see cref="Span{T}"/> of <see cref="object"/>.
    /// A span of references cannot be <c>stackalloc</c>'d.
    /// Value-type arguments are still boxed. A <see langword="ref"/> return is the current value, then boxed.
    /// Falls back to plain reflection when dynamic code is unavailable.
    /// </summary>
    /// <param name="target">The object to call; ignored on a static method.</param>
    /// <param name="args">The argument slots, and the by-ref write-backs. A span is never null.</param>
    /// <returns>The return value, boxed; null for a void method.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null on an instance method.</exception>
    /// <exception cref="ArgumentException"><paramref name="args"/> has the wrong length.</exception>
    /// <exception cref="InvalidCastException">The target's runtime type does not match, or an argument or by-ref write-back cannot be converted. A callee exception propagates unwrapped.</exception>
    public delegate object? FastSpanInvoker(object? target, Span<object?> args);

    /// <summary>
    /// An access over a constructor that uses the caller's <see cref="Span{T}"/> of <see cref="object"/>.
    /// Falls back to plain reflection when dynamic code is unavailable.
    /// </summary>
    /// <param name="args">The argument slots, and the by-ref write-backs. A span is never null.</param>
    /// <returns>The new instance, boxed for a value type.</returns>
    /// <exception cref="ArgumentException"><paramref name="args"/> has the wrong length.</exception>
    /// <exception cref="InvalidCastException">An argument or by-ref write-back cannot be converted. A callee exception propagates unwrapped.</exception>
    public delegate object? FastSpanConstructorInvoker(Span<object?> args);
#endif
}
