using Xunit;

namespace FastReflection.Tests
{
    public class TypeAccessorTests
    {
        [Fact]
        public void Indexer_GetSet()
        {
            var accessor = TypeAccessor.Create(typeof(Sample));
            var s = new Sample();
            accessor[s, "Value"] = 21;
            Assert.Equal(21, accessor[s, "Value"]);
            Assert.Equal(21, s.Value);
        }

        [Fact]
        public void Indexer_UnknownMember_Throws()
        {
            var accessor = TypeAccessor.Create(typeof(Sample));
            var s = new Sample();
            Assert.Throws<MissingMemberException>(() => accessor[s, "Nope"]);
            Assert.Throws<MissingMemberException>(() => accessor[s, "Nope"] = 1);
        }

        [Fact]
        public void Indexer_SetOnReadOnlyProp_Throws()
        {
            var accessor = TypeAccessor.Create(typeof(Sample));
            var s = new Sample();
            Assert.Throws<MissingMemberException>(() => accessor[s, nameof(Sample.ReadOnlyProp)] = "x");
        }

        [Fact]
        public void Ordinal_RoundTrip()
        {
            var accessor = TypeAccessor.Create(typeof(Sample));
            Assert.True(accessor.TryGetOrdinal(nameof(Sample.Value), out var ordinal));
            var s = new Sample { Value = 3 };
            Assert.Equal(3, accessor.GetValue(s, ordinal));
            accessor.SetValue(s, ordinal, 44);
            Assert.Equal(44, s.Value);
        }

        [Fact]
        public void Ordinal_OutOfRange_Throws()
        {
            var accessor = TypeAccessor.Create(typeof(Sample));
            Assert.Throws<ArgumentOutOfRangeException>(() => accessor.GetValue(new Sample(), 999));
        }

        [Fact]
        public void Members_ExcludesIndexersAndConstants()
        {
            var accessor = TypeAccessor.Create(typeof(Sample));
            var names = accessor.GetMembers().Select(m => m.Name).ToArray();
            Assert.Contains(nameof(Sample.Value), names);
            Assert.Contains(nameof(Sample.PublicField), names);
            Assert.DoesNotContain("Item", names);
            Assert.DoesNotContain(nameof(Sample.ConstField), names);
        }

        [Fact]
        public void Members_MetadataExposed()
        {
            var accessor = TypeAccessor.Create(typeof(Sample));
            var member = accessor.GetMembers().First(m => m.Name == nameof(Sample.Value));
            Assert.True(member.IsProperty);
            Assert.True(member.CanRead);
            Assert.True(member.CanWrite);
            Assert.NotNull(member.MemberInfo);

            var rof = accessor.GetMembers().First(m => m.Name == nameof(Sample.ReadOnlyField));
            Assert.True(rof.IsField);
            Assert.False(rof.CanWrite);
        }

        [Fact]
        public void CreateNew_PublicCtor()
        {
            var accessor = TypeAccessor.Create(typeof(Sample));
            Assert.True(accessor.CreateNewSupported);
            Assert.IsType<Sample>(accessor.CreateNew());
        }

        [Fact]
        public void CreateNew_NoCtor()
        {
            var accessor = TypeAccessor.Create(typeof(NoPublicCtor));
            Assert.False(accessor.CreateNewSupported);
            Assert.Throws<InvalidOperationException>(() => accessor.CreateNew());
        }

        [Fact]
        public void CreateNew_ValueType()
        {
            var accessor = TypeAccessor.Create(typeof(SampleStruct));
            Assert.True(accessor.CreateNewSupported);
            Assert.Equal(new SampleStruct(0, 0), accessor.CreateNew());
        }

        [Fact]
        public void NonPublic_Flag_GatesMembers()
        {
            var pub = TypeAccessor.Create(typeof(Sample));
            var np = TypeAccessor.Create(typeof(Sample), allowNonPublicAccessors: true);
            var s = new Sample();

            Assert.Throws<MissingMemberException>(() => pub[s, "InternalProp"] = 1);
            np[s, "InternalProp"] = 5;
            Assert.Equal(5, s.InternalProp);

            np[s, "PrivateProp"] = 8;
            Assert.Equal(8, s.GetPrivateProp());
        }

        [Fact]
        public void InterfaceAccessor_WorksOnImplementation()
        {
            var accessor = TypeAccessor.Create(typeof(ISample));
            var s = new Sample { Value = 30, Name = "n" };
            Assert.Equal(30, accessor[s, "Value"]);
            Assert.Equal("n", accessor[s, "Name"]);
        }

        [Fact]
        public void InterfaceAccessor_MergedInheritedMembers()
        {
            var accessor = TypeAccessor.Create(typeof(ISampleCombined));
            var names = accessor.GetMembers().Select(m => m.Name).ToArray();
            Assert.Contains("Value", names);
            Assert.Contains("Name", names);
            Assert.Contains("Extra", names);
            Assert.Equal(3, names.Length);
        }

        [Fact]
        public void Accessor_CachedPerTypeAndFlag()
        {
            Assert.Same(TypeAccessor.Create(typeof(Sample)), TypeAccessor.Create(typeof(Sample)));
            Assert.NotSame(
                TypeAccessor.Create(typeof(Sample)),
                TypeAccessor.Create(typeof(Sample), allowNonPublicAccessors: true));
        }

        [Fact]
        public void ObjectAccessor_WrapsTarget()
        {
            var s = new Sample();
            var wrapped = ObjectAccessor.Create(s);
            wrapped["Value"] = 12;
            Assert.Equal(12, wrapped["Value"]);
            Assert.Same(s, wrapped.Target);
            Assert.Equal(12, s.Value);
        }

        [Fact]
        public void ObjectAccessor_EqualsDelegatesToTarget()
        {
            var s = new Sample();
            Assert.True(ObjectAccessor.Create(s).Equals(s));
        }
    }
}
