using System.Reflection;
using FastReflection.Emit;
using Xunit;

namespace FastReflection.Tests
{
    public class MemberAttributeTests
    {
        private static Member MemberNamed(string name)
            => TypeAccessor.Create(typeof(AttributedSample)).GetMembers().First(m => m.Name == name);

        [Fact]
        public void GetAttribute_Present_ReturnsAttribute()
        {
            var taggedProp = MemberNamed(nameof(AttributedSample.TaggedProp));
            Assert.Equal("p", ((TagAttribute)taggedProp.GetAttribute(typeof(TagAttribute))!).Name);
            Assert.Equal("p", taggedProp.GetAttribute<TagAttribute>()!.Name);

            var taggedField = MemberNamed(nameof(AttributedSample.TaggedField));
            Assert.Equal("f", ((TagAttribute)taggedField.GetAttribute(typeof(TagAttribute))!).Name);
            Assert.Equal("f", taggedField.GetAttribute<TagAttribute>()!.Name);
        }

        [Fact]
        public void GetAttribute_Miss_ReturnsNull()
        {
            var plain = MemberNamed(nameof(AttributedSample.Plain));
            Assert.Null(plain.GetAttribute<TagAttribute>());
            Assert.Null(plain.GetAttribute<TagAttribute>(inherit: false));
            Assert.Null(plain.GetAttribute(typeof(TagAttribute)));
            Assert.Null(plain.GetAttribute(typeof(TagAttribute), inherit: false));
        }

        [Fact]
        public void GetAttribute_Inherit_ReadsOverriddenBaseDeclaration()
        {
            var overridden = MemberNamed(nameof(AttributedSample.Overridden));
            Assert.NotNull(overridden.GetAttribute<MarkerAttribute>());
            Assert.Null(overridden.GetAttribute<MarkerAttribute>(inherit: false));
            Assert.NotNull(overridden.GetAttribute(typeof(MarkerAttribute)));
            Assert.Null(overridden.GetAttribute(typeof(MarkerAttribute), inherit: false));
        }

        [Fact]
        public void IsDefined_AgreesWithGetAttribute()
        {
            var cases = new[]
            {
                (nameof(AttributedSample.Overridden), typeof(MarkerAttribute)),
                (nameof(AttributedSample.TaggedField), typeof(TagAttribute)),
                (nameof(AttributedSample.Plain), typeof(MarkerAttribute))
            };
            foreach (var (name, attributeType) in cases)
            {
                var member = MemberNamed(name);
                foreach (var inherit in new[] { true, false })
                {
                    Assert.Equal(member.IsDefined(attributeType, inherit), member.GetAttribute(attributeType, inherit) != null);
                }
            }

            var overridden = MemberNamed(nameof(AttributedSample.Overridden));
            Assert.True(overridden.IsDefined(typeof(MarkerAttribute), inherit: true));
            Assert.False(overridden.IsDefined(typeof(MarkerAttribute), inherit: false));
        }

        [Fact]
        public void GetAttribute_NullType_Throws()
        {
            var member = MemberNamed(nameof(AttributedSample.TaggedProp));
            Assert.Throws<ArgumentNullException>(() => member.GetAttribute(null!));
            Assert.Throws<ArgumentNullException>(() => member.IsDefined(null!));
        }

        [Fact]
        public void GetAttribute_NonAttributeType_Throws()
        {
            var member = MemberNamed(nameof(AttributedSample.TaggedProp));
            Assert.Throws<ArgumentException>(() => member.GetAttribute(typeof(string)));
            Assert.Throws<ArgumentException>(() => member.IsDefined(typeof(string)));
        }

        [Fact]
        public void GetAttribute_MultipleMatches_Throws()
        {
            var member = MemberNamed(nameof(AttributedSample.TwiceTagged));
            Assert.Throws<AmbiguousMatchException>(() => member.GetAttribute<TagAttribute>());
            Assert.Throws<AmbiguousMatchException>(() => member.GetAttribute(typeof(TagAttribute)));
        }

