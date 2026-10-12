using System.Reflection;
using FastReflection.Emit;

namespace FastReflection
{
    /// <summary>
    /// Turns <see cref="PropertyInfo"/>, <see cref="FieldInfo"/>, <see cref="MethodInfo"/> and
    /// <see cref="ConstructorInfo"/> into compiled delegates. Delegates are created once and
    /// called like normal code — no reflection cost on the hot path.
    /// <para>
    /// When runtime code generation is unavailable (AOT/trimmed interpreters), all factories
    /// transparently fall back to ordinary reflection.
    /// </para>
    /// </summary>
    public static class ReflectionExtensions
    {
        // ---------------- Layer 1: exact-signature pass-through ----------------

        /// <summary>
        /// Creates a delegate of the exact signature via <c>MethodInfo.CreateDelegate</c>.
        /// Zero adaptation cost when the signature is known at compile time.
        /// </summary>
        public static TDelegate Unreflect<TDelegate>(this MethodInfo method) where TDelegate : Delegate
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            return (TDelegate)(object)method.CreateDelegate(typeof(TDelegate));
        }

        // ---------------- Layer 2/3: property accessors ----------------

        /// <summary>Compiles an open-instance getter: <c>(object target) => (object)((T)target).Prop</c>. Works for non-public accessors and static properties (target ignored).</summary>
        public static Func<object?, object?> CreateGetter(this PropertyInfo property)
            => AccessorEmitter.CreateGetter(property);

        /// <summary>Compiles an open-instance setter: <c>(object target, object value) => ((T)target).Prop = (V)value</c>. Supports write-through for <see langword="ref"/>-returning properties without a setter.</summary>
        public static Action<object?, object?> CreateSetter(this PropertyInfo property)
            => AccessorEmitter.CreateSetter(property);

        /// <summary>Typed getter. Zero-adaptation when <typeparamref name="TDeclaring"/>/<typeparamref name="TValue"/> match the signature; otherwise wraps the open getter.</summary>
        public static Func<TDeclaring, TValue> CreateGetter<TDeclaring, TValue>(this PropertyInfo property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            var get = property.GetGetMethod(true)
                ?? throw new ArgumentException("Property has no get accessor.", nameof(property));
            try { return (Func<TDeclaring, TValue>)get.CreateDelegate(typeof(Func<TDeclaring, TValue>)); }
            catch (Exception e) when (IsDelegateBindFailure(e)) { }
            var open = property.CreateGetter();
            return target => (TValue)open(target)!;
        }

        /// <summary>Typed setter; falls back to the open setter when the exact signature cannot be bound.</summary>
        public static Action<TDeclaring, TValue> CreateSetter<TDeclaring, TValue>(this PropertyInfo property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            var set = property.GetSetMethod(true)
                ?? throw new ArgumentException("Property has no set accessor.", nameof(property));
            try { return (Action<TDeclaring, TValue>)set.CreateDelegate(typeof(Action<TDeclaring, TValue>)); }
            catch (Exception e) when (IsDelegateBindFailure(e)) { }
            var open = property.CreateSetter();
            return (target, value) => open(target, value);
        }

        // ---------------- Layer 2/3: field accessors ----------------

        /// <summary>Compiles an open-instance field getter.</summary>
        public static Func<object?, object?> CreateGetter(this FieldInfo field)
            => AccessorEmitter.CreateGetter(field);

        /// <summary>Compiles an open-instance field setter. Throws for <see langword="readonly"/>/<see langword="const"/> fields.</summary>
        public static Action<object?, object?> CreateSetter(this FieldInfo field)
            => AccessorEmitter.CreateSetter(field);

        /// <summary>Typed field getter; wraps the open getter (FieldInfo has no CreateDelegate).</summary>
        public static Func<TDeclaring, TValue> CreateGetter<TDeclaring, TValue>(this FieldInfo field)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            var open = field.CreateGetter();
            return target => (TValue)open(target)!;
        }

        /// <summary>Typed field setter; wraps the open setter (FieldInfo has no CreateDelegate).</summary>
        public static Action<TDeclaring, TValue> CreateSetter<TDeclaring, TValue>(this FieldInfo field)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            var open = field.CreateSetter();
            return (target, value) => open(target, value);
        }

        // ---------------- Layer 3: unified untyped invokers ----------------

        /// <summary>
        /// Compiles <c>(target, args) => result</c> for any method — instance, static, non-public.
        /// <see langword="ref"/>/<see langword="out"/> arguments are written back into the args array.
        /// </summary>
        public static FastInvoker CreateInvoker(this MethodInfo method)
            => AccessorEmitter.CreateInvoker(method);

        /// <summary>Compiles <c>args => instance</c> for any constructor, including non-public ones.</summary>
        public static FastConstructorInvoker CreateInvoker(this ConstructorInfo ctor)
            => AccessorEmitter.CreateInvoker(ctor);

        /// <summary>Compiles a parameterless-constructor factory.</summary>
        public static Func<object?> CreateFactory(this ConstructorInfo ctor)
            => AccessorEmitter.CreateFactory(ctor);

        /// <summary>
        /// Compiles a factory for <paramref name="type"/>'s parameterless constructor
        /// (<paramref name="nonPublic"/> allows non-public ctors). Value types get a <c>default</c> factory.
        /// </summary>
        public static Func<object?> CreateFactory(this Type type, bool nonPublic = false)
            => AccessorEmitter.CreateFactory(type, nonPublic);

#if NET8_0_OR_GREATER
        /// <summary>Span-based invoker backed by <see cref="System.Reflection.MethodInvoker"/> (allocation-free, Span overloads).</summary>
        public static FastSpanInvoker CreateSpanInvoker(this MethodInfo method)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            var invoker = System.Reflection.MethodInvoker.Create(method);
            return invoker.Invoke;
        }

        /// <summary>Span-based constructor invoker backed by <see cref="System.Reflection.ConstructorInvoker"/>.</summary>
        public static FastSpanConstructorInvoker CreateSpanInvoker(this ConstructorInfo ctor)
        {
            if (ctor == null) throw new ArgumentNullException(nameof(ctor));
            var invoker = System.Reflection.ConstructorInvoker.Create(ctor);
            return invoker.Invoke;
        }
#endif

        private static bool IsDelegateBindFailure(Exception e)
            => e is ArgumentException or MethodAccessException or MemberAccessException or MissingMethodException or InvalidOperationException;
    }
}
