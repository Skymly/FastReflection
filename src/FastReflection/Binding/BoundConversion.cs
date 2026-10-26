using System.Globalization;
using System.Reflection;

namespace FastReflection.Binding
{
    /// <summary>
    /// The bound-call conversion contract: the sanctioned conversion set shared by overload
    /// applicability, per-argument conversion, and <see cref="ReflectionExtensions.CreateConverter"/>.
    /// Identity, reference assignment, unboxing, <see cref="Nullable{T}"/> lift, enum↔underlying,
    /// and spec §10.2.3 primitive widening — including <c>→decimal</c>, excluding <c>byte→char</c>.
    /// User-defined <c>op_Implicit</c>/<c>op_Explicit</c> methods are never consulted.
    /// <see cref="FastReflection.ReflectionExtensions.CreateConverter(Type, Type)"/> uses the wider spec <em>explicit</em> numeric set (narrowing included).
    /// </summary>
    internal static class BoundConversion
    {
        // Spec §10.2.3 implicit numeric conversions. Char is a conversion SOURCE only —
        // every numeric→char conversion is explicit, so byte→char is absent here.
        internal static bool IsWideningNumeric(Type from, Type to)
        {
            // Enum types report their underlying TypeCode — enum↔underlying is its own
            // sanctioned conversion, never a numeric widening.
            if (from.IsEnum || to.IsEnum) return false;
            switch (Type.GetTypeCode(from))
            {
                case TypeCode.SByte:
                    return Is(to, TypeCode.Int16, TypeCode.Int32, TypeCode.Int64,
                              TypeCode.Single, TypeCode.Double, TypeCode.Decimal);
                case TypeCode.Byte:
                    return Is(to, TypeCode.Int16, TypeCode.UInt16, TypeCode.Int32, TypeCode.UInt32,
                              TypeCode.Int64, TypeCode.UInt64, TypeCode.Single, TypeCode.Double, TypeCode.Decimal);
                case TypeCode.Int16:
                    return Is(to, TypeCode.Int32, TypeCode.Int64,
                              TypeCode.Single, TypeCode.Double, TypeCode.Decimal);
                case TypeCode.UInt16:
                    return Is(to, TypeCode.Int32, TypeCode.UInt32, TypeCode.Int64, TypeCode.UInt64,
                              TypeCode.Single, TypeCode.Double, TypeCode.Decimal);
                case TypeCode.Int32:
                    return Is(to, TypeCode.Int64, TypeCode.Single, TypeCode.Double, TypeCode.Decimal);
                case TypeCode.UInt32:
                    return Is(to, TypeCode.Int64, TypeCode.UInt64,
                              TypeCode.Single, TypeCode.Double, TypeCode.Decimal);
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    return Is(to, TypeCode.Single, TypeCode.Double, TypeCode.Decimal);
                case TypeCode.Char:
                    return Is(to, TypeCode.UInt16, TypeCode.Int32, TypeCode.UInt32,
                              TypeCode.Int64, TypeCode.UInt64, TypeCode.Single, TypeCode.Double, TypeCode.Decimal);
                case TypeCode.Single:
                    return to == typeof(double);
                default:
                    return false;
            }

            static bool Is(Type target, params TypeCode[] codes)
            {
                var code = Type.GetTypeCode(target);
                foreach (var c in codes)
                    if (c == code) return true;
                return false;
            }
        }

        /// <summary>Every primitive numeric type the spec's explicit numeric conversions cover (char included; bool and DateTime excluded).</summary>
        internal static bool IsNumeric(Type type)
        {
            if (type.IsEnum) return false;
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                case TypeCode.Char:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsNullableTarget(Type type)
            => !type.IsValueType || Nullable.GetUnderlyingType(type) != null;

        /// <summary>
        /// Type-level applicability of the sanctioned set: can an argument whose runtime type is
        /// <paramref name="source"/> (null for a null literal) ever convert to <paramref name="target"/>?
        /// Answers binding-time questions only; the runtime value decides stage 5.
        /// </summary>
        internal static bool IsApplicable(Type? source, Type target)
        {
            if (target.IsByRef)
            {
                // By-ref binds by element type; a null slot supplies default(element) per the
                // shared machinery's null-in-slot rule.
                if (source == null) return true;
                target = target.GetElementType()!;
            }

            if (source == null) return IsNullableTarget(target);
            if (source == target) return true;
            if (target.IsAssignableFrom(source)) return true;
            if (IsWideningNumeric(source, target)) return true;

            var nullable = Nullable.GetUnderlyingType(target);
            if (nullable != null)
                return source == nullable || IsWideningNumeric(source, nullable);

            if (!target.IsValueType) return false;

            // Unboxing: an interface/base-type source (object, ValueType, Enum, implemented interfaces).
            if (source.IsAssignableFrom(target)) return true;

            return IsEnumPair(source, target);
        }

        /// <summary>
        /// Per-argument bound-call conversion (stage 5): the sanctioned set, widening-only numerics.
        /// By-ref targets take the element type; a null by-ref slot supplies its default.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2072",
            Justification = "A by-ref element type is always creatable for its default value; the #9 contract is finalized in #38.")]
        internal static object? Convert(object? value, Type target)
        {
            if (target.IsByRef)
            {
                var element = target.GetElementType()!;
                if (value == null)
                    return element.IsValueType ? Activator.CreateInstance(element) : null;
                return Convert(value, element);
            }

