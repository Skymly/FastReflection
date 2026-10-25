using System.Reflection;
using FastReflection.Emit;
using Xunit;

namespace FastReflection.Tests
{
    /// <summary>
    /// Asserts the ADR-0004 exception contract identically on the emit path and on the
    /// reflection-fallback path (<see cref="EmitHelper.ForceReflectionFallback"/>).
    /// </summary>
    public abstract class ContractTestsBase : IDisposable
    {
        protected ContractTestsBase(bool fallback) => EmitHelper.ForceReflectionFallback = fallback;
        public void Dispose() => EmitHelper.ForceReflectionFallback = false;

        /// <summary>The per-leg fixture type: TypeAccessor caches by type, so each leg needs its own.</summary>
        protected abstract Type AccessorType { get; }

        private object NewTarget() => Activator.CreateInstance(AccessorType)!;

        private static PropertyInfo Prop(string name, BindingFlags flags = BindingFlags.Public | BindingFlags.Instance)
            => typeof(Sample).GetProperty(name, flags)!;

        private static FieldInfo Field(string name, BindingFlags flags = BindingFlags.Public | BindingFlags.Instance)
            => typeof(Sample).GetField(name, flags)!;

        private static MethodInfo Method(string name, BindingFlags flags = BindingFlags.Public | BindingFlags.Instance)
            => typeof(Sample).GetMethod(name, flags)!;

        // ============================== target ==============================

        [Fact]
        public void PropertyGetter_NullTarget_Throws()
        {
            var getter = Prop(nameof(Sample.Value)).CreateGetter();
            Assert.Throws<ArgumentNullException>(() => getter(null));
        }

        [Fact]
        public void PropertySetter_NullTarget_Throws()
        {
            var setter = Prop(nameof(Sample.Value)).CreateSetter();
            Assert.Throws<ArgumentNullException>(() => setter(null, 1));
        }

        [Fact]
        public void FieldGetter_NullTarget_Throws()
        {
            var getter = Field(nameof(Sample.PublicField)).CreateGetter();
            Assert.Throws<ArgumentNullException>(() => getter(null));
        }

        [Fact]
        public void FieldSetter_NullTarget_Throws()
        {
            var setter = Field(nameof(Sample.PublicField)).CreateSetter();
            Assert.Throws<ArgumentNullException>(() => setter(null, 1));
        }

        [Fact]
        public void Invoker_NullTarget_Throws()
        {
            var invoker = Method(nameof(Sample.Add)).CreateInvoker();
            Assert.Throws<ArgumentNullException>(() => invoker(null, new object?[] { 1, 2 }));
        }

        [Fact]
        public void PropertyGetter_WrongTarget_Throws()
        {
            var getter = Prop(nameof(Sample.Value)).CreateGetter();
            Assert.Throws<InvalidCastException>(() => getter(new SampleStruct()));
        }

        [Fact]
        public void FieldSetter_WrongTarget_Throws()
        {
            var setter = Field(nameof(Sample.PublicField)).CreateSetter();
            Assert.Throws<InvalidCastException>(() => setter(new SampleStruct(), 1));
        }

        [Fact]
        public void Invoker_WrongTarget_Throws()
        {
            var invoker = Method(nameof(Sample.Add)).CreateInvoker();
            Assert.Throws<InvalidCastException>(() => invoker(new SampleStruct(), new object?[] { 1, 2 }));
        }

        [Fact]
        public void StaticMembers_IgnoreTarget()
        {
            var getter = Prop(nameof(Sample.StaticProp), BindingFlags.Public | BindingFlags.Static).CreateGetter();
            var setter = Prop(nameof(Sample.StaticProp), BindingFlags.Public | BindingFlags.Static).CreateSetter();
            setter(new SampleStruct(), 42);
            Assert.Equal(42, getter(new SampleStruct()));

            var invoker = Method(nameof(Sample.StaticAdd), BindingFlags.Public | BindingFlags.Static).CreateInvoker();
            Assert.Equal(7, invoker(new SampleStruct(), new object?[] { 3, 4 }));
        }