        [Fact]
        public void OrdinalAttribute_Shape()
        {
            Assert.Equal(3, new OrdinalAttribute(3).Value);
            Assert.True(typeof(OrdinalAttribute).IsPublic);
            Assert.True(typeof(OrdinalAttribute).IsSealed);

            var usage = typeof(OrdinalAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;
            Assert.Equal(AttributeTargets.Property | AttributeTargets.Field, usage.ValidOn);
            Assert.False(usage.AllowMultiple);
        }

        [Fact]
        public void Ordering_OverriddenPropertyInheritsBaseOrdinal()
        {
            var names = TypeAccessor.Create(typeof(OrderedOverrideDerived))
                .GetMembers().Select(m => m.Name).ToArray();
            Assert.Equal(new[] { "Late", "Early" }, names);
        }
    }

    /// <summary>
    /// Asserts the OrdinalAttribute ordering contract identically on the emit path and on the
    /// reflection-fallback path (<see cref="EmitHelper.ForceReflectionFallback"/>).
    /// </summary>
    public abstract class MemberOrderingTestsBase : IDisposable
    {
        protected MemberOrderingTestsBase(bool fallback) => EmitHelper.ForceReflectionFallback = fallback;
        public void Dispose() => EmitHelper.ForceReflectionFallback = false;

        /// <summary>The per-leg fixture type: TypeAccessor caches by type, so each leg needs its own.</summary>
        protected abstract Type AccessorType { get; }

        private static object? ReadViaReflection(Member member, object target)
            => member.MemberInfo is PropertyInfo prop
                ? prop.GetValue(target)
                : ((FieldInfo)member.MemberInfo).GetValue(target);

        [Fact]
        public void GetMembers_AttributedAscendingThenUnattributed()
        {
            var values = TypeAccessor.Create(AccessorType).GetMembers()
                .Select(m => m.GetAttribute<OrdinalAttribute>()?.Value)
                .ToArray();
            Assert.Equal(new int?[] { -5, 1, 1, 1, 10, null, null, null }, values);
        }

        [Fact]
        public void GetMembers_TiesKeepEnumerationOrder()
        {
            var flags = BindingFlags.Public | BindingFlags.Instance;
            var enumeration = AccessorType.GetProperties(flags).Select(p => p.Name)
                .Concat(AccessorType.GetFields(flags).Select(f => f.Name))
                .ToArray();
            var memberNames = TypeAccessor.Create(AccessorType).GetMembers()
                .Select(m => m.Name).ToArray();

            foreach (var group in new[] { new[] { "TieA", "TieB", "TieC" }, new[] { "FreeA", "FreeB", "FreeC" } })
            {
                Assert.Equal(
                    enumeration.Where(group.Contains).ToArray(),
                    memberNames.Where(group.Contains).ToArray());
            }
        }

        [Fact]
        public void Ordinal_IsPositionalIndex_NotAttributeValue()
        {
            var accessor = TypeAccessor.Create(AccessorType);
            var members = accessor.GetMembers();
            for (var i = 0; i < members.Count; i++)
            {
                Assert.Equal(i, members[i].Ordinal);
                Assert.True(accessor.TryGetOrdinal(members[i].Name, out var ordinal));
                Assert.Equal(i, ordinal);
            }

            Assert.Equal(4, members.First(m => m.Name == nameof(OrderedSample.Ten)).Ordinal);
            Assert.Equal(0, members.First(m => m.Name == nameof(OrderedSample.MinusFive)).Ordinal);
        }

        [Fact]
        public void OrdinalReadWrite_HitsSortedMember()
        {
            var accessor = TypeAccessor.Create(AccessorType);
            var target = Activator.CreateInstance(AccessorType)!;
            foreach (var member in accessor.GetMembers())
            {
                Assert.Equal(ReadViaReflection(member, target), accessor.GetValue(target, member.Ordinal));

                var value = 1000 + member.Ordinal;
                accessor.SetValue(target, member.Ordinal, value);
                Assert.Equal(value, ReadViaReflection(member, target));
            }
        }
    }

    /// <summary>The emit path: the same ordering contract asserted with dynamic code available.</summary>
    public sealed class EmitMemberOrderingTests : MemberOrderingTestsBase
    {
        public EmitMemberOrderingTests() : base(fallback: false) { }
        protected override Type AccessorType => typeof(EmitLegOrdered);
    }

    /// <summary>The reflection-completion path: the same ordering contract asserted with dynamic code forced off.</summary>
    public sealed class ReflectionMemberOrderingTests : MemberOrderingTestsBase
    {
        public ReflectionMemberOrderingTests() : base(fallback: true) { }
        protected override Type AccessorType => typeof(ReflectionLegOrdered);
    }
}
