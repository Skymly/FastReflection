using System.Reflection;
using Xunit;

namespace FastReflection.XmlDocs.Tests
{
    public class XmlDocsTests
    {
        private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixture.xml");

        private static XmlDocsOptions FixtureOptions() => new() { PathToXmlFile = FixturePath };

        // Reads resolve the test assembly's own generated documentation file beside the dll.

        [Fact]
        public void Summary_OnType_ReadsGeneratedDoc()
        {
            Assert.Equal("Person summary text.", typeof(Person).GetXmlDocsSummary());
        }

        [Fact]
        public void Summary_OnMemberInfoReceiver_ReadsGeneratedDoc()
        {
            MemberInfo member = typeof(Person);
            Assert.Equal("Person summary text.", member.GetXmlDocsSummary());
        }

        [Fact]
        public void Remarks_OnType_ReadsGeneratedDoc()
        {
            Assert.Equal("Person remarks text.", typeof(Person).GetXmlDocsRemarks());
        }

        [Fact]
        public void Tag_OnType_ReadsCustomTag()
        {
            Assert.Equal("Person example text.", typeof(Person).GetXmlDocsTag("example"));
        }

        [Fact]
        public void Summary_OnProperty_FlattensSeeRef()
        {
            var property = typeof(Person).GetProperty(nameof(Person.Name))!;
            Assert.Equal("Name summary with Person inline.", property.GetXmlDocsSummary());
        }

        [Fact]
        public void Summary_And_Remarks_OnField()
        {
            var field = typeof(Person).GetField(nameof(Person.Age))!;
            Assert.Equal("Age field summary.", field.GetXmlDocsSummary());
            Assert.Equal("Age remarks.", field.GetXmlDocsRemarks());
        }

        [Fact]
        public void Summary_MultiLine_CollapsesIndent()
        {
            var method = typeof(Person).GetMethod(nameof(Person.Sum))!;
            Assert.Equal("Multi-line summary\nsecond line here.", method.GetXmlDocsSummary());
        }

        [Fact]
        public void Tag_OnMethod_ReadsCustomTag()
        {
            var method = typeof(Person).GetMethod(nameof(Person.Combine))!;
            Assert.Equal("The OK response.", method.GetXmlDocsTag("response"));
        }

        [Fact]
        public void Element_OnMethod_ReturnsMemberElement()
        {
            var method = typeof(Person).GetMethod(nameof(Person.Combine))!;
            var element = method.GetXmlDocsElement();

            Assert.NotNull(element);
            Assert.Equal("member", element!.Name.LocalName);
            Assert.Equal("Combines two values.", element.Element("summary")!.Value.Trim());
            Assert.NotNull(element.Element("response"));
        }

        [Fact]
        public void ParamDocs_OnMethod_ReadParamTags()
        {
            var parameters = typeof(Person).GetMethod(nameof(Person.Combine))!.GetParameters();
            Assert.Equal("The first value.", parameters[0].GetXmlDocs());
            Assert.Equal("The second value.", parameters[1].GetXmlDocs());
        }

        [Fact]
        public void ParamDocs_OnReturnParameter_ReadsReturnsTag()
        {
            var method = typeof(Person).GetMethod(nameof(Person.Combine))!;
            Assert.Equal("The combined text.", method.ReturnParameter.GetXmlDocs());
        }

        [Fact]
        public void ParamDocs_OnParamsArray_ResolvesSignature()
        {
            var parameter = typeof(Person).GetMethod(nameof(Person.Sum))!.GetParameters()[0];
            Assert.Equal("The values.", parameter.GetXmlDocs());
        }

        // Generic members are documented on the definition in terms of type parameters.

        [Fact]
        public void Summary_OnOpenGenericMember_ResolvesSignature()
        {
            var method = typeof(Box<>).GetMethod(nameof(Box<int>.Wrap))!;
            Assert.Equal("Wraps the value.", method.GetXmlDocsSummary());
        }

        [Fact]
        public void Summary_OnClosedGenericMember_ResolvesToDefinition()
        {
            var method = typeof(Box<int>).GetMethod(nameof(Box<int>.Wrap))!;
            Assert.Equal("Wraps the value.", method.GetXmlDocsSummary());
            Assert.Equal("The value.", method.GetParameters()[0].GetXmlDocs());
        }

        [Fact]
        public void Summary_OnConstructedGenericParameter_ResolvesSignature()
        {
            var method = typeof(Box<>).GetMethod(nameof(Box<int>.Take))!;
            Assert.Equal("Takes a list of items.", method.GetXmlDocsSummary());
            Assert.Equal("The items.", method.GetParameters()[0].GetXmlDocs());
        }

