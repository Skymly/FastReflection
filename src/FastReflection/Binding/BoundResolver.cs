using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using FastReflection.Emit;

namespace FastReflection.Binding
{
    /// <summary>
    /// The metadata-only bound-call resolver: candidate enumeration, the sanctioned-conversion
    /// applicability and betterness rules (spec §10.2.3 widening, spec §12.6.4-shaped overload
    /// resolution — normal form beats expanded, no-fill beats fill, more-specific conversions win),
    /// explicit generic type arguments with constraint checks, and named-argument reordering.
    /// User-defined conversions, type inference, const narrowing, and extension-method search never apply.
    /// </summary>
    internal static class BoundResolver
    {
        // ============================== candidate shape ==============================

        private readonly struct ParamView
        {
            internal readonly Type Type;
            internal readonly string? Name;
            internal readonly bool IsOptional;
            internal readonly object? Default;
            internal readonly bool IsParams;

            /// <summary>A by-ref parameter that writes back: <see langword="ref"/> and <see langword="out"/>, never <see langword="in"/>.</summary>
            internal readonly bool WritableByRef;

            internal ParamView(Type type, string? name, bool isOptional, object? @default, bool isParams, bool writableByRef = false)
            {
                Type = type;
                Name = name;
                IsOptional = isOptional;
                Default = @default;
                IsParams = isParams;
                WritableByRef = writableByRef;
            }
        }

        private sealed class Candidate
        {
            internal MethodBase? Method;      // method, constructor, or accessor method — invoker path
            internal FieldInfo? Field;        // field get/set — accessor path
            internal PropertyInfo? RefWrite;  // get-only ref-returning property — set path
            internal ParamView[] Params = null!;
            internal Type? InstanceType;      // null when the member is static or a constructor
        }

        [UnconditionalSuppressMessage("Trimming", "IL2072",
            Justification = "A parameter type is always creatable for its default value; the #9 contract is finalized in #38.")]
        private static ParamView[] MethodParams(MethodBase method, Type[]? gps, Type[]? typeArgs)
        {
            var ps = method.GetParameters();
            var result = new ParamView[ps.Length];
            for (var i = 0; i < ps.Length; i++)
            {
                var type = ps[i].ParameterType;
                if (gps != null && HasGenericParameter(type))
                    type = Substitute(type, gps, typeArgs!);
                object? d = null;
                var isOptional = ps[i].IsOptional;
                if (isOptional)
                {
                    d = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                    if (d == null || d == DBNull.Value || d == Type.Missing)
                        d = type.IsValueType ? Activator.CreateInstance(type) : null;
                }
                result[i] = new ParamView(
                    type,
                    ps[i].Name,
                    isOptional,
                    d,
                    Attribute.IsDefined(ps[i], typeof(ParamArrayAttribute)),
                    type.IsByRef && (ps[i].IsOut || !ps[i].IsIn));
            }
            return result;
        }

        private static ParamView[] SyntheticParams(params Type[] types)
        {
            var result = new ParamView[types.Length];
            for (var i = 0; i < types.Length; i++)
                result[i] = new ParamView(types[i], null, false, null, false);
            return result;
        }

        private static Candidate MethodCandidate(MethodBase method)
            => new()
            {
                Method = method,
                Params = null!,
                InstanceType = method.IsStatic || method is ConstructorInfo ? null : method.DeclaringType,
            };

        private static Candidate FieldGetCandidate(FieldInfo field)
            => new() { Field = field, Params = SyntheticParams(), InstanceType = field.IsStatic ? null : field.DeclaringType };

        private static Candidate FieldSetCandidate(FieldInfo field)
            => new() { Field = field, Params = SyntheticParams(field.FieldType), InstanceType = field.IsStatic ? null : field.DeclaringType };

        private static Candidate RefWriteCandidate(PropertyInfo prop, Type elementType)
        {
            var get = prop.GetGetMethod(true)!;
            return new Candidate
            {
                RefWrite = prop,
                Params = SyntheticParams(elementType),
                InstanceType = get.IsStatic ? null : prop.DeclaringType,
            };
        }

        // ============================== entry points ==============================

        /// <summary>Resolves an <see cref="BoundKind.Invoke"/> signature to a cached-callable bound call.</summary>
        internal static BoundCall ResolveInvoke(BoundTables tables, in BoundSignature sig)
        {
            if (!tables.Methods.TryGetValue(sig.Name, out var overloads))
                throw new MissingMemberException(tables.Owner.FullName, sig.Name);

            var candidates = new List<Candidate>(overloads.Length);
            foreach (var method in overloads)
            {
                var c = MethodCandidate(method);
                c.Params = MethodParams(method, null, null);
                candidates.Add(c);
            }

            var fit = Select(candidates, sig);
            return MakeCall(fit, sig.TypeArguments);
        }

