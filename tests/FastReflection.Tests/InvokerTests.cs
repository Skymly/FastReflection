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
#endif
    }
}
