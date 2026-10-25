using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace FastReflection.Emit
{
    /// <summary>
    /// Completes an access with ordinary reflection under the same exception contract as the emit path:
    /// strict CLR-rule conversions (no numeric widening or narrowing), unwrapped callee exceptions,
    /// and by-ref values written back only on success and only for <see langword="ref"/>/<see langword="out"/>.
    /// </summary>
    internal static class ReflectionFallback
    {
        // ============================== fields and property accessors ==============================

        internal static object? Get(FieldInfo field, object? target)
        {
            CheckTarget(field, target);
            return field.GetValue(target);
        }

        internal static void Set(FieldInfo field, object? target, object? value)
        {
            CheckTarget(field, target);
            field.SetValue(target, Convert(value, field.FieldType));
        }

        internal static object? Get(MethodInfo get, object? target)
        {
            CheckTarget(get, target);
            return InvokeUnwrapped(get, target, null);
        }

        internal static void Set(MethodInfo set, object? target, object? value)
        {
            CheckTarget(set, target);
            InvokeUnwrapped(set, target, new[] { Convert(value, set.GetParameters()[0].ParameterType) });
        }

        // ============================== methods and constructors ==============================

        internal static object? Invoke(MethodInfo method, object? target, object?[]? args)
            => InvokeCore(method, target, checkTarget: true, args);

        internal static object? Invoke(ConstructorInfo ctor, object?[]? args)
            => InvokeCore(ctor, null, checkTarget: false, args);

        internal static object? New(ConstructorInfo ctor)
            => InvokeCore(ctor, null, checkTarget: false, null);

        [UnconditionalSuppressMessage("Trimming", "IL2067",
            Justification = "The constructor requirements of the caller-supplied type are part of the #9 contract, finalized in #38.")]
        internal static object? New(Type type)
        {
            try
            {
                return Activator.CreateInstance(type);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

#if NET8_0_OR_GREATER
        internal static object? Invoke(MethodInfo method, object? target, Span<object?> args)
            => InvokeSpanCore(method, target, checkTarget: true, args);

        internal static object? Invoke(ConstructorInfo ctor, Span<object?> args)
            => InvokeSpanCore(ctor, null, checkTarget: false, args);
#endif

        // ============================== invoke cores ==============================

        private static object? InvokeCore(MethodBase member, object? target, bool checkTarget, object?[]? args)
        {
            var ps = member.GetParameters();
            var converted = PrepareCall(ps, args);
            if (checkTarget) CheckTarget(member, target);
            var writeBack = converted == null ? null : ConvertArgs(ps, args!, converted);
            var result = InvokeUnwrapped(member, target, converted);
            if (args != null && writeBack != null)
                foreach (var i in writeBack) args[i] = converted![i];
            return result;
        }

#if NET8_0_OR_GREATER
        private static object? InvokeSpanCore(MethodBase member, object? target, bool checkTarget, Span<object?> args)
        {
            var ps = member.GetParameters();
            if (args.Length != ps.Length)
                throw new ArgumentException(EmitHelper.ArgsLengthMessage, "args");
            if (checkTarget) CheckTarget(member, target);
            var converted = new object?[ps.Length];
            var writeBack = ConvertArgs(ps, args.ToArray(), converted);
            var result = InvokeUnwrapped(member, target, converted);
            if (writeBack != null)
                foreach (var i in writeBack) args[i] = converted[i];
            return result;
        }
#endif

        /// <summary>Invokes and unwraps a callee exception out of <see cref="TargetInvocationException"/>.</summary>
        private static object? InvokeUnwrapped(MethodBase member, object? target, object?[]? args)
        {
            try
            {
                // MethodBase.Invoke(obj, args) runs a .ctor on an existing target; constructors
                // must use the allocating overload.
                return member is ConstructorInfo ctor
                    ? ctor.Invoke(args)
                    : member.Invoke(target, args);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        // ============================== contract pieces ==============================

        private static void CheckTarget(MethodBase member, object? target)
        {
            if (member.IsStatic) return;
            CheckTargetCore(member.DeclaringType!, target);
        }

        private static void CheckTarget(FieldInfo field, object? target)
        {
            if (field.IsStatic) return;
            CheckTargetCore(field.DeclaringType!, target);
        }

        private static void CheckTargetCore(Type declaringType, object? target)
        {
            if (target == null)
                throw new ArgumentNullException("target");
            if (!declaringType.IsInstanceOfType(target))
                throw new InvalidCastException(
                    $"Object of type '{target.GetType()}' cannot be converted to type '{declaringType}'.");
        }

        /// <summary>Checks null and length, then allocates the converted-arguments array.</summary>
        private static object?[]? PrepareCall(ParameterInfo[] ps, object?[]? args)
        {
            if (args == null)
            {
                if (ps.Length != 0)
                    throw new ArgumentNullException("args");
                return null;
            }
            if (args.Length != ps.Length)
                throw new ArgumentException(EmitHelper.ArgsLengthMessage, "args");
            return new object?[ps.Length];
        }

        /// <summary>
        /// Converts every slot of <paramref name="args"/> into <paramref name="converted"/>:
        /// by-value arguments by the contract rule; a non-null by-ref slot by that same rule to the
        /// element type; a null by-ref slot to a default of the element type so the callee reads it.
        /// Returns the indices written back to the caller (ref and out only — never in).
        /// </summary>
        [UnconditionalSuppressMessage("Trimming", "IL2072",
            Justification = "A by-ref element type is always creatable for its default value; the #9 contract is finalized in #38.")]
        private static List<int>? ConvertArgs(ParameterInfo[] ps, object?[] args, object?[] converted)
        {
            List<int>? writeBack = null;
            for (int i = 0; i < ps.Length; i++)
            {
                var pt = ps[i].ParameterType;
                if (!pt.IsByRef)
                {
                    converted[i] = Convert(args[i], pt);
                    continue;
                }

                var el = pt.GetElementType()!;
                converted[i] = args[i] == null
                    ? (el.IsValueType ? Activator.CreateInstance(el) : null)
                    : Convert(args[i], el);
                if (!ps[i].IsIn)
                    (writeBack ??= new List<int>()).Add(i);
            }
            return writeBack;
        }

        /// <summary>
        /// The contract conversion: CLR <c>castclass</c>/<c>unbox.any</c> — reference assignment,
        /// <see cref="Nullable{T}"/> and its underlying type in either direction, an enum and its
        /// underlying integral type, and enums sharing that underlying type. No numeric widening or narrowing.
        /// </summary>
        internal static object? Convert(object? value, Type target)
        {
            if (target.IsByRef) target = target.GetElementType()!;
            if (value == null)
            {
                if (target.IsValueType && Nullable.GetUnderlyingType(target) == null)
                    throw ConversionFailure(null, target);
                return null;
            }

            var valueType = value.GetType();
            if (valueType == target) return value;

            if (!target.IsValueType)
                return target.IsInstanceOfType(value) ? value : throw ConversionFailure(value, target);

            var nullable = Nullable.GetUnderlyingType(target);
            if (nullable != null)
            {
                // unbox.any Nullable<U> accepts only a box of U.
                if (valueType == nullable) return value;
                throw ConversionFailure(value, target);
            }

            if (target.IsEnum)
            {
                var enumUnderlying = Enum.GetUnderlyingType(target);
                if (valueType == enumUnderlying || (valueType.IsEnum && Enum.GetUnderlyingType(valueType) == enumUnderlying))
                    return Enum.ToObject(target, System.Convert.ChangeType(value, enumUnderlying, CultureInfo.InvariantCulture));
            }
            else if (valueType.IsEnum && Enum.GetUnderlyingType(valueType) == target)
            {
                return System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            }

            throw ConversionFailure(value, target);
        }

        private static InvalidCastException ConversionFailure(object? value, Type target)
            => new(value == null
                ? $"Null cannot be converted to the non-nullable value type '{target}'."
                : $"Object of type '{value.GetType()}' cannot be converted to type '{target}'.");
    }
}