        /// <summary>Resolves a <see cref="BoundKind.Get"/> or <see cref="BoundKind.Set"/> signature over the property/field domain.</summary>
        internal static BoundCall ResolveGetSet(BoundTables tables, bool set, in BoundSignature sig, bool nonPublic)
        {
            tables.Properties.TryGetValue(sig.Name, out var props);
            tables.Fields.TryGetValue(sig.Name, out var field);
            if ((props == null || props.Length == 0) && field == null)
                throw new MissingMemberException(tables.Owner.FullName, sig.Name);

            var candidates = new List<Candidate>();
            if (props != null)
            {
                foreach (var prop in props)
                {
                    var accessor = set ? prop.GetSetMethod(nonPublic) : prop.GetGetMethod(nonPublic);
                    if (accessor != null)
                    {
                        var c = MethodCandidate(accessor);
                        c.Params = MethodParams(accessor, null, null);
                        candidates.Add(c);
                    }
                    else if (set && TryRefWrite(prop, nonPublic, out var elementType))
                    {
                        candidates.Add(RefWriteCandidate(prop, elementType));
                    }
                }
            }

            if (field != null)
            {
                var writable = !field.IsInitOnly && !field.IsLiteral;
                if (!set)
                    candidates.Add(FieldGetCandidate(field));
                else if (writable)
                    candidates.Add(FieldSetCandidate(field));
            }

            var fit = Select(candidates, sig);
            return MakeCall(fit, null);
        }

        /// <summary>Resolves a <see cref="BoundKind.Ctor"/> signature over the type's constructors.</summary>
        internal static BoundCall ResolveCtor(BoundTables tables, in BoundSignature sig)
        {
            var candidates = new List<Candidate>(tables.Ctors.Length);
            foreach (var ctor in tables.Ctors)
            {
                var c = MethodCandidate(ctor);
                c.Params = MethodParams(ctor, null, null);
                candidates.Add(c);
            }

            var fit = TrySelect(candidates, sig);
            if (fit == null)
            {
                // A value type's default value remains reachable through CreateNew — the
                // implicit parameterless ctor is not in GetConstructors. Declared ctors win.
                var ownerType = tables.Owner;
                if (ownerType.IsValueType
                    && (sig.ArgTypes == null || sig.ArgTypes.Length == 0)
                    && (sig.Named == null || sig.Named.Length == 0))
                    return DefaultValueCall(ownerType);
                throw new MissingMethodException(ownerType.FullName, ".ctor");
            }
            return MakeCall(fit, null);
        }

        private static BoundCall DefaultValueCall(Type type)
        {
            var factory = AccessorEmitter.CreateFactory(type, nonPublic: false);
            return new BoundCall(new BoundPlan(Array.Empty<ArgSlot>()), null, (t, n) => factory());
        }

        // ============================== applicability ==============================

        private sealed class Fit
        {
            internal Candidate Candidate = null!;
            internal ArgSlot[] Slots = null!;
            internal Type[] PositionalTargets = null!;
            internal Type[] NamedTargets = null!;
            internal int Fills;
            internal bool Expanded;
        }

        private static Fit Select(List<Candidate> candidates, in BoundSignature sig)
        {
            var fit = TrySelect(candidates, sig);
            if (fit == null)
                throw new MissingMethodException($"No applicable overload for the supplied arguments: '{sig.Name}'.");
            return fit;
        }

        private static Fit? TrySelect(List<Candidate> candidates, in BoundSignature sig)
        {
            var fits = new List<Fit>();
            foreach (var c in candidates)
            {
                var fit = Apply(c, sig);
                if (fit != null) fits.Add(fit);
            }
            if (fits.Count == 0) return null;

            var undominated = new List<Fit>();
            foreach (var fit in fits)
            {
                var dominated = false;
                for (var k = undominated.Count - 1; k >= 0; k--)
                {
                    var cmp = Compare(undominated[k], fit, sig.ArgTypes, sig.NamedTypes);
                    if (cmp < 0) { dominated = true; break; }
                    if (cmp > 0) undominated.RemoveAt(k);
                }
                if (!dominated) undominated.Add(fit);
            }

            if (undominated.Count != 1)
                throw new AmbiguousMatchException("The call is ambiguous between the applicable overloads.");
            return undominated[0];
        }