        // ============================== value conversion ==============================

        [Fact]
        public void PropertySetter_NullToValueType_Throws_NoWrite()
        {
            var setter = Prop(nameof(Sample.Value)).CreateSetter();
            var s = new Sample { Value = 7 };
            Assert.Throws<InvalidCastException>(() => setter(s, null));
            Assert.Equal(7, s.Value);
        }

        [Fact]
        public void FieldSetter_NullToValueType_Throws_NoWrite()
        {
            var setter = Field(nameof(Sample.PublicField)).CreateSetter();
            var s = new Sample { PublicField = 5 };
            Assert.Throws<InvalidCastException>(() => setter(s, null));
            Assert.Equal(5, s.PublicField);
        }

        [Fact]
        public void PropertySetter_Widening_Throws_NoWrite()
        {
            var setter = Prop(nameof(Sample.LongProp)).CreateSetter();
            var s = new Sample { LongProp = 9L };
            Assert.Throws<InvalidCastException>(() => setter(s, 3));
            Assert.Equal(9L, s.LongProp);
        }

        [Fact]
        public void FieldSetter_Widening_Throws_NoWrite()
        {
            var setter = Field(nameof(Sample.LongField)).CreateSetter();
            var s = new Sample { LongField = 9L };
            Assert.Throws<InvalidCastException>(() => setter(s, 3));
            Assert.Equal(9L, s.LongField);
        }

        [Fact]
        public void PropertySetter_Narrowing_Throws_NoWrite()
        {
            var setter = Prop(nameof(Sample.Value)).CreateSetter();
            var s = new Sample { Value = 7 };
            Assert.Throws<InvalidCastException>(() => setter(s, 3L));
            Assert.Equal(7, s.Value);
        }

        [Fact]
        public void PropertySetter_WrongReferenceType_Throws()
        {
            var setter = Prop(nameof(Sample.Name)).CreateSetter();
            Assert.Throws<InvalidCastException>(() => setter(new Sample(), 3));
        }

        [Fact]
        public void PropertySetter_Nullable_AcceptsNullAndUnderlying()
        {
            var setter = Prop(nameof(Sample.NullableProp)).CreateSetter();
            var s = new Sample();
            setter(s, null);
            Assert.Null(s.NullableProp);
            setter(s, 5);
            Assert.Equal(5, s.NullableProp);
        }

        [Fact]
        public void PropertySetter_EnumConversions()
        {
            var day = Prop(nameof(Sample.DayProp)).CreateSetter();
            var s = new Sample();
            day(s, 5);
            Assert.Equal(DayOfWeek.Friday, s.DayProp);
            day(s, ConsoleColor.DarkYellow);
            Assert.Equal((DayOfWeek)(int)ConsoleColor.DarkYellow, s.DayProp);

            var value = Prop(nameof(Sample.Value)).CreateSetter();
            value(s, DayOfWeek.Wednesday);
            Assert.Equal((int)DayOfWeek.Wednesday, s.Value);
        }

        [Fact]
        public void Argument_Widening_Throws()
        {
            var invoker = Method(nameof(Sample.Widen)).CreateInvoker();
            Assert.Throws<InvalidCastException>(() => invoker(new Sample(), new object?[] { 3 }));
        }

        [Fact]
        public void Argument_NullToValueType_Throws()
        {
            var invoker = Method(nameof(Sample.Add)).CreateInvoker();
            Assert.Throws<InvalidCastException>(() => invoker(new Sample(), new object?[] { null, 2 }));
        }

        // ============================== callee exceptions ==============================

        [Fact]
        public void Invoker_CalleeThrows_Unwrapped()
        {
            var invoker = Method(nameof(Sample.Throwing)).CreateInvoker();
            Assert.Throws<InvalidOperationException>(() => invoker(new Sample(), new object?[] { 1 }));
        }

