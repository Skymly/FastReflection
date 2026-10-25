using System.Reflection;
using Xunit;

namespace FastReflection.Tests
{
    public class InvokerTests
    {
        private static MethodInfo Method(string name, BindingFlags flags = BindingFlags.Public | BindingFlags.Instance)
            => typeof(Sample).GetMethod(name, flags)!;

        [Fact]
        public void MethodInvoker_Basic()
        {
            var invoker = Method(nameof(Sample.Add)).CreateInvoker();
            Assert.Equal(5, invoker(new Sample(), new object?[] { 2, 3 }));
        }

        [Fact]
        public void MethodInvoker_VoidReturnsNull()
        {
            var invoker = Method(nameof(Sample.Nothing)).CreateInvoker();
            Assert.Null(invoker(new Sample(), null));
        }

        [Fact]
        public void MethodInvoker_Static()
        {
            var invoker = Method(nameof(Sample.StaticAdd), BindingFlags.Public | BindingFlags.Static).CreateInvoker();
            Assert.Equal(7, invoker(null, new object?[] { 3, 4 }));
        }

        [Fact]
        public void MethodInvoker_Private()
        {
            var invoker = Method("Secret", BindingFlags.NonPublic | BindingFlags.Instance).CreateInvoker();
            Assert.Equal(42, invoker(new Sample(), null));
        }

        [Fact]
        public void MethodInvoker_Overloads()
        {
            var one = typeof(Sample).GetMethod(nameof(Sample.Overload), new[] { typeof(int) })!.CreateInvoker();
            var two = typeof(Sample).GetMethod(nameof(Sample.Overload), new[] { typeof(int), typeof(int) })!.CreateInvoker();
            var s = new Sample();
            Assert.Equal(1, one(s, new object?[] { 1 }));
            Assert.Equal(3, two(s, new object?[] { 1, 2 }));
        }

        [Fact]
        public void MethodInvoker_OutParam_WrittenBack()
        {
            var invoker = Method(nameof(Sample.TryParse)).CreateInvoker();
            var s = new Sample();
            var args = new object?[] { "12", null };
            Assert.Equal(true, invoker(s, args));
            Assert.Equal(12, args[1]);

            args = new object?[] { "abc", null };
            Assert.Equal(false, invoker(s, args));
            Assert.Equal(-1, args[1]);
        }

        [Fact]
        public void MethodInvoker_RefParams_WrittenBack()
        {
            var invoker = Method(nameof(Sample.Swap)).CreateInvoker();
            var args = new object?[] { 3, 4 };
            invoker(new Sample(), args);
            Assert.Equal(4, args[0]);
            Assert.Equal(3, args[1]);
        }

        [Fact]
        public void MethodInvoker_InParams()
        {
            var invoker = Method(nameof(Sample.SumIn)).CreateInvoker();
            Assert.Equal(3, invoker(new Sample(), new object?[] { 1, 2 }));
        }

        [Fact]
        public void MethodInvoker_RefReturn_Dereferences()
        {
            var sample = new Sample();
            var getter = typeof(Sample).GetProperty(nameof(Sample.RefProp))!.GetGetMethod()!;
            Assert.Equal(5, getter.CreateInvoker()(sample, Array.Empty<object?>()));
            Assert.Equal("tag", Method(nameof(Sample.RefTag)).CreateInvoker()(sample, Array.Empty<object?>()));
        }

        [Fact]
        public void MethodInvoker_NullArgs_Throws()
        {
            var invoker = Method(nameof(Sample.Add)).CreateInvoker();
            Assert.Throws<ArgumentNullException>(() => invoker(new Sample(), null));
        }

        [Fact]
        public void MethodInvoker_ShortArgs_Throws()
        {
            var invoker = Method(nameof(Sample.Add)).CreateInvoker();
            Assert.Throws<ArgumentException>(() => invoker(new Sample(), new object?[] { 1 }));
        }

        [Fact]
        public void MethodInvoker_OpenGeneric_Throws()
        {
            Assert.Throws<ArgumentException>(() => Method(nameof(Sample.Echo)).CreateInvoker());
        }

