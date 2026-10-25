using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;

namespace FastReflection.Emit
{
    /// <summary>
    /// Builds compiled delegates from reflection members via <see cref="DynamicMethod"/>.
    /// Every factory falls back to plain reflection when dynamic code is unavailable or emit fails.
    /// </summary>
    internal static class AccessorEmitter
    {
        // Shared USM justification for the emit call sites below: they run only behind
        // EmitHelper.DynamicCodeSupported, and every caller falls back to reflection.
        private const string EmitSiteJustification =
            "Emit sites run only behind EmitHelper.DynamicCodeSupported; callers fall back to reflection. The #9 annotation contract is finalized in #38.";

        // ============================== properties ==============================

        internal static Func<object?, object?> CreateGetter(PropertyInfo property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            ThrowIfIndexer(property);
            ThrowIfOpenGeneric(property);
            var get = property.GetGetMethod(true)
                ?? throw new ArgumentException("Property has no get accessor.", nameof(property));
            if (!EmitHelper.DynamicCodeSupported)
                return target => ReflectionFallback.Get(get, target);
            return EmitHelper.EmitOrFallback(
                () => EmitPropertyGetter(property, get),
                (Func<object?, object?>)(target => ReflectionFallback.Get(get, target)));
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = EmitSiteJustification)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = EmitSiteJustification)]
        internal static Func<object?, object?> EmitPropertyGetter(PropertyInfo property, MethodInfo get)
        {
            var dt = DeclaringType(property);
            var dm = EmitHelper.CreateDynamicMethod(
                $"FastReflection_get_{dt.Name}_{property.Name}",
                typeof(object), new[] { typeof(object) }, dt);
            var il = dm.GetILGenerator();
            if (!get.IsStatic)
            {
                EmitHelper.EmitCheckTarget(il);
                EmitHelper.EmitLoadTarget(il, dt);
            }
            EmitHelper.EmitCall(il, get);
            EmitLoadMemberValue(il, property.PropertyType);
            il.Emit(OpCodes.Ret);
            return (Func<object?, object?>)dm.CreateDelegate(typeof(Func<object?, object?>));
        }

        internal static Action<object?, object?> CreateSetter(PropertyInfo property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            ThrowIfIndexer(property);
            ThrowIfOpenGeneric(property);
            var dt = DeclaringType(property);
            var set = property.GetSetMethod(true);
            if (set == null)
            {
                // "ref T P { get; }" can still be assigned through the returned managed pointer.
                var get = property.GetGetMethod(true);
                if (get == null || !property.PropertyType.IsByRef || IsReadOnlyRef(get))
                    throw new ArgumentException("Property has no set accessor.", nameof(property));
                if (!EmitHelper.DynamicCodeSupported)
                    throw new NotSupportedException(
                        "Writing a ref-returning property with no setter requires dynamic code, which is unavailable.");
                try
                {
                    return EmitPropertySetterViaRef(property, get, property.PropertyType.GetElementType()!);
                }
                catch
                {
                    throw new ArgumentException("Property has no set accessor.", nameof(property));
                }
            }
            if (!EmitHelper.DynamicCodeSupported)
                return (target, value) => ReflectionFallback.Set(set, target, value);
            return EmitHelper.EmitOrFallback(
                () => EmitPropertySetter(property, set),
                (Action<object?, object?>)((target, value) => ReflectionFallback.Set(set, target, value)));
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = EmitSiteJustification)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = EmitSiteJustification)]
        internal static Action<object?, object?> EmitPropertySetter(PropertyInfo property, MethodInfo set)
        {
            var dt = DeclaringType(property);
            var dm = EmitHelper.CreateDynamicMethod(
                $"FastReflection_set_{dt.Name}_{property.Name}",
                null, new[] { typeof(object), typeof(object) }, dt);
            var il = dm.GetILGenerator();
            if (!set.IsStatic)
            {
                EmitHelper.EmitCheckTarget(il);
                EmitHelper.EmitLoadTarget(il, dt);
            }
            il.Emit(OpCodes.Ldarg_1);
            EmitHelper.EmitConvertFromObject(il, property.PropertyType);
            EmitHelper.EmitCall(il, set);
            il.Emit(OpCodes.Ret);
            return (Action<object?, object?>)dm.CreateDelegate(typeof(Action<object?, object?>));
        }

        /// <summary>Emits <c>target => (elType)via-getter-reference = value</c> for ref-returning properties.</summary>
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = EmitSiteJustification)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = EmitSiteJustification)]
        private static Action<object?, object?> EmitPropertySetterViaRef(PropertyInfo property, MethodInfo get, Type elementType)
        {
            var dt = DeclaringType(property);
            var dm = EmitHelper.CreateDynamicMethod(
                $"FastReflection_setref_{dt.Name}_{property.Name}",
                null, new[] { typeof(object), typeof(object) }, dt);
            var il = dm.GetILGenerator();
            if (!get.IsStatic)
                EmitHelper.EmitCheckTarget(il);
            // Convert the value before calling the getter: a conversion failure precedes the callee.
            var converted = il.DeclareLocal(elementType);
            il.Emit(OpCodes.Ldarg_1);
            EmitHelper.EmitConvertFromObject(il, elementType);
            il.Emit(OpCodes.Stloc, converted);
            if (!get.IsStatic)
                EmitHelper.EmitLoadTarget(il, dt);
            EmitHelper.EmitCall(il, get);          // &T
            il.Emit(OpCodes.Ldloc, converted);
            il.Emit(OpCodes.Stobj, elementType);
            il.Emit(OpCodes.Ret);
            return (Action<object?, object?>)dm.CreateDelegate(typeof(Action<object?, object?>));
        }

        // ============================== fields ==============================

        internal static Func<object?, object?> CreateGetter(FieldInfo field)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            ThrowIfOpenGeneric(field);
            if (!EmitHelper.DynamicCodeSupported)
                return target => ReflectionFallback.Get(field, target);
            return EmitHelper.EmitOrFallback(
                () => EmitFieldGetter(field),
                (Func<object?, object?>)(target => ReflectionFallback.Get(field, target)));
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = EmitSiteJustification)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = EmitSiteJustification)]
        internal static Func<object?, object?> EmitFieldGetter(FieldInfo field)
        {
            var dt = DeclaringType(field);
            var dm = EmitHelper.CreateDynamicMethod(
                $"FastReflection_getf_{dt.Name}_{field.Name}",
                typeof(object), new[] { typeof(object) }, dt);
            var il = dm.GetILGenerator();
            if (field.IsStatic)
            {
                il.Emit(OpCodes.Ldsfld, field);
            }
            else
            {
                EmitHelper.EmitCheckTarget(il);
                EmitHelper.EmitLoadTarget(il, dt);
                il.Emit(OpCodes.Ldfld, field);
            }
            EmitLoadMemberValue(il, field.FieldType);
            il.Emit(OpCodes.Ret);
            return (Func<object?, object?>)dm.CreateDelegate(typeof(Func<object?, object?>));
        }

        internal static Action<object?, object?> CreateSetter(FieldInfo field)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            ThrowIfOpenGeneric(field);
            if (field.IsLiteral || field.IsInitOnly)
                throw new ArgumentException("Field is read-only.", nameof(field));
            if (!EmitHelper.DynamicCodeSupported)
                return (target, value) => ReflectionFallback.Set(field, target, value);
            return EmitHelper.EmitOrFallback(
                () => EmitFieldSetter(field),
                (Action<object?, object?>)((target, value) => ReflectionFallback.Set(field, target, value)));
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = EmitSiteJustification)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = EmitSiteJustification)]
        internal static Action<object?, object?> EmitFieldSetter(FieldInfo field)
        {
            var dt = DeclaringType(field);
            var dm = EmitHelper.CreateDynamicMethod(
                $"FastReflection_setf_{dt.Name}_{field.Name}",
                null, new[] { typeof(object), typeof(object) }, dt);
            var il = dm.GetILGenerator();
            if (field.IsStatic)
            {
                il.Emit(OpCodes.Ldarg_1);
                EmitHelper.EmitConvertFromObject(il, field.FieldType);
                il.Emit(OpCodes.Stsfld, field);
            }
            else
            {
                EmitHelper.EmitCheckTarget(il);
                EmitHelper.EmitLoadTarget(il, dt);
                il.Emit(OpCodes.Ldarg_1);
                EmitHelper.EmitConvertFromObject(il, field.FieldType);
                il.Emit(OpCodes.Stfld, field);
            }
            il.Emit(OpCodes.Ret);
            return (Action<object?, object?>)dm.CreateDelegate(typeof(Action<object?, object?>));
        }

        // ============================== methods ==============================

        internal static FastInvoker CreateInvoker(MethodInfo method)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            ThrowIfOpenGeneric(method);
            if (method.ContainsGenericParameters)
                throw new ArgumentException("Open generic methods must be constructed via MakeGenericMethod first.", nameof(method));
            if (!EmitHelper.DynamicCodeSupported)
                return (target, args) => ReflectionFallback.Invoke(method, target, args);
            return EmitHelper.EmitOrFallback(
                () => EmitInvoker(method),
                (FastInvoker)((target, args) => ReflectionFallback.Invoke(method, target, args)));
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = EmitSiteJustification)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = EmitSiteJustification)]
        internal static FastInvoker EmitInvoker(MethodInfo method)
        {
            var dt = DeclaringType(method);
            var ps = method.GetParameters();
            var dm = EmitHelper.CreateDynamicMethod(
                $"FastReflection_invoke_{dt.Name}_{method.Name}",
                typeof(object), new[] { typeof(object), typeof(object[]) }, dt);
            var il = dm.GetILGenerator();
            EmitHelper.EmitCheckArgs(il, 1, ps.Length);
            if (!method.IsStatic)
            {
                EmitHelper.EmitCheckTarget(il);
                EmitHelper.EmitLoadTarget(il, dt);
            }
            var byrefs = EmitParameters(il, ps, 1);
            EmitHelper.EmitCall(il, method);
            EmitWriteBack(il, byrefs, 1);
            if (method.ReturnType == typeof(void))
                il.Emit(OpCodes.Ldnull);
            else
                EmitHelper.EmitConvertToObject(il, method.ReturnType);
            il.Emit(OpCodes.Ret);
            return (FastInvoker)dm.CreateDelegate(typeof(FastInvoker));
        }

        // ============================== constructors ==============================

        internal static FastConstructorInvoker CreateInvoker(ConstructorInfo ctor)
        {
            if (ctor == null) throw new ArgumentNullException(nameof(ctor));
            var dt = DeclaringType(ctor);
            ThrowIfOpenGeneric(ctor);
            var ps = ctor.GetParameters();
            if (!EmitHelper.DynamicCodeSupported)
                return args => ReflectionFallback.Invoke(ctor, args);
            return EmitHelper.EmitOrFallback(
                () => EmitInvoker(ctor, ps),
                (FastConstructorInvoker)(args => ReflectionFallback.Invoke(ctor, args)));
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = EmitSiteJustification)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = EmitSiteJustification)]
        internal static FastConstructorInvoker EmitInvoker(ConstructorInfo ctor, ParameterInfo[] ps)
        {
            var dt = DeclaringType(ctor);
            var dm = EmitHelper.CreateDynamicMethod(
                $"FastReflection_new_{dt.Name}",
                typeof(object), new[] { typeof(object[]) }, dt);
            var il = dm.GetILGenerator();
            EmitHelper.EmitCheckArgs(il, 0, ps.Length);
            var byrefs = EmitParameters(il, ps, 0);
            il.Emit(OpCodes.Newobj, ctor);
            EmitWriteBack(il, byrefs, 0);
            if (dt.IsValueType) il.Emit(OpCodes.Box, dt);
            il.Emit(OpCodes.Ret);
            return (FastConstructorInvoker)dm.CreateDelegate(typeof(FastConstructorInvoker));
        }

        /// <summary>Factory for a parameterless constructor.</summary>
        internal static Func<object?> CreateFactory(ConstructorInfo ctor)
        {
            if (ctor == null) throw new ArgumentNullException(nameof(ctor));
            var dt = DeclaringType(ctor);
            if (ctor.GetParameters().Length != 0)
                throw new ArgumentException("CreateFactory requires a parameterless constructor.", nameof(ctor));
            if (!EmitHelper.DynamicCodeSupported)
                return () => ReflectionFallback.New(ctor);
            return EmitHelper.EmitOrFallback(
                () => EmitFactory(ctor),
                (Func<object?>)(() => ReflectionFallback.New(ctor)));
        }

        /// <summary>Factory for a type's parameterless constructor (any visibility when <paramref name="nonPublic"/>); structs get an initobj factory.</summary>
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = EmitSiteJustification)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = EmitSiteJustification)]
        [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "The constructor requirements of the caller-supplied type are part of the #9 contract, finalized in #38.")]
        [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The constructor requirements of the caller-supplied type are part of the #9 contract, finalized in #38.")]
        internal static Func<object?> CreateFactory(Type type, bool nonPublic)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (type.IsAbstract) // includes static classes
                throw new ArgumentException($"Cannot create instances of '{type}'.", nameof(type));
            if (type.ContainsGenericParameters)
                throw new ArgumentException("Open generic types are not supported.", nameof(type));

            var flags = BindingFlags.Instance | BindingFlags.Public;
            if (nonPublic) flags |= BindingFlags.NonPublic;
            var ctor = type.GetConstructor(flags, null, Type.EmptyTypes, null);
            if (ctor != null) return CreateFactory(ctor);

            if (type.IsValueType)
            {
                if (!EmitHelper.DynamicCodeSupported)
                    return () => ReflectionFallback.New(type);
                try
                {
                    var dm = EmitHelper.CreateDynamicMethod(
                        $"FastReflection_default_{type.Name}",
                        typeof(object), Type.EmptyTypes, type);
                    var il = dm.GetILGenerator();
                    var loc = il.DeclareLocal(type);
                    il.Emit(OpCodes.Ldloca, loc);
                    il.Emit(OpCodes.Initobj, type);
                    il.Emit(OpCodes.Ldloc, loc);
                    il.Emit(OpCodes.Box, type);
                    il.Emit(OpCodes.Ret);
                    return (Func<object?>)dm.CreateDelegate(typeof(Func<object?>));
                }
                catch
                {
                    return () => ReflectionFallback.New(type);
                }
            }

            throw new ArgumentException($"Type '{type}' has no parameterless constructor.", nameof(type));
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = EmitSiteJustification)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = EmitSiteJustification)]
        internal static Func<object?> EmitFactory(ConstructorInfo ctor)
        {
            var dt = DeclaringType(ctor);
            var dm = EmitHelper.CreateDynamicMethod(
                $"FastReflection_new_{dt.Name}",
                typeof(object), Type.EmptyTypes, dt);
            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Newobj, ctor);
            if (dt.IsValueType) il.Emit(OpCodes.Box, dt);
            il.Emit(OpCodes.Ret);
            return (Func<object?>)dm.CreateDelegate(typeof(Func<object?>));
        }