            if (value == null)
            {
                if (IsNullableTarget(target)) return null;
                throw ConversionFailure(null, target);
            }

            var source = value.GetType();
            if (source == target) return value;

            var nullable = Nullable.GetUnderlyingType(target);
            if (nullable != null)
            {
                if (source == nullable) return value;
                if (IsWideningNumeric(source, nullable))
                    return ChangeNumeric(value, nullable);
                throw ConversionFailure(value, target);
            }

            if (!target.IsValueType)
                return target.IsInstanceOfType(value) ? value : throw ConversionFailure(value, target);

            if (IsWideningNumeric(source, target))
                return ChangeNumeric(value, target);

            if (IsEnumPair(source, target))
            {
                return target.IsEnum
                    ? Enum.ToObject(target, System.Convert.ChangeType(value, Enum.GetUnderlyingType(target), CultureInfo.InvariantCulture))
                    : System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            }

            throw ConversionFailure(value, target);
        }

        /// <summary>
        /// Type-level applicability for <see cref="ReflectionExtensions.CreateConverter"/>:
        /// the sanctioned set plus spec <em>explicit</em> numeric conversions and reference casts
        /// in either direction (the runtime value decides).
        /// </summary>
        internal static bool CanCreateConverter(Type source, Type target)
        {
            if (source == target) return true;
            if (target.IsAssignableFrom(source) || source.IsAssignableFrom(target)) return true;
            // Reference casts the runtime value may satisfy: anything out of object, or to/from an interface.
            if (source == typeof(object)
                || (!target.IsValueType && (source.IsInterface || target.IsInterface))) return true;
            if (IsNumeric(source) && IsNumeric(target)) return true;

            var targetNullable = Nullable.GetUnderlyingType(target);
            var sourceNullable = Nullable.GetUnderlyingType(source);
            if (targetNullable != null || sourceNullable != null)
            {
                var t = targetNullable ?? target;
                var s = sourceNullable ?? source;
                if (s == t || (IsNumeric(s) && IsNumeric(t))) return true;
            }

            if (IsEnumPair(source, target)) return true;

            return false;
        }

        /// <summary>The <see cref="ReflectionExtensions.CreateConverter"/> runtime conversion: the explicit set.</summary>
        internal static object? ConvertExplicit(object? value, Type source, Type target)
        {
            if (value == null)
            {
                if (IsNullableTarget(target)) return null;
                throw ConversionFailure(null, target);
            }

            if (target.IsInstanceOfType(value)) return value;

            var valueType = value.GetType();
            var nullable = Nullable.GetUnderlyingType(target);
            var effective = nullable ?? target;
            var effectiveSource = Nullable.GetUnderlyingType(valueType) ?? valueType;

            if (IsEnumPair(effectiveSource, effective))
            {
                return effective.IsEnum
                    ? Enum.ToObject(effective, System.Convert.ChangeType(value, Enum.GetUnderlyingType(effective), CultureInfo.InvariantCulture))
                    : System.Convert.ChangeType(value, effective, CultureInfo.InvariantCulture);
            }
            if (IsNumeric(effectiveSource) && IsNumeric(effective))
            {
                // Boxed Nullable<T> reads as T; the Nullable lift/unlift rides the boxing rules.
                try
                {
                    return System.Convert.ChangeType(value, effective, CultureInfo.InvariantCulture);
                }
                catch (Exception e) when (!(e is InvalidCastException))
                {
                    throw ConversionFailure(value, target);
                }
            }

            throw ConversionFailure(value, target);
        }

        /// <summary>Whether <paramref name="source"/> and <paramref name="target"/> form an enum↔underlying pair.</summary>
        internal static bool IsEnumPair(Type source, Type target)
        {
            if (target.IsEnum)
            {
                var underlying = Enum.GetUnderlyingType(target);
                return source == underlying
                    || (source.IsEnum && Enum.GetUnderlyingType(source) == underlying);
            }
            return source.IsEnum && Enum.GetUnderlyingType(source) == target;
        }

        private static object ChangeNumeric(object value, Type target)
            => System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);

        internal static InvalidCastException ConversionFailure(object? value, Type target)
            => new(value == null
                ? $"Null cannot be converted to the non-nullable value type '{target}'."
                : $"Object of type '{value.GetType()}' cannot be converted to type '{target}'.");
    }
}
