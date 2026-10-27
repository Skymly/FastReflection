using System.Diagnostics.CodeAnalysis;

namespace FastReflection.Binding
{
    /// <summary>The argument-mapping recipe a resolved bound call replays on every invocation.</summary>
    internal sealed class BoundPlan
    {
        internal readonly ArgSlot[] Slots;

        internal BoundPlan(ArgSlot[] slots) => Slots = slots;
    }

    internal readonly struct ArgSlot
    {
        internal enum SourceKind : byte
        {
            /// <summary>Caller positional argument <see cref="Index"/>.</summary>
            Positional = 0,

            /// <summary>Caller named-argument value <see cref="Index"/> (in supplied order).</summary>
            Named = 1,

            /// <summary>The parameter's declared default <see cref="Default"/>.</summary>
            Default = 2,

            /// <summary>The <c>params</c> array: caller positional arguments <see cref="Index"/> onward, packed into a typed array.</summary>
            Packed = 3,
        }

        internal readonly SourceKind Source;

        /// <summary>Positional index, named-value index, or (for <see cref="SourceKind.Packed"/>) the first packed positional index.</summary>
        internal readonly int Index;

        /// <summary>The conversion target: the parameter type, or the <c>params</c> element type for <see cref="SourceKind.Packed"/>.</summary>
        internal readonly Type TargetType;

        /// <summary>The typed fill value for <see cref="SourceKind.Default"/>.</summary>
        internal readonly object? Default;

        /// <summary>A <see langword="ref"/>/<see langword="out"/> slot: on success the normalized value is written back to the caller's positional argument. <see langword="in"/> parameters do not write back.</summary>
        internal readonly bool WriteBack;

        internal ArgSlot(SourceKind source, int index, Type targetType, object? @default = null, bool writeBack = false)
        {
            Source = source;
            Index = index;
            TargetType = targetType;
            Default = @default;
            WriteBack = writeBack;
        }
    }

    /// <summary>
    /// A resolved bound call: the plan plus the adapted invoker, cached per
    /// <see cref="BoundSignature"/> on the type accessor. Execution order is the
    /// bound-call trigger order's call half — stage 3 null-args, stage 4 target rules,
    /// stage 5 per-argument sanctioned conversion, stage 6 unwrapped callee.
    /// </summary>
    internal sealed class BoundCall
    {
        private readonly BoundPlan _plan;
        private readonly Type? _instanceType;
        private readonly Func<object?, object?[]?, object?> _inner;

        internal BoundCall(BoundPlan plan, Type? instanceType, Func<object?, object?[]?, object?> inner)
        {
            _plan = plan;
            _instanceType = instanceType;
            _inner = inner;
        }

        internal object? Run(object? target, object?[]? args, object?[]? namedValues)
            => Run(target, args, args, namedValues);

        /// <param name="target">The call target; ignored when the bound member is static.</param>
        /// <param name="checkArgs">The caller's own argument array — the stage-3 null check applies to it.</param>
        /// <param name="args">The array the plan's positional slots index into.</param>
        /// <param name="namedValues">The named-argument values, in the order the plan's named slots index.</param>
        internal object? Run(object? target, object?[]? checkArgs, object?[]? args, object?[]? namedValues)
        {
            // Stage 3: null args fails unconditionally once binding succeeded.
            if (checkArgs == null)
                throw new ArgumentNullException(nameof(checkArgs));

            // Stage 4: target rules — instance member needs a compatible non-null target; static ignores it.
            if (_instanceType != null)
            {
                if (target == null)
                    throw new ArgumentNullException(nameof(target));
                if (!_instanceType.IsInstanceOfType(target))
                    throw new InvalidCastException(
                        $"Object of type '{target.GetType()}' cannot be converted to type '{_instanceType}'.");
            }

            // Stage 5: normalize + per-argument sanctioned conversion. Stage 6 runs inside _inner.
            var normalized = Normalize(args, namedValues);
            var result = _inner(target, normalized);

            // ref/out write-back follows the access machinery: positional slots only, on success
            // alone; in slots never write back; named-argument dictionaries cannot be written to.
            var slots = _plan.Slots;
            for (var i = 0; i < slots.Length; i++)
            {
                ref readonly var slot = ref slots[i];
                if (slot.WriteBack && slot.Source == ArgSlot.SourceKind.Positional)
                    args![slot.Index] = normalized![i];
            }
            return result;
        }

        private object?[]? Normalize(object?[]? args, object?[]? namedValues)
        {
            var slots = _plan.Slots;
            if (slots.Length == 0) return null;

            var normalized = new object?[slots.Length];
            for (var i = 0; i < slots.Length; i++)
            {
                ref readonly var slot = ref slots[i];
                switch (slot.Source)
                {
                    case ArgSlot.SourceKind.Positional:
                        normalized[i] = BoundConversion.Convert(args![slot.Index], slot.TargetType);
                        break;
                    case ArgSlot.SourceKind.Named:
                        normalized[i] = BoundConversion.Convert(namedValues![slot.Index], slot.TargetType);
                        break;
                    case ArgSlot.SourceKind.Packed:
                        normalized[i] = Pack(args!, slot.Index, slot.TargetType);
                        break;
                    default:
                        normalized[i] = slot.Default;
                        break;
                }
            }
            return normalized;
        }

        /// <summary>Packs caller positional arguments <paramref name="start"/> onward into a typed <c>params</c> array.</summary>
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "The params element type comes from a member of the caller-supplied rooted type; an array shape the AOT compiler did not emit fails at run time as a stage-2 bind miss.")]
        private static object Pack(object?[] args, int start, Type elementType)
        {
            var count = args.Length - start;
            var packed = Array.CreateInstance(elementType, count);
            for (var i = 0; i < count; i++)
                packed.SetValue(BoundConversion.Convert(args[start + i], elementType), i);
            return packed;
        }
    }
}