#if NET8_0_OR_GREATER
        internal static FastSpanInvoker CreateSpanInvoker(MethodInfo method)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            ThrowIfOpenGeneric(method);
            if (method.ContainsGenericParameters)
                throw new ArgumentException("Open generic methods must be constructed via MakeGenericMethod first.", nameof(method));
            if (!EmitHelper.DynamicCodeSupported)
                return (target, args) => ReflectionFallback.Invoke(method, target, args);
            return EmitHelper.EmitOrFallback(
                () => EmitSpanMethod(method),
                (FastSpanInvoker)((target, args) => ReflectionFallback.Invoke(method, target, args)));
        }

        internal static FastSpanConstructorInvoker CreateSpanInvoker(ConstructorInfo ctor)
        {
            if (ctor == null) throw new ArgumentNullException(nameof(ctor));
            ThrowIfOpenGeneric(ctor);
            if (!EmitHelper.DynamicCodeSupported)
                return args => ReflectionFallback.Invoke(ctor, args);
            return EmitHelper.EmitOrFallback(
                () => EmitSpanConstructor(ctor),
                (FastSpanConstructorInvoker)(args => ReflectionFallback.Invoke(ctor, args)));
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = EmitSiteJustification)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = EmitSiteJustification)]
        private static FastSpanInvoker EmitSpanMethod(MethodInfo method)
        {
            var dt = DeclaringType(method);
            var ps = method.GetParameters();
            var dm = EmitHelper.CreateDynamicMethod(
                $"FastReflection_span_{dt.Name}_{method.Name}",
                typeof(object), new[] { typeof(object), typeof(Span<object?>) }, dt);
            var il = dm.GetILGenerator();
            EmitHelper.EmitCheckSpan(il, 1, ps.Length);
            if (!method.IsStatic)
            {
                EmitHelper.EmitCheckTarget(il);
                EmitHelper.EmitLoadTarget(il, dt);
            }
            var byrefs = EmitParameters(il, ps, 1, span: true);
            EmitHelper.EmitCall(il, method);
            EmitWriteBack(il, byrefs, 1, span: true);
            if (method.ReturnType == typeof(void))
                il.Emit(OpCodes.Ldnull);
            else
                EmitHelper.EmitConvertToObject(il, method.ReturnType);
            il.Emit(OpCodes.Ret);
            return (FastSpanInvoker)dm.CreateDelegate(typeof(FastSpanInvoker));
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = EmitSiteJustification)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = EmitSiteJustification)]
        private static FastSpanConstructorInvoker EmitSpanConstructor(ConstructorInfo ctor)
        {
            var dt = DeclaringType(ctor);
            var ps = ctor.GetParameters();
            var dm = EmitHelper.CreateDynamicMethod(
                $"FastReflection_span_new_{dt.Name}",
                typeof(object), new[] { typeof(Span<object?>) }, dt);
            var il = dm.GetILGenerator();
            EmitHelper.EmitCheckSpan(il, 0, ps.Length);
            var byrefs = EmitParameters(il, ps, 0, span: true);
            il.Emit(OpCodes.Newobj, ctor);
            EmitWriteBack(il, byrefs, 0, span: true);
            if (dt.IsValueType) il.Emit(OpCodes.Box, dt);
            il.Emit(OpCodes.Ret);
            return (FastSpanConstructorInvoker)dm.CreateDelegate(typeof(FastSpanConstructorInvoker));
        }
