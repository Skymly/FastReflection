using System.Reflection;
using FastReflection.Emit;
using Xunit;

namespace FastReflection.Tests
{
    /// <summary>
    /// The #35 bound-call surface: resolver decision table, trigger order, member domain,
    /// bound-invoker cache — asserted identically on the emit path and on the
    /// reflection-fallback path (<see cref="EmitHelper.ForceReflectionFallback"/>).
    /// </summary>
    public abstract class BoundCallTestsBase : IDisposable
    {
        protected BoundCallTestsBase(bool fallback) => EmitHelper.ForceReflectionFallback = fallback;
        public void Dispose() => EmitHelper.ForceReflectionFallback = false;

        /// <summary>The per-leg fixture type: bound calls cache per accessor, so each leg needs its own.</summary>
        protected abstract Type AccessorType { get; }

        /// <summary>Per-leg struct fixture — same cache rule as <see cref="AccessorType"/>.</summary>
        protected abstract Type StructType { get; }

        /// <summary>Per-leg 'new'-hiding fixture — same cache rule as <see cref="AccessorType"/>.</summary>
        protected abstract Type DerivedType { get; }

        private object NewTarget() => Activator.CreateInstance(AccessorType)!;
        private TypeAccessor Accessor() => TypeAccessor.Create(AccessorType);

        private static Dictionary<string, object?> Named(params (string, object?)[] pairs)
        {
            var d = new Dictionary<string, object?>();
            foreach (var (k, v) in pairs) d[k] = v;
            return d;
        }

        // ============================== stage 1: name ==============================