        /// <summary>Tests one candidate against the supplied arguments; on success yields its argument-mapping plan and ranks.</summary>
        private static Fit? Apply(Candidate c, in BoundSignature sig)
        {
            var argTypes = sig.ArgTypes;
            var named = sig.Named;
            var namedTypes = sig.NamedTypes;
            var typeArgs = sig.TypeArguments;

            if (c.Method is MethodInfo m)
            {
                var gps = m.IsGenericMethodDefinition ? m.GetGenericArguments() : null;
                if (gps != null)
                {
                    if (typeArgs == null || typeArgs.Length != gps.Length) return null;
                    if (!CheckConstraints(gps, typeArgs)) return null;
                }
                else if (typeArgs != null && typeArgs.Length != 0)
                {
                    return null;
                }
                c.Params = MethodParams(m, gps, typeArgs);
            }
            else if (typeArgs != null && typeArgs.Length != 0)
            {
                return null;
            }

            var ps = c.Params;
            var positional = argTypes?.Length ?? 0;
            var namedCount = named?.Length ?? 0;
            var paramsIdx = ps.Length > 0 && ps[ps.Length - 1].IsParams ? ps.Length - 1 : -1;
            var normalParams = paramsIdx < 0 ? ps.Length : paramsIdx;

            var slots = new ArgSlot[ps.Length];
            var filled = new bool[ps.Length];
            var pTargets = new Type[positional];
            var nTargets = new Type[namedCount];

            var i = 0;
            for (; i < positional && i < normalParams; i++)
            {
                if (RankOf(argTypes![i], ps[i].Type) < 0) return null;
                slots[i] = new ArgSlot(ArgSlot.SourceKind.Positional, i, ps[i].Type, null, ps[i].WritableByRef);
                filled[i] = true;
                pTargets[i] = ps[i].Type;
            }

            if (i < positional)
            {
                if (paramsIdx < 0) return null;
                var arrayType = ps[paramsIdx].Type;
                var element = arrayType.GetElementType()!;
                if (i + 1 == positional && RankOf(argTypes![i], arrayType) >= 0)
                {
                    slots[paramsIdx] = new ArgSlot(ArgSlot.SourceKind.Positional, i, arrayType);
                    filled[paramsIdx] = true;
                    pTargets[i] = arrayType;
                    i++;
                }
                else
                {
                    for (var j = i; j < positional; j++)
                    {
                        if (RankOf(argTypes![j], element) < 0) return null;
                        pTargets[j] = element;
                    }
                    slots[paramsIdx] = new ArgSlot(ArgSlot.SourceKind.Packed, i, element);
                    filled[paramsIdx] = true;
                    i = positional;
                }
            }

            for (var j = 0; j < namedCount; j++)
            {
                var p = IndexOfParam(ps, named![j]);
                if (p < 0 || filled[p]) return null;
                var targetType = ps[p].Type;
                if (RankOf(namedTypes![j], targetType) < 0) return null;
                slots[p] = new ArgSlot(ArgSlot.SourceKind.Named, j, targetType, null, ps[p].WritableByRef);
                filled[p] = true;
                nTargets[j] = targetType;
            }

            var fills = 0;
            for (var p = 0; p < ps.Length; p++)
            {
                if (filled[p]) continue;
                if (p == paramsIdx)
                {
                    slots[p] = new ArgSlot(ArgSlot.SourceKind.Packed, positional, ps[p].Type.GetElementType()!);
                    filled[p] = true;
                }
                else if (ps[p].IsOptional)
                {
                    slots[p] = new ArgSlot(ArgSlot.SourceKind.Default, p, ps[p].Type, ps[p].Default);
                    filled[p] = true;
                    fills++;
                }
                else
                {
                    return null;
                }
            }

            return new Fit
            {
                Candidate = c,
                Slots = slots,
                PositionalTargets = pTargets,
                NamedTargets = nTargets,
                Fills = fills,
                Expanded = paramsIdx >= 0 && slots[paramsIdx].Source == ArgSlot.SourceKind.Packed,
            };
        }

        private static int IndexOfParam(ParamView[] ps, string name)
        {
            for (var i = 0; i < ps.Length; i++)
                if (ps[i].Name == name) return i;
            return -1;
        }

