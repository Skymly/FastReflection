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
        public void Summary_OnConstructor_ReadsCtorEntry()
        {
            var ctor = typeof(Person).GetConstructor(Type.EmptyTypes)!;
            Assert.Equal("Creates a person.", ctor.GetXmlDocsSummary());
        }

        [Fact]
        public void Summary_OnEvent_ReadsEventEntry()
        {
            var renamed = typeof(Person).GetEvent(nameof(Person.Renamed))!;
            Assert.Equal("Raised on rename.", renamed.GetXmlDocsSummary());
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

        [Fact]
        public void Summary_OnArrayType_IsAMiss()
        {
            // No `T:` entry exists for element types — the read must not alias System.Int32's docs.
            Assert.Equal(string.Empty, typeof(int[]).GetXmlDocsSummary());
            Assert.Null(typeof(int[]).GetXmlDocsElement());
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
            Assert.Throws<ArgumentNullException>(() => member.GetXmlDocsElement());
            Assert.Throws<ArgumentNullException>(() => typeof(Person).GetXmlDocsTag(null!));
            Assert.Throws<ArgumentNullException>(() => parameter.GetXmlDocs());
            Assert.Throws<ArgumentNullException>(() => assembly.GetXmlDocsElement());
        }

        // Member-name generation: signatures resolve in documentation-id form so the
        // generated doc file entry matches.

        [Fact]
        public void Summary_OnConstructedGenericType_ResolvesToDefinition()
        {
            Assert.Equal("Box summary.", typeof(Box<int>).GetXmlDocsSummary());
        }

        [Fact]
        public void Summary_OnConversionOperator_ResolvesReturnTypeSuffix()
        {
            var conversion = typeof(Converts).GetMethod("op_Implicit")!;
            Assert.Equal("Converts to an int.", conversion.GetXmlDocsSummary());
        }

        [Fact]
        public void Summary_OnRefPointerMultidimAndGenericSignatures_Resolves()
        {
            var type = typeof(Converts);
            Assert.Equal("Reads a ref parameter.", type.GetMethod(nameof(Converts.Ref))!.GetXmlDocsSummary());
            Assert.Equal("Reads a pointer parameter.", type.GetMethod(nameof(Converts.Ptr))!.GetXmlDocsSummary());
            Assert.Equal("Reads a two-dimensional array.", type.GetMethod(nameof(Converts.Grid))!.GetXmlDocsSummary());
            Assert.Equal("Reads a two-argument constructed generic.", type.GetMethod(nameof(Converts.Map))!.GetXmlDocsSummary());
        }

        [Fact]
        public void ParamDocs_OnUndocumentedMember_ReturnsEmpty()
        {
            var parameter = typeof(Person).GetMethod(nameof(Person.UndocumentedParam))!.GetParameters()[0];
            Assert.Equal(string.Empty, parameter.GetXmlDocs());
        }

        [Fact]
        public void Element_OnUnkownMemberKind_ThrowsArgument()
        {
            // MemberInfo kinds outside the documented set (type, method, constructor,
            // property, field, event) have no XML documentation entry.
            Assert.Throws<ArgumentException>(() => new WeirdMember().GetXmlDocsElement());
        }

        // Flattening: see cref/paramref/unknown elements collapse to text; empty tags flatten
        // to the empty string. Fixture.xml drives these arms deterministically.

        [Fact]
        public void Summary_FlattensParamrefSeeAndUnknownTags()
        {
            var method = typeof(Person).GetMethod(nameof(Person.Combine))!;
            Assert.Equal("Uses a with Combine and bold and  text.", method.GetXmlDocsSummary(FixtureOptions()));
        }

        [Fact]
        public void Summary_OnEmptyTag_ReturnsEmpty()
        {
            var method = typeof(Person).GetMethod(nameof(Person.UndocumentedParam))!;
            Assert.Equal(string.Empty, method.GetXmlDocsSummary(FixtureOptions()));
        }

        // Path resolution legs: beside the assembly, then the app base directory, then the
        // NuGet package cache when ResolveExternalXmlDocs is on.

        [Fact]
        public void Element_OnAssemblyInOtherDirectory_ResolvesFromBaseDirectory()
        {
            // A relocated FastReflection.dll has no XML file beside it; the base-directory
            // copy of FastReflection.xml satisfies the read.
            var temp = Path.Combine(Path.GetTempPath(), "xmldocs-loc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            var target = Path.Combine(temp, "FastReflection.dll");
            File.Copy(typeof(FastReflection.TypeAccessor).Assembly.Location, target);

            var relocated = Assembly.LoadFile(target);
            Assert.NotNull(relocated.GetXmlDocsElement());

            try { Directory.Delete(temp, recursive: true); }
            catch (Exception) { } // the loaded assembly keeps the file mapped until process exit
        }

#if NET9_0_OR_GREATER
        [Fact]
        public void ExternalNuGetPackageDocs_ResolveWhenEnabled()
        {
            // A fake global-packages cache with versioned package layouts (plus a non-version
            // directory) — the newest SemVer directory wins, not the lexical max.
            var root = Path.Combine(Path.GetTempPath(), "xmldocs-external-" + Guid.NewGuid().ToString("N"));
            var packageDirectory = Path.Combine(root, "fakelib");
            foreach (var version in new[] { "1.0.0", "10.0.0", "beta" })
            {
                var dir = Path.Combine(packageDirectory, version, "lib");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "FakeLib.xml"),
                    "<doc><assembly><name>FakeLib</name></assembly><members>" +
                    $"<member name=\"T:FakeLib.ExternalPerson\"><summary>{version} summary.</summary></member>" +
                    "</members></doc>");
            }

            // A persisted dynamic assembly has no XML file beside it or in the base
            // directory — only the external package probe can satisfy the read.
            var builder = new System.Reflection.Emit.PersistedAssemblyBuilder(
                new AssemblyName("FakeLib"), typeof(object).Assembly);
            builder.DefineDynamicModule("FakeLib")
                .DefineType("FakeLib.ExternalPerson", TypeAttributes.Public | TypeAttributes.Class)
                .CreateType();
            using var stream = new MemoryStream();
            builder.Save(stream);
            var externalType = Assembly.Load(stream.ToArray()).GetType("FakeLib.ExternalPerson")!;

            var previous = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", root);
            try
            {
                var options = new XmlDocsOptions { ResolveExternalXmlDocs = true };
                Assert.Equal("10.0.0 summary.", externalType.GetXmlDocsSummary(options));
                Assert.Equal(string.Empty,
                    externalType.GetXmlDocsSummary(new XmlDocsOptions { ResolveExternalXmlDocs = false }));
            }
            finally
            {
                Environment.SetEnvironmentVariable("NUGET_PACKAGES", previous);
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }
#endif

        private sealed class WeirdMember : MemberInfo
        {
            public override Type? DeclaringType => typeof(Person);
            public override MemberTypes MemberType => MemberTypes.Custom;
            public override Module Module => typeof(Person).Module;
            public override string Name => "Weird";
            public override Type? ReflectedType => typeof(Person);
            public override object[] GetCustomAttributes(bool inherit) => Array.Empty<object>();
            public override object[] GetCustomAttributes(Type attributeType, bool inherit) => Array.Empty<object>();
            public override bool IsDefined(Type attributeType, bool inherit) => false;
        }
    }
}