        [Fact]
        public void Invoke_NullName_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Accessor().Invoke(NewTarget(), null!, Array.Empty<object?>()));
        }

        [Fact]
        public void Invoke_MissingName_Throws_BeforeNullArgs()
        {
            var accessor = Accessor();
            Assert.Throws<MissingMemberException>(() => accessor.Invoke(NewTarget(), "Nope", null));
            Assert.Throws<MissingMemberException>(() => accessor.Invoke(null, "Nope", Array.Empty<object?>()));
        }

        [Fact]
        public void Invoke_CaseSensitive_Misses()
        {
            Assert.Throws<MissingMemberException>(() => Accessor().Invoke(NewTarget(), "add", new object?[] { 1, 2 }));
        }

        [Fact]
        public void GetValue_MissingName_Throws()
        {
            var accessor = Accessor();
            Assert.Throws<MissingMemberException>(() => accessor.GetValue(NewTarget(), "Nope"));
            Assert.Throws<MissingMemberException>(() => accessor.GetValue(NewTarget(), nameof(BoundSample.Add)));
            Assert.Throws<ArgumentNullException>(() => accessor.GetValue(NewTarget(), null!));
        }

        [Fact]
        public void SetValue_MissingName_Throws()
        {
            var accessor = Accessor();
            Assert.Throws<MissingMemberException>(() => accessor.SetValue(NewTarget(), "Nope", 1));
            Assert.Throws<ArgumentNullException>(() => accessor.SetValue(NewTarget(), null!, 1));
        }

        // ============================== stage 2: binding ==============================

        [Fact]
        public void Invoke_BadArity_Throws()
        {
            var accessor = Accessor();
            var t = NewTarget();
            Assert.Throws<MissingMethodException>(() => accessor.Invoke(t, nameof(BoundSample.Add), new object?[] { 1 }));
            Assert.Throws<MissingMethodException>(() => accessor.Invoke(t, nameof(BoundSample.Add), new object?[] { 1, 2, 3 }));
        }

        [Fact]
        public void Invoke_UnmatchedNamedArgument_Throws()
        {
            Assert.Throws<MissingMethodException>(
                () => Accessor().Invoke(NewTarget(), nameof(BoundSample.Named), Array.Empty<object?>(),
                    Named(("second", "x"), ("nope", 1)), null));
        }

        [Fact]
        public void Invoke_DuplicateFill_Throws()
        {
            Assert.Throws<MissingMethodException>(
                () => Accessor().Invoke(NewTarget(), nameof(BoundSample.Named), new object?[] { 5 },
                    Named(("first", 9)), null));
        }

        [Fact]
        public void Invoke_BestMatchTie_Throws()
        {
            Assert.Throws<AmbiguousMatchException>(
                () => Accessor().Invoke(NewTarget(), nameof(BoundSample.Amb), new object?[] { null, null }));
        }

        // ============================== stages 3-5: run ==============================

        [Fact]
        public void Invoke_NullArgs_Throws_AfterBinding_BeforeTarget()
        {
            // Nothing binds with zero parameters; stage 3 fires before the stage-4 target check.
            Assert.Throws<ArgumentNullException>(() => Accessor().Invoke(null, nameof(BoundSample.Nothing), null));
        }

        [Fact]
        public void GetValue_NullIndex_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Accessor().GetValue(NewTarget(), nameof(BoundSample.Value), null));
        }

        [Fact]
        public void SetValue_NullIndex_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Accessor().SetValue(NewTarget(), nameof(BoundSample.Value), 1, null));
        }

        [Fact]
        public void CreateNew_NullArgs_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Accessor().CreateNew(null, null));
        }

        [Fact]
        public void Invoke_NullInstanceTarget_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Accessor().Invoke(null, nameof(BoundSample.Add), new object?[] { 1, 2 }));
        }

        [Fact]
        public void Invoke_WrongTarget_Throws()
        {
            Assert.Throws<InvalidCastException>(() => Accessor().Invoke(new object(), nameof(BoundSample.Add), new object?[] { 1, 2 }));
        }

        [Fact]
        public void Invoke_CalleeThrows_Unwrapped()
        {
            Assert.Throws<InvalidOperationException>(() => Accessor().Invoke(NewTarget(), nameof(BoundSample.Throwing), Array.Empty<object?>()));
        }

        // ============================== resolver decision table ==============================

        [Fact]
        public void Invoke_WideningSet()
        {
            var accessor = Accessor();
            var t = NewTarget();
            Assert.Equal(3, accessor.Invoke(t, nameof(BoundSample.Add), new object?[] { 1, 2 }));
            Assert.Equal(5L, accessor.Invoke(t, nameof(BoundSample.Widen), new object?[] { 5 }));
            Assert.Equal(7m, accessor.Invoke(t, nameof(BoundSample.ToDecimal), new object?[] { 7 }));
            Assert.Equal('a', accessor.Invoke(t, nameof(BoundSample.ToChar), new object?[] { 'a' }));
        }

        [Fact]
        public void Invoke_ByteToChar_Excluded()
        {
            Assert.Throws<MissingMethodException>(
                () => Accessor().Invoke(NewTarget(), nameof(BoundSample.ToChar), new object?[] { (byte)65 }));
        }

        [Fact]
        public void Invoke_Null_OnlyToNullableTargets()
        {
            var accessor = Accessor();
            var t = NewTarget();
            Assert.Null(accessor.Invoke(t, nameof(BoundSample.Nullables), new object?[] { null }));
            Assert.Throws<MissingMethodException>(() => accessor.Invoke(t, nameof(BoundSample.Widen), new object?[] { null }));
        }

        [Fact]
        public void Invoke_NullableLift()
        {
            Assert.Equal(5, Accessor().Invoke(NewTarget(), nameof(BoundSample.Nullables), new object?[] { 5 }));
        }

        [Fact]
        public void Invoke_EnumConversions()
        {
            var accessor = Accessor();
            var t = NewTarget();
            Assert.Equal(DayOfWeek.Friday, accessor.Invoke(t, nameof(BoundSample.Day), new object?[] { 5 }));
            Assert.Equal(5, accessor.Invoke(t, nameof(BoundSample.EnumToInt), new object?[] { DayOfWeek.Friday }));
        }

        [Fact]
        public void Invoke_BetterConversion_PicksExact()
        {
            var accessor = Accessor();
            var t = NewTarget();
            Assert.Equal(5, accessor.Invoke(t, nameof(BoundSample.Overloaded), new object?[] { 5 }));
            Assert.Equal(5L, accessor.Invoke(t, nameof(BoundSample.Overloaded), new object?[] { 5L }));
        }

        [Fact]
        public void Invoke_UserConversion_NeverApplies()
        {
            Assert.Throws<MissingMethodException>(
                () => Accessor().Invoke(NewTarget(), nameof(BoundSample.WithImplicit), new object?[] { new ImplicitArg() }));
        }

        [Fact]
        public void Invoke_OptionalFill()
        {
            var accessor = Accessor();
            var t = NewTarget();
            Assert.Equal(12, accessor.Invoke(t, nameof(BoundSample.Optional), new object?[] { 1 }));
            Assert.Equal(13, accessor.Invoke(t, nameof(BoundSample.Optional), new object?[] { 1, 3 }));
        }

        [Fact]
        public void Invoke_NoFill_BeatsFill()
        {
            Assert.Equal(1, Accessor().Invoke(NewTarget(), nameof(BoundSample.OptOver), new object?[] { 1 }));
        }

        [Fact]
        public void Invoke_NamedArguments_Reorder()
        {
            var accessor = Accessor();
            var t = NewTarget();
            Assert.Equal("5:x", accessor.Invoke(t, nameof(BoundSample.Named), Array.Empty<object?>(),
                Named(("second", "x"), ("first", 5)), null));
            Assert.Equal("5:x", accessor.Invoke(t, nameof(BoundSample.Named), new object?[] { 5 },
                Named(("second", "x")), null));
        }

        [Fact]
        public void Invoke_Params_BothForms()
        {
            var accessor = Accessor();
            var t = NewTarget();
            Assert.Equal(6, accessor.Invoke(t, nameof(BoundSample.Sum), new object?[] { 1, 2, 3 }));
            Assert.Equal(3, accessor.Invoke(t, nameof(BoundSample.Sum), new object?[] { new[] { 1, 2 } }));
            Assert.Equal(0, accessor.Invoke(t, nameof(BoundSample.Sum), Array.Empty<object?>()));
            Assert.Equal(13, accessor.Invoke(t, nameof(BoundSample.SumTail), new object?[] { 10, 1, 2, 3 }));
        }

        [Fact]
        public void Invoke_ConversionQuality_BeatsNormalForm()
        {
            // §12.6.4 ordering: an omitted params slot is expanded form, but the strictly
            // better argument conversion (identity over widening) wins first.
            var result = Accessor().Invoke(NewTarget(), nameof(BoundSample.HeadOrWide), new object?[] { 5 });
            Assert.IsType<int>(result);
            Assert.Equal(5, result);
        }

        [Fact]
        public void Invoke_ExplicitTypeArguments()
        {
            var accessor = Accessor();
            var t = NewTarget();
            Assert.Equal(5, accessor.Invoke(t, nameof(BoundSample.Echo), new object?[] { 5 }, null, new[] { typeof(int) }));
            Assert.IsType<Sample>(accessor.Invoke(t, nameof(BoundSample.NewT), Array.Empty<object?>(), null, new[] { typeof(Sample) }));
        }

        [Fact]
        public void Invoke_TypeInference_NeverApplies()
        {
            Assert.Throws<MissingMethodException>(
                () => Accessor().Invoke(NewTarget(), nameof(BoundSample.Echo), new object?[] { 5 }));
        }

        [Fact]
        public void Invoke_ConstraintViolation_BindsNot()
        {
            var accessor = Accessor();
            var t = NewTarget();
            Assert.Throws<MissingMethodException>(
                () => accessor.Invoke(t, nameof(BoundSample.NewT), Array.Empty<object?>(), null, new[] { typeof(NoPublicCtor) }));
            Assert.Throws<MissingMethodException>(
                () => accessor.Invoke(t, nameof(BoundSample.RefT), new object?[] { 5 }, null, new[] { typeof(int) }));
            Assert.Throws<MissingMethodException>(
                () => accessor.Invoke(t, nameof(BoundSample.Add), new object?[] { 1, 2 }, null, new[] { typeof(int) }));
        }

        // ============================== member domain ==============================

        [Fact]
        public void GetSetValue_PropertyAndField()
        {
            var accessor = Accessor();
            var t = (BoundSample)NewTarget();
            Assert.Equal(7, accessor.GetValue(t, nameof(BoundSample.Value)));
            accessor.SetValue(t, nameof(BoundSample.Value), 11);
            Assert.Equal(11, t.Value);

            Assert.Equal("tag", accessor.GetValue(t, nameof(BoundSample.Tag)));
            accessor.SetValue(t, nameof(BoundSample.Tag), "x");
            Assert.Equal("x", t.Tag);
        }

        [Fact]
        public void GetSetValue_Writeability()
        {
            var accessor = Accessor();
            var t = NewTarget();
            Assert.Throws<MissingMethodException>(() => accessor.SetValue(t, nameof(BoundSample.GetOnly), 1));
            Assert.Throws<MissingMethodException>(() => accessor.GetValue(t, nameof(BoundSample.SetOnly)));
            Assert.Throws<MissingMethodException>(() => accessor.SetValue(t, nameof(BoundSample.ConstField), 1));
            Assert.Equal(3, accessor.GetValue(t, nameof(BoundSample.ConstField)));
        }

        [Fact]
        public void GetSetValue_Nullable()
        {
            var accessor = Accessor();
            var t = (BoundSample)NewTarget();
            accessor.SetValue(t, nameof(BoundSample.NullableProp), null);
            Assert.Null(t.NullableProp);
            accessor.SetValue(t, nameof(BoundSample.NullableProp), 5);
            Assert.Equal(5, t.NullableProp);
        }

        [Fact]
        public void GetSetValue_Indexer()
        {
            var accessor = Accessor();
            var t = (BoundSample)NewTarget();
            Assert.Equal(4, accessor.GetValue(t, "Item", new object?[] { 2 }));
            Assert.Equal("k!", accessor.GetValue(t, "Item", new object?[] { "k" }));
            accessor.SetValue(t, "Item", 5, new object?[] { 3 });
            Assert.Equal(8, t.IndexValue);
            Assert.Throws<MissingMethodException>(() => accessor.GetValue(t, "Item", new object?[] { 1.5 }));
            Assert.Throws<MissingMethodException>(() => accessor.GetValue(t, "Item", Array.Empty<object?>()));
        }

        [Fact]
        public void Statics_BindTargetFree()
        {
            var accessor = Accessor();
            Assert.Equal(3, accessor.Invoke(new object(), nameof(BoundSample.StaticAdd), new object?[] { 1, 2 }));
            Assert.Equal(3, accessor.Invoke(null, nameof(BoundSample.StaticAdd), new object?[] { 1, 2 }));
            Assert.Equal(3, ObjectAccessor.Create(NewTarget()).Invoke(nameof(BoundSample.StaticAdd), new object?[] { 1, 2 }));

            accessor.SetValue(null, nameof(BoundSample.StaticProp), 42);
            Assert.Equal(42, accessor.GetValue(null, nameof(BoundSample.StaticProp)));
            accessor.SetValue(null, nameof(BoundSample.StaticField), "x");
            Assert.Equal("x", accessor.GetValue(null, nameof(BoundSample.StaticField)));
            BoundSample.StaticProp = 0;
            BoundSample.StaticField = null;
        }

        [Fact]
        public void CreateNew_BindsConstructors()
        {
            var accessor = Accessor();
            Assert.Equal(5, ((BoundSample)accessor.CreateNew(new object?[] { 5 })).Value);
            var full = (BoundSample)accessor.CreateNew(new object?[] { 5, "x" });
            Assert.Equal(5, full.Value);
            Assert.Equal("x", full.Tag);

            var named = (BoundSample)accessor.CreateNew(Array.Empty<object?>(), Named(("tag", "y"), ("v", 3)));
            Assert.Equal(3, named.Value);
            Assert.Equal("y", named.Tag);

            Assert.Throws<MissingMethodException>(() => accessor.CreateNew(new object?[] { 1, 2, 3 }));
        }

        [Fact]
        public void CreateNew_ValueType_DefaultAndCtor()
        {
            var accessor = TypeAccessor.Create(StructType);
            Assert.Equal(Activator.CreateInstance(StructType), accessor.CreateNew(Array.Empty<object?>()));
            Assert.Equal(Activator.CreateInstance(StructType, 1, 2), accessor.CreateNew(new object?[] { 1, 2 }));
            Assert.Throws<MissingMethodException>(() => accessor.CreateNew(new object?[] { 1 }));
        }

        [Fact]
        public void CreateNew_BoxedStructWrite_Persists()
        {
            var accessor = TypeAccessor.Create(StructType);
            var boxed = Activator.CreateInstance(StructType)!;
            accessor.SetValue(boxed, "X", 10);
            accessor.SetValue(boxed, "Y", 3);
            Assert.Equal(10, accessor.GetValue(boxed, "X"));
            Assert.Equal(3, accessor.GetValue(boxed, "Y"));
        }

        [Fact]
        public void Hiding_KeepsMostDerived()
        {
            var accessor = TypeAccessor.Create(DerivedType);
            Assert.Equal(-1, accessor.Invoke(Activator.CreateInstance(DerivedType), nameof(BoundSample.Add), new object?[] { 1, 2 }));
        }

        [Fact]
        public void GetMemberNames_DistinctBindableSet()
        {
            var names = Accessor().GetMemberNames();
            Assert.Contains(nameof(BoundSample.Value), names);
            Assert.Contains(nameof(BoundSample.Tag), names);
            Assert.Contains(nameof(BoundSample.Add), names);
            Assert.Contains("Item", names);
            Assert.Contains(nameof(BoundSample.StaticProp), names);
            Assert.Contains(nameof(BoundSample.StaticField), names);
            Assert.Contains(nameof(BoundSample.ConstField), names);
            Assert.DoesNotContain("get_Item", names);
            Assert.DoesNotContain("set_Item", names);
            Assert.DoesNotContain(".ctor", names);
            Assert.Single(names, n => n == nameof(BoundSample.Overloaded));
        }

        [Fact]
        public void NonPublic_ExtendsDomain()
        {
            var pub = Accessor();
            Assert.Throws<MissingMemberException>(() => pub.Invoke(NewTarget(), "Secret", Array.Empty<object?>()));
            Assert.DoesNotContain("Secret", pub.GetMemberNames());

            var priv = TypeAccessor.Create(AccessorType, allowNonPublicAccessors: true);
            Assert.Equal(42, priv.Invoke(NewTarget(), "Secret", Array.Empty<object?>()));
            Assert.Contains("Secret", priv.GetMemberNames());
        }

        // ============================== Try* scope ==============================

        [Fact]
        public void TryInvoke_BindStages_ReturnFalse()
        {
            var accessor = Accessor();
            var t = NewTarget();
            Assert.False(accessor.TryInvoke(t, null!, Array.Empty<object?>(), out _));
            Assert.False(accessor.TryInvoke(t, "Nope", Array.Empty<object?>(), out _));
            Assert.False(accessor.TryInvoke(t, nameof(BoundSample.Add), new object?[] { 1 }, out _));
            Assert.False(accessor.TryInvoke(t, nameof(BoundSample.Amb), new object?[] { null, null }, out _));
        }

        [Fact]
        public void TryInvoke_LaterStages_Propagate()
        {
            var accessor = Accessor();
            var t = NewTarget();
            Assert.True(accessor.TryInvoke(t, nameof(BoundSample.Add), new object?[] { 1, 2 }, out var r));
            Assert.Equal(3, r);
            Assert.Throws<ArgumentNullException>(() => accessor.TryInvoke(null, nameof(BoundSample.Nothing), null, out _));
            Assert.Throws<ArgumentNullException>(() => accessor.TryInvoke(null, nameof(BoundSample.Add), new object?[] { 1, 2 }, out _));
            Assert.Throws<InvalidCastException>(() => accessor.TryInvoke(new object(), nameof(BoundSample.Add), new object?[] { 1, 2 }, out _));
            Assert.Throws<InvalidOperationException>(() => accessor.TryInvoke(t, nameof(BoundSample.Throwing), Array.Empty<object?>(), out _));
        }

        [Fact]
        public void TryGetSetValue_BindStages_ReturnFalse()
        {
            var accessor = Accessor();
            var t = NewTarget();
            Assert.False(accessor.TryGetValue(t, "Nope", null, out _));
            Assert.False(accessor.TryGetValue(t, nameof(BoundSample.Add), null, out _));
            Assert.False(accessor.TryGetValue(t, nameof(BoundSample.SetOnly), null, out _));
            Assert.False(accessor.TrySetValue(t, "Nope", 1, null));
            Assert.False(accessor.TrySetValue(t, nameof(BoundSample.GetOnly), 1, null));

            Assert.True(accessor.TryGetValue(t, nameof(BoundSample.Value), Array.Empty<object?>(), out var v));
            Assert.Equal(7, v);
            Assert.True(accessor.TrySetValue(t, nameof(BoundSample.Value), 12, Array.Empty<object?>()));
            Assert.Equal(12, ((BoundSample)t).Value);
        }

        [Fact]
        public void TryCreateNew_BindStages()
        {
            var accessor = Accessor();
            Assert.True(accessor.TryCreateNew(new object?[] { 5 }, null, out var r));
            Assert.Equal(5, ((BoundSample)r!).Value);
            Assert.False(accessor.TryCreateNew(new object?[] { 1, 2, 3 }, null, out _));
            Assert.Throws<ArgumentNullException>(() => accessor.TryCreateNew(null, null, out _));
        }

        // ============================== by-ref write-back ==============================

        [Fact]
        public void RefParams_WriteBack_OnSuccess()
        {
            var args = new object?[] { 3, 4 };
            Accessor().Invoke(NewTarget(), nameof(BoundSample.Swap), args);
            Assert.Equal(4, args[0]);
            Assert.Equal(3, args[1]);
        }

        [Fact]
        public void OutParam_NullSlot_WrittenBack()
        {
            var args = new object?[] { "12", null };
            Assert.Equal(true, Accessor().Invoke(NewTarget(), nameof(BoundSample.TryOut), args));
            Assert.Equal(12, args[1]);
        }

        [Fact]
        public void InParam_NullSlot_NotWrittenBack()
        {
            var args = new object?[] { 1, null };
            Assert.Equal(1, Accessor().Invoke(NewTarget(), nameof(BoundSample.SumIn), args));
            Assert.Null(args[1]);
        }

        [Fact]
        public void RefParam_CalleeThrows_NoWriteBack()
        {
            var args = new object?[] { 5 };
            Assert.Throws<InvalidOperationException>(() => Accessor().Invoke(NewTarget(), nameof(BoundSample.SetThenThrow), args));
            Assert.Equal(5, args[0]);
        }

        // ============================== bound-invoker cache ==============================

        [Fact]
        public void BoundInvoker_RepeatSignature_ReplaysCorrectly()
        {
            // A resolved bound call is cached per signature and replayed: repeated binds
            // keep producing the right result (external-behavior evidence per spec — no
            // internal-state assertions on resolver or cache internals).
            var accessor = Accessor();
            var t = NewTarget();
            Assert.Equal(13, accessor.Invoke(t, nameof(BoundSample.SumTail), new object?[] { 9, 8, 7, 6, 5 }));
            Assert.Equal(5, accessor.Invoke(t, nameof(BoundSample.SumTail), new object?[] { 1, 2, 3, 4, 5 }));
            Assert.Equal(6, accessor.Invoke(t, nameof(BoundSample.SumTail), new object?[] { 1, 2, 3, 4, 5, 6 }));
        }

        // ============================== ObjectAccessor ==============================

        [Fact]
        public void ObjectAccessor_BoundSurface_Forwards()
        {
            var wrapped = ObjectAccessor.Create(NewTarget());
            Assert.Equal(3, wrapped.Invoke(nameof(BoundSample.Add), new object?[] { 1, 2 }));
            Assert.Equal(7, wrapped.GetValue(nameof(BoundSample.Value)));
            wrapped.SetValue(nameof(BoundSample.Value), 8);
            Assert.Equal(8, wrapped.GetValue(nameof(BoundSample.Value)));
            Assert.Equal(4, wrapped.GetValue("Item", new object?[] { 2 }));
            Assert.True(wrapped.TryInvoke(nameof(BoundSample.Add), new object?[] { 1, 2 }, out var r));
            Assert.Equal(3, r);
            Assert.False(wrapped.TryInvoke("Nope", Array.Empty<object?>(), out _));
            Assert.False(wrapped.TryGetValue("Nope", null, out _));
            Assert.True(wrapped.TryCreateNew(new object?[] { 6 }, null, out var made));
            Assert.Equal(6, ((BoundSample)made!).Value);
            Assert.Equal(Accessor().GetMemberNames(), wrapped.GetMemberNames());
        }

        // ============================== CreateConverter ==============================

        [Fact]
        public void CreateConverter_SanctionedSet()
        {
            Assert.Equal(5L, ReflectionExtensions.CreateConverter(typeof(int), typeof(long))(5));
            Assert.Equal(5, ReflectionExtensions.CreateConverter(typeof(long), typeof(int))(5L));
            Assert.Equal('a', ReflectionExtensions.CreateConverter(typeof(int), typeof(char))('a' - 0));
            Assert.Equal(1, ReflectionExtensions.CreateConverter(typeof(DayOfWeek), typeof(int))(DayOfWeek.Monday));
            Assert.Equal(DayOfWeek.Monday, ReflectionExtensions.CreateConverter(typeof(int), typeof(DayOfWeek))(1));
            Assert.Equal(5, ReflectionExtensions.CreateConverter(typeof(int), typeof(int?))(5));
            Assert.Equal("s", ReflectionExtensions.CreateConverter(typeof(object), typeof(string))("s"));
        }

        [Fact]
        public void CreateConverter_NullHandling()
        {
            Assert.Null(ReflectionExtensions.CreateConverter(typeof(string), typeof(string))(null));
            Assert.Throws<InvalidCastException>(() => ReflectionExtensions.CreateConverter(typeof(object), typeof(int))(null));
        }

        [Fact]
        public void CreateConverter_Failures()
        {
            Assert.Throws<ArgumentNullException>(() => ReflectionExtensions.CreateConverter(null!, typeof(int)));
            Assert.Throws<ArgumentNullException>(() => ReflectionExtensions.CreateConverter(typeof(int), null!));
            Assert.Throws<InvalidCastException>(() => ReflectionExtensions.CreateConverter(typeof(string), typeof(int)));
            Assert.Throws<InvalidCastException>(() => ReflectionExtensions.CreateConverter(typeof(ImplicitArg), typeof(int)));
            var narrow = ReflectionExtensions.CreateConverter(typeof(long), typeof(int));
            Assert.Throws<InvalidCastException>(() => narrow(long.MaxValue));
            Assert.Throws<InvalidCastException>(() => narrow("x"));
        }
    }

    /// <summary>The emit path: the bound-call contract asserted with dynamic code available.</summary>
    public sealed class EmitBoundCallTests : BoundCallTestsBase
    {
        public EmitBoundCallTests() : base(fallback: false) { }
        protected override Type AccessorType => typeof(EmitBoundLeg);
        protected override Type StructType => typeof(EmitBoundStruct);
        protected override Type DerivedType => typeof(EmitBoundDerived);
    }

    /// <summary>The reflection-completion path: the bound-call contract asserted with dynamic code forced off.</summary>
    public sealed class ReflectionBoundCallTests : BoundCallTestsBase
    {
        public ReflectionBoundCallTests() : base(fallback: true) { }
        protected override Type AccessorType => typeof(ReflectionBoundLeg);
        protected override Type StructType => typeof(ReflectionBoundStruct);
        protected override Type DerivedType => typeof(ReflectionBoundDerived);
    }
}
