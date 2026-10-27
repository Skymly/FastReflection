using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace FastReflection.Emit
{
    /// <summary>Shared DynamicMethod factory and IL micro-helpers.</summary>
    internal static class EmitHelper
    {
        internal const string DynamicCodeMessage =
            "FastReflection compiles delegates via DynamicMethod, which requires dynamic code generation and is not available under full AOT/IL trimming.";

        /// <summary>The wrong-length message shared by the emitted checks and the reflection path.</summary>
        internal const string ArgsLengthMessage = "args length does not match this call.";

        /// <summary>Justification shared by the suppression sites where a Type[] enumeration loses per-element DAM flow — the caller-supplied union roots every level.</summary>
        internal const string LevelEnumerationJustification =
            "Each enumerated level is kept by the caller-supplied type's TypeAccessor.Create union — member marking covers base-type members and Interfaces covers interface levels; per-element requirements on a Type[] return are inexpressible.";

        /// <summary>Justification shared by the sites defaulting a member-signature type — its parameterless constructor sits outside the TypeAccessor.Create union.</summary>
        internal const string MaterializedSignatureJustification =
            "A parameter or by-ref element type comes from a materialized member signature the caller already holds; its parameterless constructor is outside the TypeAccessor.Create union and a missing one surfaces as an ordinary reflection failure.";

        /// <summary>Justification shared by the sites instantiating generics from the caller's explicit type arguments.</summary>
        internal const string GenericInstantiationJustification =
            "Explicit generic type arguments come from the caller's bound call; a generic instantiation the AOT compiler did not emit fails at run time as a stage-2 bind miss.";

        private static readonly bool s_dynamicCode = ProbeDynamicCode();

        /// <summary>Test hook: force the reflection fallback path (settable from the test assembly).</summary>
        internal static volatile bool ForceReflectionFallback;

        /// <summary>True when runtime code generation is usable on this platform.</summary>
        internal static bool DynamicCodeSupported => s_dynamicCode && !ForceReflectionFallback;

        private static bool ProbeDynamicCode()
        {
#if NET8_0_OR_GREATER
            return RuntimeFeature.IsDynamicCodeSupported && RuntimeFeature.IsDynamicCodeCompiled;
#else
            // RuntimeFeature is unavailable on .NET Framework < 4.7.1 / netstandard2.0.
            // Probe once: desktop CLR always supports DynamicMethod; exotic partial-trust
            // hosts will surface false here and we fall back to plain reflection.
            try
            {
                var probe = new DynamicMethod("FastReflection_Probe", typeof(void), Type.EmptyTypes);
                probe.GetILGenerator().Emit(OpCodes.Ret);
                return probe.CreateDelegate(typeof(Action)) is Action;
            }
            catch
            {
                return false;
            }
#endif
        }

        /// <summary>Creates a collectible DynamicMethod bound to <paramref name="owner"/>'s module, skipping visibility checks.</summary>
        [RequiresDynamicCode(DynamicCodeMessage)]
        [RequiresUnreferencedCode(DynamicCodeMessage)]
        internal static DynamicMethod CreateDynamicMethod(string name, Type? returnType, Type[] parameterTypes, Type owner)
            => new(name, returnType, parameterTypes, owner, skipVisibility: true);

        /// <summary>Emits <c>if (arg == null) throw new ArgumentNullException("target")</c> for an instance-access target.</summary>
        [RequiresDynamicCode(DynamicCodeMessage)]
        [RequiresUnreferencedCode(DynamicCodeMessage)]
        internal static void EmitCheckTarget(ILGenerator il, int argIndex = 0)
        {
            var notNull = il.DefineLabel();
            il.Emit(OpCodes.Ldarg, argIndex);
            il.Emit(OpCodes.Brtrue_S, notNull);
            il.Emit(OpCodes.Ldstr, "target");
            il.Emit(OpCodes.Newobj, typeof(ArgumentNullException).GetConstructor(new[] { typeof(string) })!);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(notNull);
        }

        /// <summary>
        /// Loads argument <paramref name="argIndex"/> (declared <see cref="object"/>) onto the stack as
        /// <paramref name="declaringType"/>: <c>castclass</c> for reference types, <c>unbox</c>
        /// (managed pointer into the box — writes to value-type members persist) otherwise.
        /// </summary>
        [RequiresDynamicCode(DynamicCodeMessage)]
        [RequiresUnreferencedCode(DynamicCodeMessage)]
        internal static void EmitLoadTarget(ILGenerator il, Type declaringType, int argIndex = 0)
        {
            il.Emit(OpCodes.Ldarg, argIndex);
            if (declaringType.IsValueType || declaringType.IsGenericParameter)
                il.Emit(OpCodes.Unbox, declaringType);
            else
                il.Emit(OpCodes.Castclass, declaringType);
        }

        /// <summary>
        /// Converts the boxed value on the stack to <paramref name="type"/>.
        /// Null converts to <see cref="InvalidCastException"/> on a non-nullable value type.
        /// </summary>
        [RequiresDynamicCode(DynamicCodeMessage)]
        [RequiresUnreferencedCode(DynamicCodeMessage)]
        internal static void EmitConvertFromObject(ILGenerator il, Type type)
        {
            if (type.IsByRef) type = type.GetElementType()!;
            if (type.IsValueType || type.IsGenericParameter)
            {
                if (!type.IsGenericParameter && Nullable.GetUnderlyingType(type) == null)
                {
                    var notNull = il.DefineLabel();
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Brtrue_S, notNull);
                    il.Emit(OpCodes.Ldstr, "Null cannot be converted to a non-nullable value type.");
                    il.Emit(OpCodes.Newobj, typeof(InvalidCastException).GetConstructor(new[] { typeof(string) })!);
                    il.Emit(OpCodes.Throw);
                    il.MarkLabel(notNull);
                }
                il.Emit(OpCodes.Unbox_Any, type);
            }
            else
            {
                il.Emit(OpCodes.Castclass, type);
            }
        }

        /// <summary>
        /// Converts the value on the stack to <see cref="object"/> (boxes when necessary).
        /// A by-ref type is dereferenced first, so a <c>ref</c> return is boxed as its element value rather than as the address.
        /// </summary>
        [RequiresDynamicCode(DynamicCodeMessage)]
        [RequiresUnreferencedCode(DynamicCodeMessage)]
        internal static void EmitConvertToObject(ILGenerator il, Type type)
        {
            if (type.IsByRef)
            {
                type = type.GetElementType()!;
                il.Emit(OpCodes.Ldobj, type);
            }
            if (type.IsValueType || type.IsGenericParameter)
                il.Emit(OpCodes.Box, type);
        }

        internal static bool IsDelegateBindFailure(Exception e)
            => e is ArgumentException or MethodAccessException or MemberAccessException or MissingMethodException or InvalidOperationException;

        /// <summary>Returns <paramref name="emit"/>'s delegate, or <paramref name="fallback"/> when emit throws.</summary>
        internal static T EmitOrFallback<T>(Func<T> emit, T fallback)
        {
            try { return emit(); }
            catch { return fallback; }
        }

        /// <summary>Emits <c>call</c> for statics/value-type methods, <c>callvirt</c> otherwise.</summary>
        [RequiresDynamicCode(DynamicCodeMessage)]
        [RequiresUnreferencedCode(DynamicCodeMessage)]
        internal static void EmitCall(ILGenerator il, MethodInfo method)
        {
            var op = method.IsStatic || (method.DeclaringType?.IsValueType ?? false)
                ? OpCodes.Call
                : OpCodes.Callvirt;
            il.EmitCall(op, method, null);
        }

        /// <summary>Loads <c>args[elementIndex]</c> from the object[] at <paramref name="argsParamIndex"/> and converts it to <paramref name="type"/>.</summary>
        [RequiresDynamicCode(DynamicCodeMessage)]
        [RequiresUnreferencedCode(DynamicCodeMessage)]
        internal static void EmitLoadArrayElement(ILGenerator il, int argsParamIndex, int elementIndex, Type type)
        {
            il.Emit(OpCodes.Ldarg, argsParamIndex);
            EmitLdcI4(il, elementIndex);
            il.Emit(OpCodes.Ldelem_Ref);
            EmitConvertFromObject(il, type);
        }

        /// <summary>
        /// Emits <c>if (args == null) throw new ArgumentNullException; if (args.Length != required) throw new ArgumentException;</c>.
        /// When <paramref name="requiredCount"/> is 0 a null array is allowed but extra arguments are not.
        /// </summary>
        [RequiresDynamicCode(DynamicCodeMessage)]
        [RequiresUnreferencedCode(DynamicCodeMessage)]
        internal static void EmitCheckArgs(ILGenerator il, int argsParamIndex, int requiredCount)
        {
            var ok = il.DefineLabel();

            if (requiredCount == 0)
            {
                il.Emit(OpCodes.Ldarg, argsParamIndex);
                il.Emit(OpCodes.Brfalse_S, ok);
            }
            else
            {
                var notNull = il.DefineLabel();
                il.Emit(OpCodes.Ldarg, argsParamIndex);
                il.Emit(OpCodes.Brtrue_S, notNull);
                il.Emit(OpCodes.Ldstr, "args");
                il.Emit(OpCodes.Newobj, typeof(ArgumentNullException).GetConstructor(new[] { typeof(string) })!);
                il.Emit(OpCodes.Throw);
                il.MarkLabel(notNull);
            }

            il.Emit(OpCodes.Ldarg, argsParamIndex);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            EmitLdcI4(il, requiredCount);
            il.Emit(OpCodes.Beq_S, ok);
            il.Emit(OpCodes.Ldstr, ArgsLengthMessage);
            il.Emit(OpCodes.Newobj, typeof(ArgumentException).GetConstructor(new[] { typeof(string) })!);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(ok);
        }

#if NET8_0_OR_GREATER
        private static readonly MethodInfo s_spanGetItem =
            typeof(Span<object?>).GetProperty("Item")!.GetGetMethod()!;

        private static readonly MethodInfo s_spanGetLength =
            typeof(Span<object?>).GetProperty(nameof(Span<object?>.Length))!.GetGetMethod()!;

        /// <summary>Loads <c>args[elementIndex]</c> from the <see cref="Span{T}"/> at <paramref name="argsParamIndex"/> and converts it to <paramref name="type"/>.</summary>
        [RequiresDynamicCode(DynamicCodeMessage)]
        [RequiresUnreferencedCode(DynamicCodeMessage)]
        internal static void EmitLoadSpanElement(ILGenerator il, int argsParamIndex, int elementIndex, Type type)
        {
            EmitLoadSpanElementObject(il, argsParamIndex, elementIndex);
            EmitConvertFromObject(il, type);
        }

        /// <summary>Pushes the object reference stored at <c>args[elementIndex]</c> without unboxing.</summary>
        [RequiresDynamicCode(DynamicCodeMessage)]
        [RequiresUnreferencedCode(DynamicCodeMessage)]
        internal static void EmitLoadSpanElementObject(ILGenerator il, int argsParamIndex, int elementIndex)
        {
            il.Emit(OpCodes.Ldarga_S, (byte)argsParamIndex);
            EmitLdcI4(il, elementIndex);
            il.Emit(OpCodes.Call, s_spanGetItem);
            il.Emit(OpCodes.Ldind_Ref);
        }

        /// <summary>Boxes <paramref name="local"/> and stores it into <c>args[elementIndex]</c>.</summary>
        [RequiresDynamicCode(DynamicCodeMessage)]
        [RequiresUnreferencedCode(DynamicCodeMessage)]
        internal static void EmitStoreSpanElement(ILGenerator il, int argsParamIndex, int elementIndex, LocalBuilder local, Type elementType)
        {
            il.Emit(OpCodes.Ldarga_S, (byte)argsParamIndex);
            EmitLdcI4(il, elementIndex);
            il.Emit(OpCodes.Call, s_spanGetItem);
            il.Emit(OpCodes.Ldloc, local);
            EmitConvertToObject(il, elementType);
            il.Emit(OpCodes.Stind_Ref);
        }

        /// <summary>Emits <c>if (args.Length != required) throw new ArgumentException;</c>. A span is never null.</summary>
        [RequiresDynamicCode(DynamicCodeMessage)]
        [RequiresUnreferencedCode(DynamicCodeMessage)]
        internal static void EmitCheckSpan(ILGenerator il, int argsParamIndex, int requiredCount)
        {
            var ok = il.DefineLabel();
            il.Emit(OpCodes.Ldarga_S, (byte)argsParamIndex);
            il.Emit(OpCodes.Call, s_spanGetLength);
            EmitLdcI4(il, requiredCount);
            il.Emit(OpCodes.Beq_S, ok);
            il.Emit(OpCodes.Ldstr, ArgsLengthMessage);
            il.Emit(OpCodes.Newobj, typeof(ArgumentException).GetConstructor(new[] { typeof(string) })!);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(ok);
        }
#endif

        [RequiresDynamicCode(DynamicCodeMessage)]
        [RequiresUnreferencedCode(DynamicCodeMessage)]
        internal static void EmitLdcI4(ILGenerator il, int value)
        {
            switch (value)
            {
                case -1: il.Emit(OpCodes.Ldc_I4_M1); break;
                case 0: il.Emit(OpCodes.Ldc_I4_0); break;
                case 1: il.Emit(OpCodes.Ldc_I4_1); break;
                case 2: il.Emit(OpCodes.Ldc_I4_2); break;
                case 3: il.Emit(OpCodes.Ldc_I4_3); break;
                case 4: il.Emit(OpCodes.Ldc_I4_4); break;
                case 5: il.Emit(OpCodes.Ldc_I4_5); break;
                case 6: il.Emit(OpCodes.Ldc_I4_6); break;
                case 7: il.Emit(OpCodes.Ldc_I4_7); break;
                case 8: il.Emit(OpCodes.Ldc_I4_8); break;
                default:
                    if (value >= sbyte.MinValue && value <= sbyte.MaxValue)
                        il.Emit(OpCodes.Ldc_I4_S, (sbyte)value);
                    else
                        il.Emit(OpCodes.Ldc_I4, value);
                    break;
            }
        }
    }
}