        [Fact]
        public void PropertySetter_CalleeThrows_Unwrapped()
        {
            var setter = Prop(nameof(Sample.SetterThrows)).CreateSetter();
            Assert.Throws<InvalidOperationException>(() => setter(new Sample(), 1));
        }

        [Fact]
        public void CtorInvoker_CalleeThrows_Unwrapped()
        {
            var ctor = typeof(ThrowingCtor).GetConstructor(new[] { typeof(int) })!;
            Assert.Throws<InvalidOperationException>(() => ctor.CreateInvoker()(new object?[] { 1 }));
        }

        [Fact]
        public void Factory_CalleeThrows_Unwrapped()
        {
            var factory = typeof(ThrowingCtor).GetConstructor(Type.EmptyTypes)!.CreateFactory();
            Assert.Throws<InvalidOperationException>(() => factory());
        }

        // ============================== argument count ==============================

        [Fact]
        public void Invoker_NullArgs_Throws()
        {
            var invoker = Method(nameof(Sample.Add)).CreateInvoker();
            Assert.Throws<ArgumentNullException>(() => invoker(new Sample(), null));
        }

        [Fact]
        public void Invoker_ShortArgs_Throws()
        {
            var invoker = Method(nameof(Sample.Add)).CreateInvoker();
            Assert.Throws<ArgumentException>(() => invoker(new Sample(), new object?[] { 1 }));
        }

        [Fact]
        public void Invoker_LongArgs_Throws()
        {
            var invoker = Method(nameof(Sample.Add)).CreateInvoker();
            Assert.Throws<ArgumentException>(() => invoker(new Sample(), new object?[] { 1, 2, 3 }));
        }

        [Fact]
        public void Invoker_ZeroParams_ExtraArgs_Throws()
        {
            var invoker = Method(nameof(Sample.Nothing)).CreateInvoker();
            Assert.Throws<ArgumentException>(() => invoker(new Sample(), new object?[] { 1 }));
        }

        [Fact]
        public void ZeroParams_NullArgs_Allowed()
        {
            var invoker = Method(nameof(Sample.Nothing)).CreateInvoker();
            Assert.Null(invoker(new Sample(), null));
            var ctor = typeof(Sample).GetConstructor(Type.EmptyTypes)!;
            Assert.IsType<Sample>(ctor.CreateInvoker()(null));
        }

        [Fact]
        public void CtorInvoker_NullArgs_Throws()
        {
            var ctor = typeof(SampleStruct).GetConstructor(new[] { typeof(int), typeof(int) })!;
            Assert.Throws<ArgumentNullException>(() => ctor.CreateInvoker()(null));
        }

        [Fact]
        public void CtorInvoker_WrongCount_Throws()
        {
            var ctor = typeof(SampleStruct).GetConstructor(new[] { typeof(int), typeof(int) })!;
            var invoker = ctor.CreateInvoker();
            Assert.Throws<ArgumentException>(() => invoker(new object?[] { 1 }));
            Assert.Throws<ArgumentException>(() => invoker(new object?[] { 1, 2, 3 }));
        }

        // ============================== member writability ==============================

        [Fact]
        public void SetValue_UnwritableMember_Throws()
        {
            var accessor = TypeAccessor.Create(AccessorType);
            Assert.Throws<InvalidOperationException>(
                () => accessor[NewTarget(), nameof(Sample.ReadOnlyProp)] = "x");
        }

        [Fact]
        public void GetValue_UnreadableMember_Throws()
        {
            var accessor = TypeAccessor.Create(AccessorType);
            Assert.Throws<InvalidOperationException>(
                () => accessor[NewTarget(), nameof(Sample.WriteOnlyProp)]);
        }

        [Fact]
        public void GetValue_OrdinalOutOfRange_Throws()
        {
            var accessor = TypeAccessor.Create(AccessorType);
            Assert.Throws<ArgumentOutOfRangeException>(() => accessor.GetValue(NewTarget(), 999));
            Assert.Throws<ArgumentOutOfRangeException>(() => accessor.SetValue(NewTarget(), 999, 1));
        }