        /// <summary>Conversion rank for betterness: 0 identity, 1 exact-lift/enum-underlying, 2 numeric widening, 3 reference/unboxing/null — or −1 when not applicable.</summary>
        private static int RankOf(Type? source, Type target)
        {
            if (!BoundConversion.IsApplicable(source, target)) return -1;
            if (source == null) return 3;

            var t = target.IsByRef ? target.GetElementType()! : target;
            if (source == t) return 0;

            var underlying = Nullable.GetUnderlyingType(t);
            if (underlying != null && source == underlying) return 1;

            if (BoundConversion.IsEnumPair(source, t)) return 1;

            if (BoundConversion.IsWideningNumeric(source, t)
                || (underlying != null && BoundConversion.IsWideningNumeric(source, underlying)))
                return 2;

            return 3;
        }

        /// <summary>Compares two applicable candidates: &lt;0 = A strictly better, &gt;0 = B strictly better, 0 = tie/incomparable.</summary>
        private static int Compare(Fit a, Fit b, Type?[]? argTypes, Type?[]? namedTypes)
        {
            // §12.6.4 ordering: per-argument conversion quality first — a candidate wins only
            // when it is not worse on every argument and strictly better on at least one.
            // The normal-form/optional-fill tie-breaks apply only to conversion-equivalent fits.
            var aWins = 0;
            var bWins = 0;
            for (var i = 0; i < a.PositionalTargets.Length; i++)
            {
                var c = CompareConversion(argTypes![i], a.PositionalTargets[i], b.PositionalTargets[i]);
                if (c < 0) aWins++;
                else if (c > 0) bWins++;
            }
            for (var j = 0; j < a.NamedTargets.Length; j++)
            {
                var c = CompareConversion(namedTypes![j], a.NamedTargets[j], b.NamedTargets[j]);
                if (c < 0) aWins++;
                else if (c > 0) bWins++;
            }

            if (aWins > 0 && bWins == 0) return -1;
            if (bWins > 0 && aWins == 0) return 1;
            if (aWins > 0) return 0;

            if (a.Expanded != b.Expanded) return a.Expanded ? 1 : -1;
            if ((a.Fills == 0) != (b.Fills == 0)) return a.Fills == 0 ? -1 : 1;
            return 0;
        }

        /// <summary>Better-conversion for one argument: lower rank wins; equal rank prefers the more specific target (one convertibly nested in the other).</summary>
        private static int CompareConversion(Type? source, Type targetA, Type targetB)
        {
            var rankA = RankOf(source, targetA);
            var rankB = RankOf(source, targetB);
            if (rankA != rankB) return rankA < rankB ? -1 : 1;
            if (targetA == targetB) return 0;

            var aToB = BoundConversion.IsApplicable(targetA, targetB);
            var bToA = BoundConversion.IsApplicable(targetB, targetA);
            if (aToB && !bToA) return -1;
            if (bToA && !aToB) return 1;
            return 0;
        }

        // ============================== generics ==============================

        private static bool HasGenericParameter(Type type)
        {
            if (type.IsGenericParameter) return true;
            if (type.HasElementType) return HasGenericParameter(type.GetElementType()!);
            if (type.IsGenericType)
                foreach (var arg in type.GetGenericArguments())
                    if (HasGenericParameter(arg)) return true;
            return false;
        }

