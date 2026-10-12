using System.Reflection;
using Xunit;

namespace FastReflection.Tests
{
    public class AccessorEmitTests
    {
        private static PropertyInfo Prop(string name, BindingFlags flags = BindingFlags.Public | BindingFlags.Instance)
            => typeof(Sample).GetProperty(name, flags)!;

        private static FieldInfo Field(string name, BindingFlags flags = BindingFlags.Public | BindingFlags.Instance)
            => typeof(Sample).GetField(name, flags)!;

        [Fact]
        public void PropertyGetter_Public()
        {
            var getter = Prop(nameof(Sample.Value)).CreateGetter();
            var s = new Sample { Value = 42 };
            Assert.Equal(42, getter(s));
        }

        [Fact]
        public void PropertySetter_Public()
        {
            var setter = Prop(nameof(Sample.Value)).CreateSetter();
            var s = new Sample();
            setter(s, 123);
            Assert.Equal(123, s.Value);
        }

        [Fact]
        public void PropertySetter_PrivateAccessor()
        {
            var setter = Prop(nameof(Sample.PrivateSetterProp)).CreateSetter();
            var s = new Sample();
            setter(s, 11);
            Assert.Equal(11, s.PrivateSetterProp);
        }

        [Fact]
        public void PropertyGetter_PrivateMember()
        {
            var getter = Prop("PrivateProp", BindingFlags.NonPublic | BindingFlags.Instance).CreateGetter();
            var s = new Sample();
            s.SetPrivateProp(77);
            Assert.Equal(77, getter(s));
        }

        [Fact]
        public void PropertyGetter_Setter_NoSetter_Throws()
        {
            Assert.Throws<ArgumentException>(() => Prop(nameof(Sample.ReadOnlyProp)).CreateSetter());
        }

        [Fact]
        public void PropertyGetter_Static()
        {
            var getter = Prop(nameof(Sample.StaticProp), BindingFlags.Public | BindingFlags.Static).CreateGetter();
            Sample.StaticProp = 55;
            Assert.Equal(55, getter(null));
        }

        [Fact]
        public void PropertySetter_WriteThroughRef()
        {
            var setter = Prop(nameof(Sample.RefProp)).CreateSetter();
            var s = new Sample();
            setter(s, 99);
            Assert.Equal(99, s.PublicField);
        }

        [Fact]
        public void PropertyGetter_RefProp()
        {
            var getter = Prop(nameof(Sample.RefProp)).CreateGetter();
            var s = new Sample { PublicField = 8 };
            Assert.Equal(8, getter(s));
        }

        [Fact]
        public void PropertyGetter_Indexer_Throws()
        {
            Assert.Throws<NotSupportedException>(() => Prop("Item").CreateGetter());
        }

        [Fact]
        public void FieldGetter_Public()
        {
            var getter = Field(nameof(Sample.PublicField)).CreateGetter();
            var s = new Sample { PublicField = 13 };
            Assert.Equal(13, getter(s));
        }

        [Fact]
        public void FieldSetter_Public()
        {
            var setter = Field(nameof(Sample.PublicField)).CreateSetter();
            var s = new Sample();
            setter(s, 17);
            Assert.Equal(17, s.PublicField);
        }

        [Fact]
        public void FieldGetter_Private()
        {
            var getter = Field("_privateField", BindingFlags.NonPublic | BindingFlags.Instance).CreateGetter();
            var s = new Sample();
            Assert.Equal("pf", getter(s));
            s.SetPrivateField("changed");
            Assert.Equal("changed", getter(s));
        }

        [Fact]
        public void FieldSetter_Private()
        {
            var setter = Field("_privateField", BindingFlags.NonPublic | BindingFlags.Instance).CreateSetter();
            var s = new Sample();
            setter(s, "x");
            Assert.Equal("x", s.GetPrivateField());
        }

        [Fact]
        public void FieldSetter_ReadOnly_Throws()
        {
            Assert.Throws<ArgumentException>(() => Field(nameof(Sample.ReadOnlyField)).CreateSetter());
        }

        [Fact]
        public void FieldGetter_Const()
        {
            var getter = Field(nameof(Sample.ConstField), BindingFlags.Public | BindingFlags.Static).CreateGetter();
            Assert.Equal(3, getter(new Sample()));
        }

        [Fact]
        public void FieldGetterSetter_Static()
        {
            var field = Field(nameof(Sample.StaticField), BindingFlags.Public | BindingFlags.Static);
            field.CreateSetter()(null, "sv");
            Assert.Equal("sv", field.CreateGetter()(null));
        }

        [Fact]
        public void TypedGetter_ExactSignature()
        {
            var getter = Prop(nameof(Sample.Value)).CreateGetter<Sample, int>();
            var s = new Sample { Value = 66 };
            Assert.Equal(66, getter(s));
        }

        [Fact]
        public void TypedGetter_WidenedTarget()
        {
            var getter = Prop(nameof(Sample.Value)).CreateGetter<object, int>();
            var s = new Sample { Value = 66 };
            Assert.Equal(66, getter(s));
        }

        [Fact]
        public void TypedGetter_WidenedValue()
        {
            var getter = Prop(nameof(Sample.Value)).CreateGetter<Sample, object>();
            var s = new Sample { Value = 66 };
            Assert.Equal(66, getter(s));
        }

        [Fact]
        public void TypedSetter_ExactSignature()
        {
            var setter = Prop(nameof(Sample.Value)).CreateSetter<Sample, int>();
            var s = new Sample();
            setter(s, 5);
            Assert.Equal(5, s.Value);
        }

        [Fact]
        public void TypedFieldGetterSetter()
        {
            var getter = Field(nameof(Sample.PublicField)).CreateGetter<Sample, int>();
            var setter = Field(nameof(Sample.PublicField)).CreateSetter<Sample, int>();
            var s = new Sample();
            setter(s, 33);
            Assert.Equal(33, getter(s));
        }

        [Fact]
        public void StructFieldWrite_PersistsIntoBox()
        {
            var setter = typeof(SampleStruct).GetField(nameof(SampleStruct.Y))!.CreateSetter();
            object boxed = new SampleStruct(1, 2);
            setter(boxed, 10);
            Assert.Equal(10, ((SampleStruct)boxed).Y);
        }

        [Fact]
        public void StructPropertyGetter_OnBox()
        {
            var getter = typeof(SampleStruct).GetProperty(nameof(SampleStruct.X))!.CreateGetter();
            object boxed = new SampleStruct(4, 5);
            Assert.Equal(4, getter(boxed));
        }

        [Fact]
        public void Unreflect_ExactSignature()
        {
            var add = typeof(Sample).GetMethod(nameof(Sample.Add))!
                .Unreflect<Func<Sample, int, int, int>>();
            Assert.Equal(9, add(new Sample(), 4, 5));
        }
    }
}