        [Fact]
        public void TryGetOrdinal_Null_Throws()
        {
            var accessor = TypeAccessor.Create(AccessorType);
            Assert.Throws<ArgumentNullException>(() => accessor.TryGetOrdinal(null!, out _));
        }

        [Fact]
        public void SetValue_MemberWrite_ConversionContract()
        {
            var accessor = TypeAccessor.Create(AccessorType);
            var s = (Sample)NewTarget();
            s.LongProp = 9L;
            Assert.Throws<InvalidCastException>(() => accessor[s, nameof(Sample.LongProp)] = 3);
            Assert.Equal(9L, s.LongProp);
            Assert.Throws<InvalidCastException>(() => accessor[s, nameof(Sample.Value)] = null);
            Assert.Equal(7, s.Value);
            accessor[s, nameof(Sample.Value)] = 11;
            Assert.Equal(11, s.Value);
        }

        [Fact]
        public void ObjectAccessor_ForwardsContract()
        {
            var wrapped = ObjectAccessor.Create(NewTarget());
            Assert.Throws<MissingMemberException>(() => wrapped["Nope"]);
            Assert.Throws<MissingMemberException>(() => wrapped["Nope"] = 1);
            Assert.Throws<InvalidOperationException>(() => wrapped[nameof(Sample.ReadOnlyProp)] = "x");
        }

        // ============================== by-ref slots ==============================

        [Fact]
        public void InParam_NullSlot_NotWrittenBack()
        {
            var invoker = Method(nameof(Sample.SumIn)).CreateInvoker();
            var args = new object?[] { 1, null };
            Assert.Equal(1, invoker(new Sample(), args));
            Assert.Null(args[1]);
        }

        [Fact]
        public void OutParam_NullSlot_WrittenBack()
        {
            var invoker = Method(nameof(Sample.TryParse)).CreateInvoker();
            var args = new object?[] { "12", null };
            Assert.Equal(true, invoker(new Sample(), args));
            Assert.Equal(12, args[1]);
        }

        [Fact]
        public void RefParam_NullSlot_WrittenBack()
        {
            var invoker = Method(nameof(Sample.Swap)).CreateInvoker();
            var args = new object?[] { null, 4 };
            invoker(new Sample(), args);
            Assert.Equal(4, args[0]);
            Assert.Equal(0, args[1]);
        }

        [Fact]
        public void RefSlot_BadValue_Throws_NotInvoked()
        {
            var invoker = Method(nameof(Sample.Swap)).CreateInvoker();
            var args = new object?[] { "x", 4 };
            Assert.Throws<InvalidCastException>(() => invoker(new Sample(), args));
            Assert.Equal("x", args[0]);
            Assert.Equal(4, args[1]);
        }

        [Fact]
        public void CalleeThrows_NoWriteBack()
        {
            var invoker = Method(nameof(Sample.SetThenThrow)).CreateInvoker();
            var args = new object?[] { 5 };
            Assert.Throws<InvalidOperationException>(() => invoker(new Sample(), args));
            Assert.Equal(5, args[0]);
        }

        [Fact]
        public void StructMember_WritePersistsIntoBox()
        {
            var setter = typeof(SampleStruct).GetProperty(nameof(SampleStruct.X))!.CreateSetter();
            object boxed = new SampleStruct(1, 2);
            setter(boxed, 10);
            Assert.Equal(10, ((SampleStruct)boxed).X);
        }

        // ============================== trigger order ==============================

        [Fact]
        public void Invoker_BadArgsAndNullTarget_ArgsFirst()
        {
            var invoker = Method(nameof(Sample.Add)).CreateInvoker();
            Assert.Throws<ArgumentException>(() => invoker(null, new object?[] { 1 }));
        }

        [Fact]
        public void Setter_NullTargetAndBadValue_TargetFirst()
        {
            var setter = Prop(nameof(Sample.Value)).CreateSetter();
            Assert.Throws<ArgumentNullException>(() => setter(null, "bad"));
        }