        /// <summary>Substitutes the method's generic parameters with explicit type arguments inside a parameter or constraint type.</summary>
        [UnconditionalSuppressMessage("Trimming", "IL2055",
            Justification = "Explicit generic type arguments are part of the bound-call contract; their requirements are preserved by the #9 annotation union, finalized in #38.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "Explicit generic type arguments are part of the bound-call contract; their requirements are preserved by the #9 annotation union, finalized in #38.")]
        private static Type Substitute(Type type, Type[] gps, Type[] typeArgs)
        {
            if (type.IsGenericParameter)
                return typeArgs[type.GenericParameterPosition];
            if (type.HasElementType)
            {
                var element = Substitute(type.GetElementType()!, gps, typeArgs);
                if (type.IsByRef) return element.MakeByRefType();
                if (type.IsPointer) return element.MakePointerType();
                return type.GetArrayRank() == 1 ? element.MakeArrayType() : element.MakeArrayType(type.GetArrayRank());
            }
            if (type.IsGenericType)
            {
                var args = type.GetGenericArguments();
                var substituted = new Type[args.Length];
                for (var i = 0; i < args.Length; i++)
                    substituted[i] = Substitute(args[i], gps, typeArgs);
                return type.GetGenericTypeDefinition().MakeGenericType(substituted);
            }
            return type;
        }

        /// <summary>Spec §12.6.4 constraint check on explicit type arguments: reference/value-type/default-ctor flags plus declared constraint types.</summary>
        [UnconditionalSuppressMessage("Trimming", "IL2065",
            Justification = "The default-constructor constraint check inspects the caller-supplied type argument; its requirements are covered by the #9 annotation union, finalized in #38.")]
        private static bool CheckConstraints(Type[] gps, Type[] typeArgs)
        {
            for (var i = 0; i < gps.Length; i++)
            {
                var arg = typeArgs[i];
                var attrs = gps[i].GenericParameterAttributes & GenericParameterAttributes.SpecialConstraintMask;

                if ((attrs & GenericParameterAttributes.ReferenceTypeConstraint) != 0 && arg.IsValueType)
                    return false;
                if ((attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0
                    && (!arg.IsValueType || Nullable.GetUnderlyingType(arg) != null))
                    return false;
                if ((attrs & GenericParameterAttributes.DefaultConstructorConstraint) != 0
                    && !arg.IsValueType
                    && arg.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null) == null)
                    return false;

                foreach (var constraint in gps[i].GetGenericParameterConstraints())
                {
                    Type resolved;
                    try
                    {
                        resolved = Substitute(constraint, gps, typeArgs);
                    }
                    catch
                    {
                        // Constraints over the declaring type's generic parameters can't be
                        // substituted here — treat the candidate as inapplicable.
                        return false;
                    }
                    if (resolved.IsGenericParameter)
                    {
                        if (resolved.DeclaringMethod == null) return false;
                        resolved = typeArgs[resolved.GenericParameterPosition];
                    }
                    if (!resolved.IsAssignableFrom(arg))
                        return false;
                }
            }
            return true;
        }

        // ============================== bound-call materialization ==============================

        /// <summary>Builds the adapted invoker for the winning fit: the resolved member closed with any explicit type arguments, running through the same emit/reflection machinery.</summary>
        [UnconditionalSuppressMessage("Trimming", "IL2060",
            Justification = "Explicit generic type arguments are part of the bound-call contract; their requirements are preserved by the #9 annotation union, finalized in #38.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "Explicit generic type arguments are part of the bound-call contract; their requirements are preserved by the #9 annotation union, finalized in #38.")]
        private static BoundCall MakeCall(Fit fit, Type[]? typeArgs)
        {
            var c = fit.Candidate;
            Func<object?, object?[]?, object?> inner;

            if (c.Method is MethodInfo method)
            {
                MethodInfo closed;
                try
                {
                    // Runtime-only constraints (unmanaged, notnull) CheckConstraints cannot read;
                    // a violated one surfaces here and maps back onto the stage-2 miss.
                    closed = method.IsGenericMethodDefinition ? method.MakeGenericMethod(typeArgs!) : method;
                }
                catch (ArgumentException e)
                {
                    throw new MissingMethodException(e.Message);
                }
                var invoker = AccessorEmitter.CreateInvoker(closed);
                inner = (target, normalized) => invoker(target, normalized);
            }
            else if (c.Method is ConstructorInfo ctor)
            {
                var invoker = AccessorEmitter.CreateInvoker(ctor);
                inner = (target, normalized) => invoker(normalized);
            }
            else if (c.Field is FieldInfo field)
            {
                if (c.Params.Length == 0)
                {
                    var getter = field.CreateGetter();
                    inner = (target, normalized) => getter(target);
                }
                else
                {
                    var setter = field.CreateSetter();
                    inner = (target, normalized) =>
                    {
                        setter(target, normalized![0]);
                        return null;
                    };
                }
            }
            else
            {
                var setter = c.RefWrite!.CreateSetter();
                inner = (target, normalized) =>
                {
                    setter(target, normalized![0]);
                    return null;
                };
            }

            return new BoundCall(new BoundPlan(fit.Slots), c.InstanceType, inner);
        }

        private static bool TryRefWrite(PropertyInfo prop, bool nonPublic, out Type elementType)
        {
            elementType = null!;
            if (prop.GetIndexParameters().Length != 0) return false;
            if (!prop.PropertyType.IsByRef) return false;
            var get = prop.GetGetMethod(nonPublic);
            if (get == null) return false;
            foreach (var t in get.ReturnParameter.GetRequiredCustomModifiers())
                if (t.Name is "InAttribute" or "IsReadOnlyAttribute")
                    return false;
            elementType = prop.PropertyType.GetElementType()!;
            return true;
        }
    }
}
