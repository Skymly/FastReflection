using System.Reflection;
using FastReflection.Emit;

namespace FastReflection
{
    /// <summary>
    /// Builds an access over a property, field, method, or constructor already known at runtime.
    /// An access is still that access when ordinary reflection completes it, and it fails when reflection cannot.
    /// <para>
    /// <see cref="Unreflect{TDelegate}(MethodInfo)"/> and a public exact
    /// <see cref="CreateInvoker{TDelegate}(MethodInfo)"/> bind are not accesses.
    /// <see cref="Ref{T}"/> adaptation and non-public methods that <c>CreateDelegate</c> cannot bind are accesses,
    /// and they throw <see cref="NotSupportedException"/> when dynamic code is unavailable.
    /// </para>
    /// </summary>
    public static class ReflectionExtensions
    {
        // Exact bind. Not an access.

        /// <summary>
        /// Binds <typeparamref name="TDelegate"/> with <c>MethodInfo.CreateDelegate</c>.
        /// Not an access: no adaptation, and no fallback to reflection.
        /// </summary>
        public static TDelegate Unreflect<TDelegate>(this MethodInfo method) where TDelegate : Delegate
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            return (TDelegate)(object)method.CreateDelegate(typeof(TDelegate));
        }

        // Access over a non-indexer property.

        /// <summary>An access over a non-indexer property: <c>(object target) => (object)value</c>. A static property has no target. A ref return is the current value, not a live reference. Indexer properties are not subjects of this access.</summary>
        public static Func<object?, object?> CreateGetter(this PropertyInfo property)
            => AccessorEmitter.CreateGetter(property);

        /// <summary>An access that writes a non-indexer property. A static property has no target. A ref-returning property with no setter can still be written, unless it is <see langword="ref"/> <see langword="readonly"/>. That write does not make the property a writable <see cref="Member"/>.</summary>
        public static Action<object?, object?> CreateSetter(this PropertyInfo property)
            => AccessorEmitter.CreateSetter(property);

        /// <summary>Typed get delegate. An exact bind is not an access. When that bind fails, the result is an access over <see cref="CreateGetter(PropertyInfo)"/>.</summary>
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

        /// <summary>Typed set delegate. An exact bind is not an access. When that bind fails, the result is an access over <see cref="CreateSetter(PropertyInfo)"/>.</summary>
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

        // Access over a field.

        /// <summary>An access over a field. A static field has no target.</summary>
        public static Func<object?, object?> CreateGetter(this FieldInfo field)
            => AccessorEmitter.CreateGetter(field);

        /// <summary>An access that writes a field. A static field has no target. Throws for a <see langword="readonly"/> or <see langword="const"/> field.</summary>
        public static Action<object?, object?> CreateSetter(this FieldInfo field)
            => AccessorEmitter.CreateSetter(field);

        /// <summary>Typed get delegate. An access over <see cref="CreateGetter(FieldInfo)"/>; <see cref="FieldInfo"/> has no exact bind.</summary>
        public static Func<TDeclaring, TValue> CreateGetter<TDeclaring, TValue>(this FieldInfo field)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            var open = field.CreateGetter();
            return target => (TValue)open(target)!;
        }

        /// <summary>Typed set delegate. An access over <see cref="CreateSetter(FieldInfo)"/>; <see cref="FieldInfo"/> has no exact bind.</summary>
        public static Action<TDeclaring, TValue> CreateSetter<TDeclaring, TValue>(this FieldInfo field)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            var open = field.CreateSetter();
            return (target, value) => open(target, value);
        }

        // Access over a method or constructor.

        /// <summary>
        /// An access over a method: <c>(target, args) => result</c>, including a non-public method.
        /// A static method has no target. <see langword="ref"/>/<see langword="out"/> values are written back into the array and boxed.
        /// A <see langword="ref"/> return is the current value, boxed, not a live reference.
        /// </summary>
        public static FastInvoker CreateInvoker(this MethodInfo method)
            => AccessorEmitter.CreateInvoker(method);

        /// <summary>
        /// Binds <typeparamref name="TDelegate"/> to the method.
        /// A public exact signature is bound with <c>CreateDelegate</c> and is not an access.
        /// A signature <c>CreateDelegate</c> cannot bind, including a non-public method and a
        /// <see cref="Ref{T}"/> passed by <see langword="ref"/>, <see langword="out"/>, or <see langword="in"/>,
        /// is an access. That access reads and writes <see cref="Ref{T}.Value"/> in place, without boxing,
        /// and throws <see cref="NotSupportedException"/> when dynamic code is unavailable.
        /// A <see langword="ref"/> or <see langword="in"/> class target is dereferenced before the call.
        /// </summary>
        /// <exception cref="ArgumentException">The delegate does not match, or <see cref="Ref{T}"/> is passed by value.</exception>
        /// <exception cref="NotSupportedException">Binding needs dynamic code and it is unavailable.</exception>
        public static TDelegate CreateInvoker<TDelegate>(this MethodInfo method) where TDelegate : Delegate
            => AccessorEmitter.CreateTypedInvoker<TDelegate>(method);

        /// <summary>An access over a constructor, including a non-public one: <c>args => instance</c>.</summary>
        public static FastConstructorInvoker CreateInvoker(this ConstructorInfo ctor)
            => AccessorEmitter.CreateInvoker(ctor);

        /// <summary>An access over a parameterless constructor.</summary>
        public static Func<object?> CreateFactory(this ConstructorInfo ctor)
            => AccessorEmitter.CreateFactory(ctor);

        /// <summary>
        /// An access over <paramref name="type"/>'s parameterless constructor.
        /// <paramref name="nonPublic"/> selects a non-public constructor.
        /// A value type with no parameterless constructor yields its default value, which is not an access.
        /// </summary>
        public static Func<object?> CreateFactory(this Type type, bool nonPublic = false)
            => AccessorEmitter.CreateFactory(type, nonPublic);

#if NET8_0_OR_GREATER
        /// <summary>
        /// An access over a method that uses the caller's <see cref="Span{T}"/> of <see cref="object"/>.
        /// A span of references cannot be <c>stackalloc</c>'d. Value-type arguments and the return value are still boxed.
        /// A <see langword="ref"/> return is the current value, then boxed.
        /// Falls back to <see cref="System.Reflection.MethodInvoker"/> when dynamic code is unavailable.
        /// </summary>
        public static FastSpanInvoker CreateSpanInvoker(this MethodInfo method)
            => AccessorEmitter.CreateSpanInvoker(method);

        /// <summary>
        /// An access over a constructor that uses the caller's <see cref="Span{T}"/> of <see cref="object"/>.
        /// Falls back to <see cref="System.Reflection.ConstructorInvoker"/> when dynamic code is unavailable.
        /// </summary>
        public static FastSpanConstructorInvoker CreateSpanInvoker(this ConstructorInfo ctor)
            => AccessorEmitter.CreateSpanInvoker(ctor);
#endif
    }
}
