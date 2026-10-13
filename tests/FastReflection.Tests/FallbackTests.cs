using System.Reflection;
using FastReflection.Emit;
using Xunit;

namespace FastReflection.Tests
{
    /// <summary>Verifies the reflection fallback path used when dynamic code is unavailable.</summary>
    public class FallbackTests : IDisposable
    {
        public FallbackTests() => EmitHelper.ForceReflectionFallback = true;
        public void Dispose() => EmitHelper.ForceReflectionFallback = false;

        [Fact]
        public void Getter_FallsBack()
        {
            var getter = typeof(Sample).GetProperty(nameof(Sample.Value))!.CreateGetter();
            Assert.Equal(7, getter(new Sample()));
        }

        [Fact]
        public void Setter_FallsBack()
        {
            var setter = typeof(Sample).GetProperty(nameof(Sample.Value))!.CreateSetter();
            var s = new Sample();
            setter(s, 2);
            Assert.Equal(2, s.Value);
        }

        [Fact]
        public void FieldSetter_FallsBack()
        {
            var setter = typeof(Sample).GetField(nameof(Sample.PublicField))!.CreateSetter();
            var s = new Sample();
            setter(s, 3);
            Assert.Equal(3, s.PublicField);
        }

        [Fact]
        public void Invoker_FallsBack()
        {
            var invoker = typeof(Sample).GetMethod(nameof(Sample.Add))!.CreateInvoker();
            Assert.Equal(9, invoker(new Sample(), new object?[] { 4, 5 }));
        }

        [Fact]
        public void CtorInvoker_FallsBack()
        {
            var ctor = typeof(Sample).GetConstructor(Type.EmptyTypes)!;
            Assert.IsType<Sample>(ctor.CreateInvoker()(null));
        }

        [Fact]
        public void Factory_FallsBack()
        {
            Assert.IsType<Sample>(typeof(Sample).CreateFactory()());
        }

        [Fact]
        public void TypedInvoker_ExactSignature_FallsBackToCreateDelegate()
        {
            var add = typeof(Sample).GetMethod(nameof(Sample.Add))!.CreateInvoker<Func<Sample, int, int, int>>();
            Assert.Equal(9, add(new Sample(), 4, 5));
        }

        [Fact]
        public void RefInvoker_Adaptation_RequiresDynamicCode()
        {
            var method = typeof(Sample).GetMethod(nameof(Sample.TryParse))!;
            var ex = Assert.Throws<NotSupportedException>(() => method.CreateInvoker<Parse>());
            Assert.Contains("dynamic code", ex.Message);
        }

        [Fact]
        public void TypedInvoker_ExactByRef_FallsBackToCreateDelegate()
        {
            var swap = typeof(Sample).GetMethod(nameof(Sample.Swap))!.CreateInvoker<ExactSwap>();
            int a = 3, b = 4;
            swap(new Sample(), ref a, ref b);
            Assert.Equal(4, a);
            Assert.Equal(3, b);
        }

        [Fact]
        public void TypedInvoker_PrivateExactByRef_UsesCreateDelegate()
        {
            var method = typeof(Sample).GetMethod("TrySecret", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var parse = method.CreateInvoker<SecretParseExact>();
            Assert.True(parse(new Sample(), out var value));
            Assert.Equal(7, value);
        }

        [Fact]
        public void TypedInvoker_ByValueStructExactByRef_RequiresDynamicCode()
        {
            var method = typeof(SampleStruct).GetMethod("SecretSwap", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var ex = Assert.Throws<NotSupportedException>(() => method.CreateInvoker<StructSwap>());
            Assert.Contains("dynamic code", ex.Message);
        }

        [Fact]
        public void TypedInvoker_RefClassTarget_RequiresDynamicCode()
        {
            var method = typeof(Sample).GetMethod(nameof(Sample.Bump))!;
            var ex = Assert.Throws<NotSupportedException>(() => method.CreateInvoker<BumpRef>());
            Assert.Contains("dynamic code", ex.Message);
        }

#if NET8_0_OR_GREATER
        [Fact]
        public void SpanInvoker_FallsBack()
        {
            var invoker = typeof(Sample).GetMethod(nameof(Sample.Add))!.CreateSpanInvoker();
            Assert.Equal(9, invoker(new Sample(), new object?[] { 4, 5 }.AsSpan()));
        }

        [Fact]
        public void SpanInvoker_OutParam_FallsBack()
        {
            var invoker = typeof(Sample).GetMethod(nameof(Sample.TryParse))!.CreateSpanInvoker();
            var args = new object?[] { "12", null };
            Assert.Equal(true, invoker(new Sample(), args.AsSpan()));
            Assert.Equal(12, args[1]);
        }
#endif

        private delegate bool Parse(Sample target, string text, out Ref<int> value);
        private delegate void ExactSwap(Sample target, ref int a, ref int b);
        private delegate bool SecretParseExact(Sample target, out int value);
        private delegate void StructSwap(SampleStruct target, ref int a, ref int b);
        private delegate int BumpRef(ref Sample target);
    }
}
