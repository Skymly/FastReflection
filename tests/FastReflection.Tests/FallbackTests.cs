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
    }
}
