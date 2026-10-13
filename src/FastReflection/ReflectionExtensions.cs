using System.Reflection;
using FastReflection.Emit;

namespace FastReflection
{
    /// <summary>
    /// Turns <see cref="PropertyInfo"/>, <see cref="FieldInfo"/>, <see cref="MethodInfo"/> and
    /// <see cref="ConstructorInfo"/> into compiled delegates. Delegates are created once and
    /// called like normal code — no reflection cost on the hot path.
    /// <para>
    /// When runtime code generation is unavailable (AOT/trimmed interpreters), factories fall back
    /// to ordinary reflection. <see cref="CreateInvoker{TDelegate}(MethodInfo)"/> still binds a public
    /// exact signature with <c>CreateDelegate</c>; <see cref="Ref{T}"/> adaptation and non-public methods
    /// that <c>CreateDelegate</c> cannot bind throw <see cref="NotSupportedException"/> instead.
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
            catch (Exception e) when (EmitHelper.IsDelegateBindFailure(e)) { }
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
            catch (Exception e) when (EmitHelper.IsDelegateBindFailure(e)) { }
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
        /// A <see langword="ref"/> return is dereferenced and boxed as its element value.
        /// </summary>
        public static FastInvoker CreateInvoker(this MethodInfo method)
            => AccessorEmitter.CreateInvoker(method);

        /// <summary>
        /// Creates a strongly typed invoker.
        /// A public exact signature is bound with <c>CreateDelegate</c> and does not need dynamic code.
        /// Non-public exact signatures that <c>CreateDelegate</c> cannot bind, including
        /// <see langword="ref"/> / <see langword="out"/> / <see langword="in"/> parameters, are compiled
        /// with <c>DynamicMethod</c>. Those parameters may instead be declared as <c>ref</c> / <c>out</c> /
        /// <c>in</c> <see cref="Ref{T}"/>; that shape is also compiled, and the method writes
        /// <see cref="Ref{T}.Value"/> in place, without boxing.
        /// A <see langword="ref"/> or <see langword="in"/> class target is dereferenced before the call.
        /// </summary>
        /// <exception cref="ArgumentException">The delegate does not match, or <see cref="Ref{T}"/> is passed by value.</exception>
        /// <exception cref="NotSupportedException">Binding needs dynamic code and it is unavailable.</exception>
        public static TDelegate CreateInvoker<TDelegate>(this MethodInfo method) where TDelegate : Delegate
            => AccessorEmitter.CreateTypedInvoker<TDelegate>(method);

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
        /// <summary>
        /// Span-based invoker compiled with <c>DynamicMethod</c>.
        /// The caller owns the <see cref="Span{T}"/> buffer (<c>Span&lt;object?&gt;</c> cannot be <c>stackalloc</c>'d).
        /// Value-type arguments and the return value are still boxed.
        /// A <see langword="ref"/> return is dereferenced, then boxed.
        /// Falls back to <see cref="System.Reflection.MethodInvoker"/> when dynamic code is unavailable.
        /// </summary>
        public static FastSpanInvoker CreateSpanInvoker(this MethodInfo method)
            => AccessorEmitter.CreateSpanInvoker(method);

        /// <summary>
        /// Span-based constructor invoker compiled with <c>DynamicMethod</c>.
        /// Falls back to <see cref="System.Reflection.ConstructorInvoker"/> when dynamic code is unavailable.
        /// </summary>
        public static FastSpanConstructorInvoker CreateSpanInvoker(this ConstructorInfo ctor)
            => AccessorEmitter.CreateSpanInvoker(ctor);
#endif
    }
}
