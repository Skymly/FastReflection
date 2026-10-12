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

        /// <summary>
        /// Loads argument <paramref name="argIndex"/> (declared <see cref="object"/>) onto the stack as
        /// <paramref name="declaringType"/>: <c>castclass</c> for reference types, <c>unbox</c>
        /// (managed pointer into the box — writes to value-type members persist) otherwise.
        /// </summary>
        internal static void EmitLoadTarget(ILGenerator il, Type declaringType, int argIndex = 0)
        {
            il.Emit(OpCodes.Ldarg, argIndex);
            if (declaringType.IsValueType || declaringType.IsGenericParameter)
                il.Emit(OpCodes.Unbox, declaringType);
            else
                il.Emit(OpCodes.Castclass, declaringType);
        }

        /// <summary>Converts the boxed value on the stack to <paramref name="type"/>.</summary>
        internal static void EmitConvertFromObject(ILGenerator il, Type type)
        {
            if (type.IsByRef) type = type.GetElementType()!;
            if (type.IsValueType || type.IsGenericParameter)
                il.Emit(OpCodes.Unbox_Any, type);
            else
                il.Emit(OpCodes.Castclass, type);
        }

        /// <summary>Converts the value on the stack to <see cref="object"/> (boxes when necessary).</summary>
        internal static void EmitConvertToObject(ILGenerator il, Type type)
        {
            if (type.IsByRef) type = type.GetElementType()!;
            if (type.IsValueType || type.IsGenericParameter)
                il.Emit(OpCodes.Box, type);
        }

        /// <summary>Emits <c>call</c> for statics/value-type methods, <c>callvirt</c> otherwise.</summary>
        internal static void EmitCall(ILGenerator il, MethodInfo method)
        {
            var op = method.IsStatic || (method.DeclaringType?.IsValueType ?? false)
                ? OpCodes.Call
                : OpCodes.Callvirt;
            il.EmitCall(op, method, null);
        }

        /// <summary>Loads <c>args[elementIndex]</c> from the object[] at <paramref name="argsParamIndex"/> and converts it to <paramref name="type"/>.</summary>
        internal static void EmitLoadArrayElement(ILGenerator il, int argsParamIndex, int elementIndex, Type type)
        {
            il.Emit(OpCodes.Ldarg, argsParamIndex);
            EmitLdcI4(il, elementIndex);
            il.Emit(OpCodes.Ldelem_Ref);
            EmitConvertFromObject(il, type);
        }

        /// <summary>
        /// Emits <c>if (args == null) throw new ArgumentNullException; if (args.Length &lt; required) throw new ArgumentException;</c>.
        /// </summary>
        internal static void EmitCheckArgs(ILGenerator il, int argsParamIndex, int requiredCount)
        {
            if (requiredCount == 0) return;

            var notNull = il.DefineLabel();
            var ok = il.DefineLabel();

            il.Emit(OpCodes.Ldarg, argsParamIndex);
            il.Emit(OpCodes.Brtrue_S, notNull);
            il.Emit(OpCodes.Ldstr, "args");
            il.Emit(OpCodes.Newobj, typeof(ArgumentNullException).GetConstructor(new[] { typeof(string) })!);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(notNull);

            il.Emit(OpCodes.Ldarg, argsParamIndex);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            EmitLdcI4(il, requiredCount);
            il.Emit(OpCodes.Bge_S, ok);
            il.Emit(OpCodes.Ldstr, "args does not contain enough elements for this call.");
            il.Emit(OpCodes.Newobj, typeof(ArgumentException).GetConstructor(new[] { typeof(string) })!);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(ok);
        }

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
