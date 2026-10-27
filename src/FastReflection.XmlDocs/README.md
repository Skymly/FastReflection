# Skymly.FastReflection.XmlDocs

XML documentation file reads for reflection objects — summary, remarks, tag, element and parameter docs on `MemberInfo`/`Type`/`ParameterInfo`/`Assembly`. Satellite of [Skymly.FastReflection](https://github.com/Skymly/FastReflection).

```csharp
using FastReflection.XmlDocs;

var summary = typeof(Person).GetXmlDocsSummary();
var remarks = method.GetXmlDocsRemarks();
var element = method.GetXmlDocsElement();          // the member's XElement
var paramDoc = method.GetParameters()[0].GetXmlDocs();

// override the resolved file, or disable the NuGet-cache fallback
var options = new XmlDocsOptions { PathToXmlFile = path, ResolveExternalXmlDocs = false };
```

XML files resolve beside the member's assembly (or via `XmlDocsOptions.PathToXmlFile`); parsed files are cached per assembly and release with it. A missing or malformed file yields empty strings and null elements, never an exception.