        [Fact]
        public void Summary_OnOpenGenericIndexer_ResolvesSignature()
        {
            var indexer = typeof(Box<>).GetProperty("Item")!;
            Assert.Equal("Indexer summary.", indexer.GetXmlDocsSummary());
        }

        [Fact]
        public void Summary_OnNestedType_Resolves()
        {
            Assert.Equal("Nested type summary.", typeof(Outer.Inner).GetXmlDocsSummary());
        }

        // Misses degrade to empty strings and null elements — never an exception.

        [Fact]
        public void UndocumentedMember_ReturnsEmptyAndNull()
        {
            var method = typeof(Person).GetMethod(nameof(Person.Undocumented))!;
            Assert.Equal(string.Empty, method.GetXmlDocsSummary());
            Assert.Null(method.GetXmlDocsElement());
        }

        [Fact]
        public void MissingFile_ReturnsEmptyAndNull()
        {
            var options = new XmlDocsOptions { PathToXmlFile = Path.Combine(AppContext.BaseDirectory, "nope.xml") };
            Assert.Equal(string.Empty, typeof(Person).GetXmlDocsSummary(options));
            Assert.Null(typeof(Person).GetXmlDocsElement(options));
        }

        [Fact]
        public void MalformedFile_ReturnsEmptyAndNull()
        {
            // The test dll itself is not an XML document — the parse miss resolves to "no docs".
            var options = new XmlDocsOptions { PathToXmlFile = typeof(Person).Assembly.Location };
            Assert.Equal(string.Empty, typeof(Person).GetXmlDocsSummary(options));
            Assert.Null(typeof(Person).GetXmlDocsElement(options));
        }

        // Options: PathToXmlFile redirects the per-assembly resolution to the fixture file.

        [Fact]
        public void PathOverride_OnType_ReadsFixtureFile()
        {
            Assert.Equal("Fixture file summary.", typeof(Person).GetXmlDocsSummary(FixtureOptions()));
            Assert.Equal("Fixture file remarks.", typeof(Person).GetXmlDocsRemarks(FixtureOptions()));
        }

        [Fact]
        public void PathOverride_OnField_And_ParamDocs_ReadFixtureFile()
        {
            var field = typeof(Person).GetField(nameof(Person.Age))!;
            Assert.Equal("Fixture age summary.", field.GetXmlDocsSummary(FixtureOptions()));

            var parameters = typeof(Person).GetMethod(nameof(Person.Combine))!.GetParameters();
            Assert.Equal("Fixture param text.", parameters[0].GetXmlDocs(FixtureOptions()));
            var method = typeof(Person).GetMethod(nameof(Person.Combine))!;
            Assert.Equal("Fixture return text.", method.ReturnParameter.GetXmlDocs(FixtureOptions()));
        }

        // Assembly-level read: the resolved document's root element.

        [Fact]
        public void Element_OnAssembly_ReturnsDocRoot()
        {
            var root = typeof(Person).Assembly.GetXmlDocsElement();

            Assert.NotNull(root);
            Assert.Equal("doc", root!.Name.LocalName);
            Assert.NotNull(root.Element("members"));
        }

        [Fact]
        public void Element_OnAssembly_WithPathOverride_ReadsFixtureRoot()
        {
            var root = typeof(Person).Assembly.GetXmlDocsElement(FixtureOptions());

            Assert.NotNull(root);
            var members = root!.Element("members")!;
            Assert.Contains(members.Elements("member"),
                m => m.Attribute("name")?.Value == "T:FastReflection.XmlDocs.Tests.Person");
        }

        // Another shipped assembly's docs resolve beside that assembly.

        [Fact]
        public void Summary_OnCoreType_ReadsPackageDocs()
        {
            Assert.NotEqual(string.Empty, typeof(FastReflection.TypeAccessor).GetXmlDocsSummary());
        }

        // Argument validation stays outside the miss contract.

        [Fact]
        public void NullArguments_Throw()
        {
            MemberInfo member = null!;
            ParameterInfo parameter = null!;
            Assembly assembly = null!;
            Assert.Throws<ArgumentNullException>(() => member.GetXmlDocsSummary());
            Assert.Throws<ArgumentNullException>(() => typeof(Person).GetXmlDocsTag(null!));
            Assert.Throws<ArgumentNullException>(() => parameter.GetXmlDocs());
            Assert.Throws<ArgumentNullException>(() => assembly.GetXmlDocsElement());
        }
    }
}