        [Fact]
        public void MethodInvoker_ConstructedGeneric()
        {
            var invoker = Method(nameof(Sample.Echo)).MakeGenericMethod(typeof(int)).CreateInvoker();
            Assert.Equal(9, invoker(new Sample(), new object?[] { 9 }));
        }

        [Fact]
        public void CtorInvoker_Private()
        {
            var ctor = typeof(Sample).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(int) }, null)!;
            var invoker = ctor.CreateInvoker();
            var s = (Sample)invoker(new object?[] { 9 })!;
            Assert.Equal(9, s.Value);
        }

        [Fact]
        public void CtorInvoker_ValueType()
        {
            var ctor = typeof(SampleStruct).GetConstructor(new[] { typeof(int), typeof(int) })!;
            var invoker = ctor.CreateInvoker();
            var s = (SampleStruct)invoker(new object?[] { 1, 2 })!;
            Assert.Equal(1, s.X);
            Assert.Equal(2, s.Y);
        }

        [Fact]
        public void Factory_PublicCtor()
        {
            var f = typeof(Sample).CreateFactory();
            Assert.IsType<Sample>(f());
        }

        [Fact]
        public void Factory_ValueType()
        {
            var f = typeof(SampleStruct).CreateFactory();
            Assert.Equal(new SampleStruct(0, 0), f());
        }

        [Fact]
        public void Factory_NoCtor_Throws()
        {
            Assert.Throws<ArgumentException>(() => typeof(NoPublicCtor).CreateFactory());
        }

        [Fact]
        public void Factory_PrivateCtor_Allowed()
        {
            var f = typeof(NoPublicCtor).CreateFactory(nonPublic: true);
            Assert.IsType<NoPublicCtor>(f());
        }

        [Fact]
        public void Factory_NonParameterlessCtor_Throws()
        {
            var ctor = typeof(NoPublicCtor).GetConstructor(new[] { typeof(int) })!;
            Assert.Throws<ArgumentException>(() => ctor.CreateFactory());
        }

        [Fact]
        public void TypedInvoker_ExactSignature()
        {
            var add = Method(nameof(Sample.Add)).CreateInvoker<Func<Sample, int, int, int>>();
            Assert.Equal(9, add(new Sample(), 4, 5));
        }

        [Fact]
        public void TypedInvoker_ExactRefSignature()
        {
            var swap = Method(nameof(Sample.Swap)).CreateInvoker<ExactSwap>();
            int a = 3, b = 4;
            swap(new Sample(), ref a, ref b);
            Assert.Equal(4, a);
            Assert.Equal(3, b);
        }

        [Fact]
        public void TypedInvoker_RefClassTarget()
        {
            var bump = Method(nameof(Sample.Bump)).CreateInvoker<BumpRef>();
            var sample = new Sample { Value = 1 };
            Assert.Equal(2, bump(ref sample));
            Assert.Equal(2, sample.Value);
        }

        [Fact]
        public void TypedInvoker_InClassTarget()
        {
            var bump = Method(nameof(Sample.Bump)).CreateInvoker<BumpIn>();
            var sample = new Sample { Value = 1 };
            Assert.Equal(2, bump(in sample));
            Assert.Equal(2, sample.Value);
        }

        [Fact]
        public void TypedInvoker_PrivateExactByRef()
        {
            var method = typeof(Sample).GetMethod("TrySecret", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var parse = method.CreateInvoker<SecretParseExact>();
            Assert.True(parse(new Sample(), out var value));
            Assert.Equal(7, value);
        }

        [Fact]
        public void TypedInvoker_ByValueStructExactByRef()
        {
            var method = typeof(SampleStruct).GetMethod("SecretSwap", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var swap = method.CreateInvoker<StructSwap>();
            int a = 1, b = 2;
            swap(new SampleStruct(), ref a, ref b);
            Assert.Equal(2, a);
            Assert.Equal(1, b);
        }

        [Fact]
        public void RefInvoker_OutParam()
        {
            var parse = Method(nameof(Sample.TryParse)).CreateInvoker<Parse>();
            Assert.True(parse(new Sample(), "12", out var value));
            Assert.Equal(12, value.Value);

            Assert.False(parse(new Sample(), "abc", out value));
            Assert.Equal(-1, value.Value);
        }

        [Fact]
        public void RefInvoker_RefParams()
        {
            var swap = Method(nameof(Sample.Swap)).CreateInvoker<SwapRefs>();
            var a = new Ref<int>(3);
            var b = new Ref<int>(4);
            swap(new Sample(), ref a, ref b);
            Assert.Equal(4, a.Value);
            Assert.Equal(3, b.Value);
        }

        [Fact]
        public void RefInvoker_NullTarget_Throws()
        {
            var parse = Method(nameof(Sample.TryParse)).CreateInvoker<Parse>();
            Assert.Throws<ArgumentNullException>(() => parse(null!, "12", out _));
        }

        [Fact]
        public void TypedInvoker_RefNullTarget_Throws()
        {
            var bump = Method(nameof(Sample.Bump)).CreateInvoker<BumpRef>();
            Sample? s = null;
            Assert.Throws<ArgumentNullException>(() => bump(ref s!));
        }

        [Fact]
        public void RefInvoker_InParams()
        {
            var sum = Method(nameof(Sample.SumIn)).CreateInvoker<SumRefs>();
            var a = new Ref<int>(1);
            var b = new Ref<int>(2);
            Assert.Equal(3, sum(new Sample(), in a, in b));
        }

        [Fact]
        public void RefInvoker_Static()
        {
            var swap = Method(nameof(Sample.StaticSwap), BindingFlags.Public | BindingFlags.Static).CreateInvoker<StaticSwapRefs>();
            var a = new Ref<int>(1);
            var b = new Ref<int>(2);
            swap(ref a, ref b);
            Assert.Equal(2, a.Value);
            Assert.Equal(1, b.Value);
        }

        [Fact]
        public void RefInvoker_Private()
        {
            var method = typeof(Sample).GetMethod("TrySecret", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var parse = method.CreateInvoker<SecretParse>();
            Assert.True(parse(new Sample(), out var value));
            Assert.Equal(7, value.Value);
        }

        [Fact]
        public void RefInvoker_ByValue_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() => Method(nameof(Sample.TryParse)).CreateInvoker<ParseByValue>());
            Assert.Contains("by value", ex.Message);
        }

        [Fact]
        public void RefInvoker_Mismatch_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() => Method(nameof(Sample.TryParse)).CreateInvoker<ParseMismatch>());
            Assert.DoesNotContain("by value", ex.Message);
        }

        [Fact]
        public void TypedInvoker_OpenGeneric_Throws()
        {
            Assert.Throws<ArgumentException>(() => Method(nameof(Sample.Echo)).CreateInvoker<Func<Sample, int, int>>());
        }

