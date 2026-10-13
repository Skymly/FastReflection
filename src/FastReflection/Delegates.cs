namespace FastReflection
{
    /// <summary>
    /// Unified untyped method invoker: <c>(target, args) => result</c>.
    /// <paramref name="target"/> is ignored for static methods; <paramref name="args"/>
    /// may be <see langword="null"/> when the method has no parameters.
    /// Values of <see langword="ref"/>/<see langword="out"/> parameters are written back into
    /// <paramref name="args"/> after the call. A <see langword="ref"/> return is dereferenced and boxed.
    /// </summary>
    public delegate object? FastInvoker(object? target, object?[]? args);

    /// <summary>
    /// Unified untyped constructor invoker: <c>args => instance</c>.
    /// <paramref name="args"/> may be <see langword="null"/> when the constructor has no parameters.
    /// </summary>
    public delegate object? FastConstructorInvoker(object?[]? args);

#if NET8_0_OR_GREATER
    /// <summary>
    /// Span-based method invoker compiled with <c>DynamicMethod</c>.
    /// The caller reuses the argument buffer; <see cref="Span{T}"/> of a reference type cannot be <c>stackalloc</c>'d.
    /// Value-type arguments are still boxed. A <c>ref</c> return is dereferenced, then boxed.
    /// Falls back to <see cref="System.Reflection.MethodInvoker"/> when dynamic code is unavailable.
    /// </summary>
    public delegate object? FastSpanInvoker(object? target, Span<object?> args);

    /// <summary>
    /// Span-based constructor invoker compiled with <c>DynamicMethod</c>.
    /// Falls back to <see cref="System.Reflection.ConstructorInvoker"/> when dynamic code is unavailable.
    /// </summary>
    public delegate object? FastSpanConstructorInvoker(Span<object?> args);
#endif
}
