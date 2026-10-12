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
        // ============================== properties ==============================

        internal static Func<object?, object?> CreateGetter(PropertyInfo property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            ThrowIfIndexer(property);
            ThrowIfOpenGeneric(property);
            var get = property.GetGetMethod(true)
                ?? throw new ArgumentException("Property has no get accessor.", nameof(property));
            if (!EmitHelper.DynamicCodeSupported)
                return target => get.Invoke(target, null);
            try
            {
                return EmitPropertyGetter(property, get);
            }
            catch
            {
                return target => get.Invoke(target, null);
            }
        }

        internal static Func<object?, object?> EmitPropertyGetter(PropertyInfo property, MethodInfo get)
        {
            var dt = DeclaringType(property);
            var dm = EmitHelper.CreateDynamicMethod(
                $"FastReflection_get_{dt.Name}_{property.Name}",
                typeof(object), new[] { typeof(object) }, dt);
            var il = dm.GetILGenerator();
            if (!get.IsStatic) EmitHelper.EmitLoadTarget(il, dt);
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
                    throw new ArgumentException("Property has no set accessor.", nameof(property));
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
                return (target, value) => set.Invoke(target, new[] { value });
            try
            {
                return EmitPropertySetter(property, set);
            }
            catch
            {
                return (target, value) => set.Invoke(target, new[] { value });
            }
        }

        internal static Action<object?, object?> EmitPropertySetter(PropertyInfo property, MethodInfo set)
        {
            var dt = DeclaringType(property);
            var dm = EmitHelper.CreateDynamicMethod(
                $"FastReflection_set_{dt.Name}_{property.Name}",
                null, new[] { typeof(object), typeof(object) }, dt);
            var il = dm.GetILGenerator();
            if (!set.IsStatic) EmitHelper.EmitLoadTarget(il, dt);
            il.Emit(OpCodes.Ldarg_1);
            EmitHelper.EmitConvertFromObject(il, property.PropertyType);
            EmitHelper.EmitCall(il, set);
            il.Emit(OpCodes.Ret);
            return (Action<object?, object?>)dm.CreateDelegate(typeof(Action<object?, object?>));
        }

        /// <summary>Emits <c>target => (elType)via-getter-reference = value</c> for ref-returning properties.</summary>
        private static Action<object?, object?> EmitPropertySetterViaRef(PropertyInfo property, MethodInfo get, Type elementType)
        {
            var dt = DeclaringType(property);
            var dm = EmitHelper.CreateDynamicMethod(
                $"FastReflection_setref_{dt.Name}_{property.Name}",
                null, new[] { typeof(object), typeof(object) }, dt);
            var il = dm.GetILGenerator();
            if (!get.IsStatic) EmitHelper.EmitLoadTarget(il, dt);
            EmitHelper.EmitCall(il, get);          // &T
            il.Emit(OpCodes.Ldarg_1);
            EmitHelper.EmitConvertFromObject(il, elementType);  // T
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
                return target => field.GetValue(target);
            try
            {
                return EmitFieldGetter(field);
            }
            catch
            {
                return target => field.GetValue(target);
            }
        }

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
                return (target, value) => field.SetValue(target, value);
            try
            {
                return EmitFieldSetter(field);
            }
            catch
            {
                return (target, value) => field.SetValue(target, value);
            }
        }

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
                return (target, args) => method.Invoke(target, args);
            try
            {
                return EmitInvoker(method);
            }
            catch
            {
                return (target, args) => method.Invoke(target, args);
            }
        }

        internal static FastInvoker EmitInvoker(MethodInfo method)
        {
            var dt = DeclaringType(method);
            var ps = method.GetParameters();
            var dm = EmitHelper.CreateDynamicMethod(
                $"FastReflection_invoke_{dt.Name}_{method.Name}",
                typeof(object), new[] { typeof(object), typeof(object[]) }, dt);
            var il = dm.GetILGenerator();
            EmitHelper.EmitCheckArgs(il, 1, ps.Length);
            if (!method.IsStatic) EmitHelper.EmitLoadTarget(il, dt);
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
                return args => ctor.Invoke(args);
            try
            {
                return EmitInvoker(ctor, ps);
            }
            catch
            {
                return args => ctor.Invoke(args);
            }
        }

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
                return () => ctor.Invoke(null);
            try
            {
                return EmitFactory(ctor);
            }
            catch
            {
                return () => ctor.Invoke(null);
            }
        }

        /// <summary>Factory for a type's parameterless constructor (any visibility when <paramref name="nonPublic"/>); structs get an initobj factory.</summary>
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
                    return () => Activator.CreateInstance(type);
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
                    return () => Activator.CreateInstance(type);
                }
            }

            throw new ArgumentException($"Type '{type}' has no parameterless constructor.", nameof(type));
        }

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
        /// </summary>
        private static List<ByRefLocal>? EmitParameters(ILGenerator il, ParameterInfo[] ps, int argsIndex)
        {
            List<ByRefLocal>? byrefs = null;
            for (int i = 0; i < ps.Length; i++)
            {
                var pt = ps[i].ParameterType;
                if (!pt.IsByRef)
                {
                    EmitHelper.EmitLoadArrayElement(il, argsIndex, i, pt);
                    continue;
                }

                var el = pt.GetElementType()!;
                var loc = il.DeclareLocal(el);
                var doInit = il.DefineLabel();
                var done = il.DefineLabel();

                // if (args[i] != null) loc = (el)args[i];
                il.Emit(OpCodes.Ldarg, argsIndex);
                EmitHelper.EmitLdcI4(il, i);
                il.Emit(OpCodes.Ldelem_Ref);
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
        private static void EmitWriteBack(ILGenerator il, List<ByRefLocal>? locals, int argsIndex)
        {
            if (locals == null) return;
            foreach (var b in locals)
            {
                if (!b.WriteBack) continue;
                il.Emit(OpCodes.Ldarg, argsIndex);
                EmitHelper.EmitLdcI4(il, b.Index);
                il.Emit(OpCodes.Ldloc, b.Local);
                EmitHelper.EmitConvertToObject(il, b.ElementType);
                il.Emit(OpCodes.Stelem_Ref);
            }
        }

        /// <summary>Loads a member value to object; dereferences by-ref member types (ref-returning properties).</summary>
        private static void EmitLoadMemberValue(ILGenerator il, Type memberType)
        {
            if (memberType.IsByRef)
            {
                var el = memberType.GetElementType()!;
                il.Emit(OpCodes.Ldobj, el);
                EmitHelper.EmitConvertToObject(il, el);
            }
            else
            {
                EmitHelper.EmitConvertToObject(il, memberType);
            }
        }

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