#if NETCOREAPP
        [Fact]
        public void RefInvoker_TryParse_AllocatesNothing()
        {
            var parse = Method(nameof(Sample.TryParse)).CreateInvoker<Parse>();
            var sample = new Sample();
            for (int i = 0; i < 200; i++)
                parse(sample, "12", out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            parse(sample, "12", out var value);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(12, value.Value);
            Assert.Equal(0, after - before);
        }
#endif

#if NET8_0_OR_GREATER
        [Fact]
        public void SpanInvoker_Method()
        {
            var invoker = Method(nameof(Sample.Add)).CreateSpanInvoker();
            var args = new Span<object?>(new object?[] { 2, 3 });
            Assert.Equal(5, invoker(new Sample(), args));
        }

        [Fact]
        public void SpanInvoker_Ctor()
        {
            var ctor = typeof(Sample).GetConstructor(Type.EmptyTypes)!;
            var invoker = ctor.CreateSpanInvoker();
            Assert.IsType<Sample>(invoker(Span<object?>.Empty));
        }

        [Fact]
        public void SpanInvoker_Ctor_ValueType()
        {
            var ctor = typeof(SampleStruct).GetConstructor(new[] { typeof(int), typeof(int) })!;
            var invoker = ctor.CreateSpanInvoker();
            var s = (SampleStruct)invoker(new object?[] { 1, 2 }.AsSpan())!;
            Assert.Equal(1, s.X);
            Assert.Equal(2, s.Y);
        }

        [Fact]
        public void SpanInvoker_OutParam_WrittenBack()
        {
            var invoker = Method(nameof(Sample.TryParse)).CreateSpanInvoker();
            var args = new object?[] { "12", null };
            Assert.Equal(true, invoker(new Sample(), args.AsSpan()));
            Assert.Equal(12, args[1]);

            args = new object?[] { "abc", null };
            Assert.Equal(false, invoker(new Sample(), args.AsSpan()));
            Assert.Equal(-1, args[1]);
        }

        [Fact]
        public void SpanInvoker_RefParams_WrittenBack()
        {
            var invoker = Method(nameof(Sample.Swap)).CreateSpanInvoker();
            var args = new object?[] { 3, 4 };
            invoker(new Sample(), args.AsSpan());
            Assert.Equal(4, args[0]);
            Assert.Equal(3, args[1]);
        }

        [Fact]
        public void SpanInvoker_InParams()
        {
            var invoker = Method(nameof(Sample.SumIn)).CreateSpanInvoker();
            Assert.Equal(3, invoker(new Sample(), new object?[] { 1, 2 }.AsSpan()));
        }

        [Fact]
        public void SpanInvoker_ShortArgs_Throws()
        {
            var invoker = Method(nameof(Sample.Add)).CreateSpanInvoker();
            Assert.Throws<ArgumentException>(() => invoker(new Sample(), new object?[] { 1 }.AsSpan()));
        }

        [Fact]
        public void SpanInvoker_ZeroArgs()
        {
            var invoker = Method(nameof(Sample.Nothing)).CreateSpanInvoker();
            Assert.Null(invoker(new Sample(), default));
        }

        [Fact]
        public void SpanInvoker_Static()
        {
            var invoker = Method(nameof(Sample.StaticAdd), BindingFlags.Public | BindingFlags.Static).CreateSpanInvoker();
            Assert.Equal(7, invoker(null, new object?[] { 3, 4 }.AsSpan()));
        }

        [Fact]
        public void SpanInvoker_Private()
        {
            var invoker = Method("Secret", BindingFlags.NonPublic | BindingFlags.Instance).CreateSpanInvoker();
            Assert.Equal(42, invoker(new Sample(), default));
        }

        [Fact]
        public void SpanInvoker_OpenGeneric_Throws()
        {
            Assert.Throws<ArgumentException>(() => Method(nameof(Sample.Echo)).CreateSpanInvoker());
        }

        [Fact]
        public void SpanInvoker_ConstructedGeneric()
        {
            var invoker = Method(nameof(Sample.Echo)).MakeGenericMethod(typeof(int)).CreateSpanInvoker();
            Assert.Equal(9, invoker(new Sample(), new object?[] { 9 }.AsSpan()));
        }

        [Fact]
        public void SpanInvoker_RefReturn_Dereferences()
        {
            var sample = new Sample();
            var getter = typeof(Sample).GetProperty(nameof(Sample.RefProp))!.GetGetMethod()!;
            Assert.Equal(5, getter.CreateSpanInvoker()(sample, default));
            Assert.Equal("tag", Method(nameof(Sample.RefTag)).CreateSpanInvoker()(sample, default));
        }
#endif

        private delegate bool Parse(Sample target, string text, out Ref<int> value);
        private delegate void SwapRefs(Sample target, ref Ref<int> a, ref Ref<int> b);
        private delegate int SumRefs(Sample target, in Ref<int> a, in Ref<int> b);
        private delegate void StaticSwapRefs(ref Ref<int> a, ref Ref<int> b);
        private delegate bool SecretParse(Sample target, out Ref<int> value);
        private delegate bool ParseByValue(Sample target, string text, Ref<int> value);
        private delegate bool ParseMismatch(Sample target, string text, ref Ref<long> value);
        private delegate void ExactSwap(Sample target, ref int a, ref int b);
        private delegate int BumpRef(ref Sample target);
        private delegate int BumpIn(in Sample target);
        private delegate bool SecretParseExact(Sample target, out int value);
        private delegate void StructSwap(SampleStruct target, ref int a, ref int b);
    }
}
