using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using FastReflection.Binding;
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
        /// <typeparam name="TDelegate">The exact delegate signature to bind.</typeparam>
        /// <param name="method">The method to bind.</param>
        /// <returns>The bound delegate.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="method"/> is null.</exception>
        /// <exception cref="ArgumentException">The delegate signature does not match the method.</exception>
        /// <exception cref="MethodAccessException">The exact bind is not permitted for this method.</exception>
        public static TDelegate Unreflect<TDelegate>(this MethodInfo method) where TDelegate : Delegate
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            return (TDelegate)(object)method.CreateDelegate(typeof(TDelegate));
        }

        // Access over a non-indexer property.

        /// <summary>An access over a non-indexer property: <c>(object target) => (object)value</c>. A static property has no target. A ref return is the current value, not a live reference. Indexer properties are not subjects of this access.</summary>
        /// <param name="property">The property to access.</param>
        /// <returns>The getter access.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="property"/> is null — or, on the returned getter, the target is null on an instance property.</exception>
        /// <exception cref="NotSupportedException"><paramref name="property"/> is an indexer; call <see cref="CreateInvoker(MethodInfo)"/> on the accessor method instead.</exception>
        /// <exception cref="ArgumentException">The property has no get accessor, or its declaring type is an open generic.</exception>
        /// <exception cref="InvalidCastException">On the returned getter, the target's runtime type does not match. A getter exception propagates unwrapped.</exception>
        public static Func<object?, object?> CreateGetter(this PropertyInfo property)
            => AccessorEmitter.CreateGetter(property);

        /// <summary>An access that writes a non-indexer property. A static property has no target. A ref-returning property with no setter can still be written, unless it is <see langword="ref"/> <see langword="readonly"/>. That write does not make the property a writable <see cref="Member"/>.</summary>
        /// <param name="property">The property to write.</param>
        /// <returns>The setter access.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="property"/> is null — or, on the returned setter, the target is null on an instance property.</exception>
        /// <exception cref="NotSupportedException"><paramref name="property"/> is an indexer; or a ref-returning property with no setter and dynamic code is unavailable.</exception>
        /// <exception cref="ArgumentException">The property has no set accessor — and is not a writable ref-returning property — or its declaring type is an open generic.</exception>
        /// <exception cref="InvalidCastException">On the returned setter, the target's runtime type does not match, or the value cannot be converted to the property type. A setter exception propagates unwrapped.</exception>
        public static Action<object?, object?> CreateSetter(this PropertyInfo property)
            => AccessorEmitter.CreateSetter(property);

        /// <summary>Typed get delegate. An exact bind is not an access. When that bind fails, the result is an access over <see cref="CreateGetter(PropertyInfo)"/>.</summary>
        /// <typeparam name="TDeclaring">The declaring type, or a compatible target type.</typeparam>
        /// <typeparam name="TValue">The property type, or a compatible value type.</typeparam>
        /// <param name="property">The property to access.</param>
        /// <returns>The bound getter.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="property"/> is null.</exception>
        /// <exception cref="ArgumentException">The property has no get accessor.</exception>
        /// <exception cref="InvalidCastException">On the returned delegate, the target or the value does not convert to <typeparamref name="TDeclaring"/> or <typeparamref name="TValue"/>.</exception>
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
        /// <typeparam name="TDeclaring">The declaring type, or a compatible target type.</typeparam>
        /// <typeparam name="TValue">The property type, or a compatible value type.</typeparam>
        /// <param name="property">The property to write.</param>
        /// <returns>The bound setter.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="property"/> is null.</exception>
        /// <exception cref="ArgumentException">The property has no set accessor.</exception>
        /// <exception cref="InvalidCastException">On the returned delegate, the target or the value does not convert to <typeparamref name="TDeclaring"/> or <typeparamref name="TValue"/>.</exception>
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
        /// <param name="field">The field to access.</param>
        /// <returns>The getter access.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="field"/> is null — or, on the returned getter, the target is null on an instance field.</exception>
        /// <exception cref="ArgumentException">The field's declaring type is an open generic.</exception>
        /// <exception cref="InvalidCastException">On the returned getter, the target's runtime type does not match.</exception>
        public static Func<object?, object?> CreateGetter(this FieldInfo field)
            => AccessorEmitter.CreateGetter(field);

        /// <summary>An access that writes a field. A static field has no target. Throws for a <see langword="readonly"/> or <see langword="const"/> field.</summary>
        /// <param name="field">The field to write.</param>
        /// <returns>The setter access.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="field"/> is null — or, on the returned setter, the target is null on an instance field.</exception>
        /// <exception cref="ArgumentException">The field is <see langword="readonly"/> or <see langword="const"/>, or its declaring type is an open generic.</exception>
        /// <exception cref="InvalidCastException">On the returned setter, the target's runtime type does not match, or the value cannot be converted to the field type.</exception>
        public static Action<object?, object?> CreateSetter(this FieldInfo field)
            => AccessorEmitter.CreateSetter(field);

        /// <summary>Typed get delegate. An access over <see cref="CreateGetter(FieldInfo)"/>; <see cref="FieldInfo"/> has no exact bind.</summary>
        /// <typeparam name="TDeclaring">The declaring type, or a compatible target type.</typeparam>
        /// <typeparam name="TValue">The field type, or a compatible value type.</typeparam>
        /// <param name="field">The field to access.</param>
        /// <returns>The getter access, typed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="field"/> is null.</exception>
        /// <exception cref="InvalidCastException">On the returned delegate, the target or the value does not convert to <typeparamref name="TDeclaring"/> or <typeparamref name="TValue"/>.</exception>
        public static Func<TDeclaring, TValue> CreateGetter<TDeclaring, TValue>(this FieldInfo field)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            var open = field.CreateGetter();
            return target => (TValue)open(target)!;
        }

        /// <summary>Typed set delegate. An access over <see cref="CreateSetter(FieldInfo)"/>; <see cref="FieldInfo"/> has no exact bind.</summary>
        /// <typeparam name="TDeclaring">The declaring type, or a compatible target type.</typeparam>
        /// <typeparam name="TValue">The field type, or a compatible value type.</typeparam>
        /// <param name="field">The field to write.</param>
        /// <returns>The setter access, typed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="field"/> is null.</exception>
        /// <exception cref="InvalidCastException">On the returned delegate, the target or the value does not convert to <typeparamref name="TDeclaring"/> or <typeparamref name="TValue"/>.</exception>
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
        /// <param name="method">The method to access. An open generic method must be constructed first.</param>
        /// <returns>The invoker access.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="method"/> is null — or, on the returned invoker, the target is null on an instance method or <c>args</c> is null when the method has parameters.</exception>
        /// <exception cref="ArgumentException">The method or its declaring type is an open generic — or, on the returned invoker, <c>args</c> has the wrong length.</exception>
        /// <exception cref="InvalidCastException">On the returned invoker, the target's runtime type does not match, or an argument or by-ref write-back cannot be converted. A callee exception propagates unwrapped.</exception>
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
        /// <typeparam name="TDelegate">The delegate signature to bind. By-ref parameters may be <see cref="Ref{T}"/> holders.</typeparam>
        /// <param name="method">The method to bind or access.</param>
        /// <returns>The bound or access delegate.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="method"/> is null.</exception>
        /// <exception cref="ArgumentException">The delegate does not match, or <see cref="Ref{T}"/> is passed by value, or the method or its declaring type is an open generic.</exception>
        /// <exception cref="NotSupportedException">Binding needs dynamic code and it is unavailable.</exception>
        /// <remarks>
        /// Callers binding a public exact signature can use <see cref="Unreflect{TDelegate}(MethodInfo)"/>,
        /// which carries no dynamic-code requirement.
        /// </remarks>
        [RequiresDynamicCode(EmitHelper.DynamicCodeMessage)]
        public static TDelegate CreateInvoker<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TDelegate>(this MethodInfo method) where TDelegate : Delegate
            => AccessorEmitter.CreateTypedInvoker<TDelegate>(method);

        /// <summary>An access over a constructor, including a non-public one: <c>args => instance</c>.</summary>
        /// <param name="ctor">The constructor to access.</param>
        /// <returns>The invoker access.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="ctor"/> is null — or, on the returned invoker, <c>args</c> is null when the constructor has parameters.</exception>
        /// <exception cref="ArgumentException">The constructor's declaring type is an open generic — or, on the returned invoker, <c>args</c> has the wrong length.</exception>
        /// <exception cref="InvalidCastException">On the returned invoker, an argument or by-ref write-back cannot be converted. A callee exception propagates unwrapped.</exception>
        public static FastConstructorInvoker CreateInvoker(this ConstructorInfo ctor)
            => AccessorEmitter.CreateInvoker(ctor);

        /// <summary>An access over a parameterless constructor.</summary>
        /// <param name="ctor">The parameterless constructor to access.</param>
        /// <returns>The factory access.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="ctor"/> is null.</exception>
        /// <exception cref="ArgumentException">The constructor is not parameterless.</exception>
        /// <remarks>A constructor exception propagates unwrapped through the returned factory.</remarks>
        public static Func<object?> CreateFactory(this ConstructorInfo ctor)
            => AccessorEmitter.CreateFactory(ctor);

        /// <summary>
        /// An access over <paramref name="type"/>'s parameterless constructor.
        /// <paramref name="nonPublic"/> selects a non-public constructor.
        /// A value type with no parameterless constructor yields its default value, which is not an access.
        /// </summary>
        /// <param name="type">The type to instantiate.</param>
        /// <param name="nonPublic">True to select a non-public parameterless constructor.</param>
        /// <returns>The factory access.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
        /// <exception cref="ArgumentException">The type is abstract or an open generic, or it has no parameterless constructor.</exception>
        /// <remarks>A constructor exception propagates unwrapped through the returned factory.</remarks>
        public static Func<object?> CreateFactory(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
            this Type type, bool nonPublic = false)
            => AccessorEmitter.CreateFactory(type, nonPublic);

        /// <summary>
        /// A bounded converter between two runtime-known types — the sanctioned bound-call conversion
        /// set plus spec explicit numeric conversions: identity, reference casts and unboxing in either
        /// direction, every spec numeric conversion (widening and narrowing, <see langword="char"/>
        /// included), <see cref="Nullable{T}"/> lift and unlift, and enum↔underlying.
        /// User-defined <c>op_Implicit</c>/<c>op_Explicit</c> methods are never consulted.
        /// Not a name lookup and not a <see cref="Type"/> extension — a <c>Create</c>-shaped access.
        /// </summary>
        /// <param name="sourceType">The declared source type.</param>
        /// <param name="targetType">The target type.</param>
        /// <returns>The converter; a boxed value in, a boxed value out.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="sourceType"/> or <paramref name="targetType"/> is null.</exception>
        /// <exception cref="InvalidCastException">No sanctioned conversion exists between the two types — at <c>Create</c> time — or, on the returned converter, a value cannot be converted.</exception>
        public static Func<object?, object?> CreateConverter(Type sourceType, Type targetType)
        {
            if (sourceType == null) throw new ArgumentNullException(nameof(sourceType));
            if (targetType == null) throw new ArgumentNullException(nameof(targetType));
            if (!BoundConversion.CanCreateConverter(sourceType, targetType))
                throw new InvalidCastException($"No sanctioned conversion exists from '{sourceType}' to '{targetType}'.");
            return value => BoundConversion.ConvertExplicit(value, sourceType, targetType);
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// An access over a method that uses the caller's <see cref="Span{T}"/> of <see cref="object"/>.
        /// A span of references cannot be <c>stackalloc</c>'d. Value-type arguments and the return value are still boxed.
        /// A <see langword="ref"/> return is the current value, then boxed.
        /// Falls back to plain reflection when dynamic code is unavailable.
        /// </summary>
        /// <param name="method">The method to access. An open generic method must be constructed first.</param>
        /// <returns>The span invoker access.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="method"/> is null — or, on the returned invoker, the target is null on an instance method.</exception>
        /// <exception cref="ArgumentException">The method or its declaring type is an open generic — or, on the returned invoker, the span has the wrong length. A span is never null.</exception>
        /// <exception cref="InvalidCastException">On the returned invoker, the target's runtime type does not match, or an argument or by-ref write-back cannot be converted. A callee exception propagates unwrapped.</exception>
        public static FastSpanInvoker CreateSpanInvoker(this MethodInfo method)
            => AccessorEmitter.CreateSpanInvoker(method);

        /// <summary>
        /// An access over a constructor that uses the caller's <see cref="Span{T}"/> of <see cref="object"/>.
        /// Falls back to plain reflection when dynamic code is unavailable.
        /// </summary>
        /// <param name="ctor">The constructor to access.</param>
        /// <returns>The span invoker access.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="ctor"/> is null.</exception>
        /// <exception cref="ArgumentException">The constructor's declaring type is an open generic — or, on the returned invoker, the span has the wrong length.</exception>
        /// <exception cref="InvalidCastException">On the returned invoker, an argument or by-ref write-back cannot be converted. A callee exception propagates unwrapped.</exception>
        public static FastSpanConstructorInvoker CreateSpanInvoker(this ConstructorInfo ctor)
            => AccessorEmitter.CreateSpanInvoker(ctor);
#endif
    }
}