#endif

        [UnconditionalSuppressMessage("Trimming", "IL2090", Justification = "TDelegate's Invoke method is part of the #9 contract (DynamicallyAccessedMembers), finalized in #38.")]
        internal static TDelegate CreateTypedInvoker<TDelegate>(MethodInfo method) where TDelegate : Delegate
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            ThrowIfOpenGeneric(method);
            if (method.ContainsGenericParameters)
                throw new ArgumentException("Open generic methods must be constructed via MakeGenericMethod first.", nameof(method));

            try
            {
                return (TDelegate)(object)method.CreateDelegate(typeof(TDelegate));
            }
            catch (Exception e) when (EmitHelper.IsDelegateBindFailure(e))
            {
            }

            var invoke = typeof(TDelegate).GetMethod("Invoke")
                ?? throw new ArgumentException("Delegate type has no Invoke method.", nameof(method));
            EnsureTypedSignature(method, invoke);
            if (!EmitHelper.DynamicCodeSupported)
                throw new NotSupportedException(
                    "This delegate requires dynamic code. CreateDelegate binds a public exact signature without it; Ref<T> adaptation and non-public methods that CreateDelegate cannot bind do not.");
            return EmitTypedInvoker<TDelegate>(method, invoke);
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = EmitSiteJustification)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = EmitSiteJustification)]
        [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Ref<T>.Value is kept by the #9 contract (DynamicDependency), finalized in #38.")]
        private static TDelegate EmitTypedInvoker<TDelegate>(MethodInfo method, MethodInfo invoke) where TDelegate : Delegate
        {
            var dt = DeclaringType(method);
            var dps = invoke.GetParameters();
            var mps = method.GetParameters();
            var paramTypes = new Type[dps.Length];
            for (int i = 0; i < dps.Length; i++)
                paramTypes[i] = dps[i].ParameterType;

            var dm = EmitHelper.CreateDynamicMethod(
                $"FastReflection_typed_{dt.Name}_{method.Name}",
                invoke.ReturnType == typeof(void) ? null : invoke.ReturnType,
                paramTypes, dt);
            var il = dm.GetILGenerator();

            int offset = 0;
            if (!method.IsStatic)
            {
                offset = 1;
                EmitLoadTypedTarget(il, dps[0].ParameterType);
            }

            for (int i = 0; i < mps.Length; i++)
            {
                int arg = i + offset;
                il.Emit(OpCodes.Ldarg, arg);
                if (!mps[i].ParameterType.IsByRef)
                    continue;

                // Exact by-ref already has the caller's address on the stack.
                // Ref<T> adaptation addresses Value so the method writes it in place.
                var held = dps[arg].ParameterType.GetElementType()!;
                if (held == mps[i].ParameterType.GetElementType())
                    continue;
                var field = held.GetField(nameof(Ref<int>.Value))!;
                il.Emit(OpCodes.Ldflda, field);
            }

            EmitHelper.EmitCall(il, method);
            il.Emit(OpCodes.Ret);
            return (TDelegate)(object)dm.CreateDelegate(typeof(TDelegate));
        }

        /// <summary>
        /// Loads the instance argument. A by-ref class target is the address of the reference slot
        /// and must be dereferenced before <c>callvirt</c>. A by-value struct is passed by address.
        /// </summary>
        private static void EmitLoadTypedTarget(ILGenerator il, Type targetType)
        {
            if (!targetType.IsByRef)
            {
                if (targetType.IsValueType)
                {
                    il.Emit(OpCodes.Ldarga_S, (byte)0);
                }
                else
                {
                    il.Emit(OpCodes.Ldarg_0);
                    EmitCheckTypedTargetNotNull(il);
                }
                return;
            }

            il.Emit(OpCodes.Ldarg_0);
            var element = targetType.GetElementType()!;
            if (!element.IsValueType)
            {
                il.Emit(OpCodes.Ldind_Ref);
                EmitCheckTypedTargetNotNull(il);
            }
        }

        /// <summary>Emits <c>if (loadedTarget == null) throw new ArgumentNullException("target")</c> on the reference on the stack.</summary>
        private static void EmitCheckTypedTargetNotNull(ILGenerator il)
        {
            var notNull = il.DefineLabel();
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, notNull);
            il.Emit(OpCodes.Ldstr, "target");
            il.Emit(OpCodes.Newobj, typeof(ArgumentNullException).GetConstructor(new[] { typeof(string) })!);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(notNull);
        }

        private static void EnsureTypedSignature(MethodInfo method, MethodInfo invoke)
        {
            if (invoke.ReturnType != method.ReturnType)
                throw Mismatch(method);

            var dps = invoke.GetParameters();
            var mps = method.GetParameters();
            int offset = method.IsStatic ? 0 : 1;
            if (dps.Length != mps.Length + offset)
                throw Mismatch(method);

            if (!method.IsStatic)
                EnsureTarget(method, dps[0]);

            for (int i = 0; i < mps.Length; i++)
                EnsureArgument(method, mps[i], dps[i + offset]);
        }

        private static void EnsureTarget(MethodInfo method, ParameterInfo target)
        {
            var actual = target.ParameterType;
            if (!actual.IsByRef && IsRefHolder(actual))
                throw ByValueRef(method);
            var type = actual.IsByRef ? actual.GetElementType()! : actual;
            if (type != DeclaringType(method))
                throw Mismatch(method);
        }

        private static void EnsureArgument(MethodInfo method, ParameterInfo methodParam, ParameterInfo delegateParam)
        {
            var delegateType = delegateParam.ParameterType;
            if (!delegateType.IsByRef && IsRefHolder(delegateType))
                throw ByValueRef(method);

            if (!methodParam.ParameterType.IsByRef)
            {
                if (delegateType != methodParam.ParameterType)
                    throw Mismatch(method);
                return;
            }

            if (!delegateType.IsByRef)
                throw Mismatch(method);

            var held = delegateType.GetElementType()!;
            var methodElement = methodParam.ParameterType.GetElementType()!;
            bool exact = held == methodElement;
            bool adapted = !exact && IsRefHolder(held, out var valueType) && valueType == methodElement;
            if (!exact && !adapted)
                throw Mismatch(method);
            if (methodParam.IsOut != delegateParam.IsOut || methodParam.IsIn != delegateParam.IsIn)
                throw Mismatch(method);
        }

        private static bool IsRefHolder(Type type) => IsRefHolder(type, out _);

        private static bool IsRefHolder(Type type, out Type valueType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Ref<>))
            {
                valueType = type.GetGenericArguments()[0];
                return true;
            }

            valueType = null!;
            return false;
        }

        private static ArgumentException ByValueRef(MethodInfo method)
            => new("Ref<T> must be passed by ref, out, or in. Passing it by value drops writes.", nameof(method));

        private static ArgumentException Mismatch(MethodInfo method)
            => new("Delegate signature does not match this method. By-ref parameters must match exactly, or be ref, out, or in Ref<T>; every other parameter must match exactly.", nameof(method));

        // ============================== shared emit pieces ==============================

        private readonly struct ByRefLocal
        {
            internal readonly int Index;
            internal readonly LocalBuilder Local;
            internal readonly Type ElementType;
            internal readonly bool WriteBack;

            internal ByRefLocal(int index, LocalBuilder local, Type elementType, bool writeBack)
            {
                Index = index;
                Local = local;
                ElementType = elementType;
                WriteBack = writeBack;
            }
        }

        /// <summary>
        /// Emits loads for all parameters. By-ref parameters are materialized into locals
        /// (initialized from <c>args[i]</c> when non-null) and passed by <c>ldloca</c>.
        /// <paramref name="span"/> reads a <c>Span&lt;object?&gt;</c> argument instead of an object array (net8.0).
        /// </summary>
        private static List<ByRefLocal>? EmitParameters(ILGenerator il, ParameterInfo[] ps, int argsIndex, bool span = false)
        {
            List<ByRefLocal>? byrefs = null;
            for (int i = 0; i < ps.Length; i++)
            {
                var pt = ps[i].ParameterType;
                if (!pt.IsByRef)
                {
                    EmitLoadArgument(il, argsIndex, i, pt, span);
                    continue;
                }

                var el = pt.GetElementType()!;
                var loc = il.DeclareLocal(el);
                var doInit = il.DefineLabel();
                var done = il.DefineLabel();

                // if (args[i] != null) loc = (el)args[i];
                EmitLoadArgumentObject(il, argsIndex, i, span);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brtrue_S, doInit);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Br_S, done);
                il.MarkLabel(doInit);
                EmitHelper.EmitConvertFromObject(il, el);
                il.Emit(OpCodes.Stloc, loc);
                il.MarkLabel(done);

                il.Emit(OpCodes.Ldloca, loc);
                (byrefs ??= new List<ByRefLocal>()).Add(new ByRefLocal(i, loc, el, !ps[i].IsIn));
            }
            return byrefs;
        }

        /// <summary>Writes by-ref locals back to <c>args[i]</c> (boxed). Skipped for <c>in</c> parameters.</summary>
        private static void EmitWriteBack(ILGenerator il, List<ByRefLocal>? locals, int argsIndex, bool span = false)
        {
            if (locals == null) return;
            foreach (var b in locals)
            {
                if (!b.WriteBack) continue;
                if (span)
                {
#if NET8_0_OR_GREATER
                    EmitHelper.EmitStoreSpanElement(il, argsIndex, b.Index, b.Local, b.ElementType);
                    continue;
#else
                    throw new InvalidOperationException("Span invokers require net8.0.");
#endif
                }

                il.Emit(OpCodes.Ldarg, argsIndex);
                EmitHelper.EmitLdcI4(il, b.Index);
                il.Emit(OpCodes.Ldloc, b.Local);
                EmitHelper.EmitConvertToObject(il, b.ElementType);
                il.Emit(OpCodes.Stelem_Ref);
            }
        }

        private static void EmitLoadArgument(ILGenerator il, int argsIndex, int elementIndex, Type type, bool span)
        {
            if (span)
            {
#if NET8_0_OR_GREATER
                EmitHelper.EmitLoadSpanElement(il, argsIndex, elementIndex, type);
                return;
#else
                throw new InvalidOperationException("Span invokers require net8.0.");
#endif
            }

            EmitHelper.EmitLoadArrayElement(il, argsIndex, elementIndex, type);
        }

        private static void EmitLoadArgumentObject(ILGenerator il, int argsIndex, int elementIndex, bool span)
        {
            if (span)
            {
#if NET8_0_OR_GREATER
                EmitHelper.EmitLoadSpanElementObject(il, argsIndex, elementIndex);
                return;
#else
                throw new InvalidOperationException("Span invokers require net8.0.");
#endif
            }

            il.Emit(OpCodes.Ldarg, argsIndex);
            EmitHelper.EmitLdcI4(il, elementIndex);
            il.Emit(OpCodes.Ldelem_Ref);
        }

        /// <summary>Loads a member value to object; dereferences by-ref member types (ref-returning properties).</summary>
        private static void EmitLoadMemberValue(ILGenerator il, Type memberType)
            => EmitHelper.EmitConvertToObject(il, memberType);

        // ============================== helpers ==============================

        private static bool IsReadOnlyRef(MethodInfo get)
        {
            // 'ref readonly' returns carry InAttribute (and, depending on compiler, IsReadOnlyAttribute) modreq.
            foreach (var t in get.ReturnParameter.GetRequiredCustomModifiers())
                if (t.Name is "InAttribute" or "IsReadOnlyAttribute")
                    return true;
            return false;
        }

        private static void ThrowIfIndexer(PropertyInfo property)
        {
            if (property.GetIndexParameters().Length != 0)
                throw new NotSupportedException(
                    "Indexer properties cannot be used as member accessors; use CreateInvoker on the accessor method instead.");
        }

        private static void ThrowIfOpenGeneric(MemberInfo member)
        {
            if (member.DeclaringType?.ContainsGenericParameters == true)
                throw new ArgumentException("Members of open generic types are not supported; close the generic type first.", nameof(member));
        }

        private static Type DeclaringType(MemberInfo member)
            => member.DeclaringType ?? throw new ArgumentException("Member has no declaring type.", nameof(member));
    }
}