        [Fact]
        public void Indexer_UnknownNameAndNullTarget_NameFirst()
        {
            var accessor = TypeAccessor.Create(AccessorType);
            Assert.Throws<MissingMemberException>(() => accessor[null, "Nope"] = 1);
            Assert.Throws<MissingMemberException>(() => accessor[NewTarget(), "Nope"]);
        }

        // ============================== creation ==============================

        [Fact]
        public void RefReturnSetter_DynamicCodeGate()
        {
            var prop = Prop(nameof(Sample.RefProp));
            if (!EmitHelper.DynamicCodeSupported)
            {
                Assert.Throws<NotSupportedException>(() => prop.CreateSetter());
                return;
            }
            var s = new Sample { PublicField = 1 };
            prop.CreateSetter()(s, 3);
            Assert.Equal(3, s.PublicField);
        }

#if NET8_0_OR_GREATER
        [Fact]
        public void SpanInvoker_NullTarget_Throws()
        {
            var invoker = Method(nameof(Sample.Add)).CreateSpanInvoker();
            Assert.Throws<ArgumentNullException>(() => invoker(null, new object?[] { 1, 2 }.AsSpan()));
        }

        [Fact]
        public void SpanInvoker_WrongLength_Throws()
        {
            var invoker = Method(nameof(Sample.Add)).CreateSpanInvoker();
            Assert.Throws<ArgumentException>(() => invoker(new Sample(), new object?[] { 1 }.AsSpan()));
            Assert.Throws<ArgumentException>(() => invoker(new Sample(), new object?[] { 1, 2, 3 }.AsSpan()));
        }

        [Fact]
        public void SpanInvoker_ZeroParams_ExtraArgs_Throws()
        {
            var invoker = Method(nameof(Sample.Nothing)).CreateSpanInvoker();
            Assert.Throws<ArgumentException>(() => invoker(new Sample(), new object?[] { 1 }.AsSpan()));
        }

        [Fact]
        public void SpanInvoker_Widening_Throws()
        {
            var invoker = Method(nameof(Sample.Widen)).CreateSpanInvoker();
            Assert.Throws<InvalidCastException>(() => invoker(new Sample(), new object?[] { 3 }.AsSpan()));
        }

        [Fact]
        public void SpanInvoker_InParam_NullSlot_NotWrittenBack()
        {
            var invoker = Method(nameof(Sample.SumIn)).CreateSpanInvoker();
            var args = new object?[] { 1, null };
            Assert.Equal(1, invoker(new Sample(), args.AsSpan()));
            Assert.Null(args[1]);
        }

        [Fact]
        public void SpanInvoker_CalleeThrows_NoWriteBack()
        {
            var invoker = Method(nameof(Sample.SetThenThrow)).CreateSpanInvoker();
            var args = new object?[] { 5 };
            Assert.Throws<InvalidOperationException>(() => invoker(new Sample(), args.AsSpan()));
            Assert.Equal(5, args[0]);
        }

        [Fact]
        public void SpanCtorInvoker_WrongLength_Throws()
        {
            var ctor = typeof(SampleStruct).GetConstructor(new[] { typeof(int), typeof(int) })!;
            var invoker = ctor.CreateSpanInvoker();
            Assert.Throws<ArgumentException>(() => invoker(new object?[] { 1 }.AsSpan()));
            Assert.Throws<ArgumentException>(() => invoker(new object?[] { 1, 2, 3 }.AsSpan()));
        }
#endif
    }

    /// <summary>The emit path: the same contract asserted with dynamic code available.</summary>
    public sealed class EmitContractTests : ContractTestsBase
    {
        public EmitContractTests() : base(fallback: false) { }
        protected override Type AccessorType => typeof(EmitLegSample);
    }

    /// <summary>The reflection-completion path: the same contract asserted with dynamic code forced off.</summary>
    public sealed class ReflectionContractTests : ContractTestsBase
    {
        public ReflectionContractTests() : base(fallback: true) { }
        protected override Type AccessorType => typeof(ReflectionLegSample);
    }
}
