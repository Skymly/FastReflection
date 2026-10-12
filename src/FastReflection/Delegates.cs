namespace FastReflection
{
    /// <summary>
    /// Unified untyped method invoker: <c>(target, args) => result</c>.
    /// <paramref name="target"/> is ignored for static methods; <paramref name="args"/>
    /// may be <see langword="null"/> when the method has no parameters.
    /// Values of <see langword="ref"/>/<see langword="out"/> parameters are written back into
    /// <paramref name="args"/> after the call.
    /// </summary>
    public delegate object? FastInvoker(object? target, object?[]? args);

    /// <summary>
    /// Unified untyped constructor invoker: <c>args => instance</c>.
    /// <paramref name="args"/> may be <see langword="null"/> when the constructor has no parameters.
    /// </summary>
    public delegate object? FastConstructorInvoker(object?[]? args);

#if NET8_0_OR_GREATER
    /// <summary>
    /// Span-based method invoker. Avoids the implicit <see cref="object"/> array allocation
    /// at the call site; backed by <see cref="System.Reflection.MethodInvoker"/>.
    /// </summary>
    public delegate object? FastSpanInvoker(object? target, Span<object?> args);

    /// <summary>
    /// Span-based constructor invoker; backed by <see cref="System.Reflection.ConstructorInvoker"/>.
    /// </summary>
    public delegate object? FastSpanConstructorInvoker(Span<object?> args);
#endif
}
