namespace FastReflection
{
    /// <summary>
    /// Mutable holder for a by-ref argument.
    /// Pass it as <see langword="ref"/>, <see langword="out"/>, or <see langword="in"/> to
    /// <see cref="ReflectionExtensions.CreateInvoker{TDelegate}(System.Reflection.MethodInfo)"/>.
    /// The called method reads and writes <see cref="Value"/> in place, with no boxing and no argument array.
    /// </summary>
    /// <typeparam name="T">Type of the method parameter, after removing <see langword="ref"/> / <see langword="out"/> / <see langword="in"/>.</typeparam>
    public struct Ref<T>
    {
        /// <summary>The value the method reads and writes.</summary>
        public T Value;

        /// <summary>Creates a holder with an initial value.</summary>
        public Ref(T value) => Value = value;
    }
}
