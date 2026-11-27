# FastReflection

[English](README.md) | **简体中文** | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md)

[![CI](https://github.com/Skymly/FastReflection/actions/workflows/ci.yml/badge.svg)](https://github.com/Skymly/FastReflection/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

高性能 .NET 运行时反射库。在支持动态代码生成的环境中使用编译委托跑在热路径上，在不支持的环境中自动回退到标准反射（兼容 NativeAOT 与裁剪）。

- **成员访问器（Access）**：提供 `CreateInvoker`、`CreateSpanInvoker`、`CreateGetter`、`CreateSetter` 及 `Ref<T>` 按引用参数支持，另提供无适配精确绑定的 `Unreflect<TDelegate>()`。
- **类型与对象访问器**：`TypeAccessor` 与 `ObjectAccessor` 支持按名称或序号快速读写属性/字段、`CreateNew` 以及带重载解析的绑定调用（`Invoke`、`GetValue`、`SetValue`）。
- **NativeAOT 与裁剪就绪**：标注 `IsAotCompatible` 与 `DynamicallyAccessedMembers`，并通过 `net8.0` / `net10.0` 的 `PublishAot` 冒烟应用验证。
- **配套包**：`Skymly.FastReflection.ObjectReader`（`DbDataReader` 适配）与 `Skymly.FastReflection.XmlDocs`（XML 文档读取）。

**目标框架**：`net462`、`netstandard2.0`、`net8.0`、`net10.0` · 领域词汇表：[CONTEXT.md](CONTEXT.md)

> **边界说明**：聚焦运行时已知成员与封闭类型访问器（`TypeAccessor` 仅列出实例属性与字段）；不包含 DLR / `dynamic` 分派、用户自定义转换、成员路径语法/按名称事件，以及编译期源码生成。

## 快速开始

```csharp
using FastReflection;
using System.Reflection;

// 基于方法的 access。ref/out 会写回数组；值类型会装箱。
MethodInfo add = typeof(Foo).GetMethod("Add")!;
FastInvoker invoker = add.CreateInvoker();
object? result = invoker(target, new object?[] { 2, 3 });

// 基于非索引器属性的 access。静态 access 无需 target。
Func<object?, object?> get = typeof(Foo).GetProperty("Value")!.CreateGetter();
Action<object?, object?> set = typeof(Foo).GetProperty("Value")!.CreateSetter();
set(target, 42);

// 不属于 access：精确签名绑定，无适配、无回退。
var typed = add.Unreflect<Func<Foo, int, int, int>>();
int sum = typed(foo, 2, 3);

// Ref 代表一个按引用传递的参数。access 直接原地读写 Value。
delegate bool Parse(Foo target, string text, out Ref<int> value);
var parse = typeof(Foo).GetMethod("TryParse")!.CreateInvoker<Parse>();
parse(foo, "12", out var parsed); // parsed.Value == 12

// 类型访问器：封闭类型在某一可见性下的 Members。名称区分大小写。
TypeAccessor typeAccessor = TypeAccessor.Create(typeof(Foo));
typeAccessor[target, "Value"] = 42;
int ord = typeAccessor.TryGetOrdinal("Value", out var i) ? i : -1;
object? v = typeAccessor.GetValue(target, ord); // Ordinal 是当前类型访问器中的索引，而非名称
object? instance = typeAccessor.CreateNew();    // 不是 Member；值类型的默认值不是 access

// 对象访问器：对象运行时类型对应的类型访问器，并与该对象绑定。
ObjectAccessor wrapped = ObjectAccessor.Create(target);
wrapped["Value"] = 42;
```

## 绑定调用

绑定调用（bound call）一次性解析区分大小写的名称和实参签名，然后作为经过适配的 invoker 通过与 access 相同的底层机制执行——包括反射回退。它不属于 access，也不属于 Member：其作用域在实例 Member 域的基础上增加了方法、索引器、静态成员和构造函数。

```csharp
TypeAccessor accessor = TypeAccessor.Create(typeof(Foo));

accessor.Invoke(target, "Add", 2, 3);                       // 按名称调用方法；解析重载
accessor.SetValue(target, "Value", 42);                     // 按名称写属性/字段
object? item = accessor.GetValue(target, "Item", 0);        // 索引器以其 Item 名称绑定
object created = accessor.CreateNew(new object?[] { "x" }); // 按实参匹配构造函数

// 静态成员按相同方式绑定，并忽略 target。
accessor.Invoke(null, "GetDefault", Array.Empty<object?>());

// 命名实参按参数名重排；泛型方法显式传入类型实参。
accessor.Invoke(target, "Configure", args, namedArgs, typeArguments: new[] { typeof(int) });

// 每个动词都有对应的 Try* 版本：名称未命中或绑定失败返回 false，不吞掉其他异常。
accessor.TryInvoke(target, "Add", args, out object? result);

// 所有不重复的可绑定名称：方法、属性、字段以及索引器 Item 名称。
IReadOnlyList<string> names = accessor.GetMemberNames();

// 有界转换器 —— 而非按名称查找。失败抛出 InvalidCastException。
Func<object?, object?> convert = ReflectionExtensions.CreateConverter(typeof(int), typeof(long));
```

- 参数转换仅限受认可的集合：恒等转换、引用赋值、拆箱、`Nullable<T>`、枚举↔底层整数，以及 C# 规范中的基元拓宽（含 `→decimal`，不含 `byte→char`）；`null` 仅适用于可空目标。绝不应用用户自定义转换、泛型类型推断、常量窄化与扩展方法查找。
- `params` 支持常规形态或展开形态绑定；可选参数自动补全；无需替换的候选优于需要补全可选参数的候选。命名参数匹配区分大小写。
- 契约化的失败顺序（[ADR-0004](docs/adr/0004-one-exception-contract-for-emit-and-reflection-completion.md)）：`name` 为 null → `ArgumentNullException`；名称不存在 → `MissingMemberException`；无适用候选 → `MissingMethodException`；最佳匹配平局 → `AmbiguousMatchException`；`args` 为 null → `ArgumentNullException`；target 规则不符 → `ArgumentNullException`/`InvalidCastException`；逐参数转换失败 → `InvalidCastException`；被调用方异常原样透传不包装。`Try*` 仅在查找阶段返回 `false`。
- 绑定后的 invoker 按成员类别、名称与规范化签名作为键缓存在类型访问器上；没有公开刷新方法，缓存增长仅受限于调用点多样性。

## Members 与排序

`GetMembers()` 返回一个类型访问器的 `IReadOnlyList<Member>`——仅包含实例属性与字段，绝不包含方法、索引器、常量或静态成员。

```csharp
foreach (Member m in TypeAccessor.Create(typeof(Foo)).GetMembers())
{
    string name = m.Name;          // 区分大小写
    Type type = m.Type;
    int ordinal = m.Ordinal;       // 在该访问器列表中的位置索引
    bool rw = m.CanRead && m.CanWrite;
    var attr = m.GetAttribute<DisplayAttribute>();
}
```

`OrdinalAttribute`（作用于 `property`/`field`，接收一个 `int` 值）决定列表排序：带特性的成员排在前面并按值升序；未带特性的成员紧随其后；相等的值保留枚举顺序。`Member.Ordinal` 始终代表结果列表中的位置索引——该特性只是排序输入，绝非对外暴露的索引值。

## 值得注意的能力

- 只要你持有对应的反射对象，就可以为非公开的属性、字段、方法或构造函数构建 access。同一封闭类型的公开与非公开类型访问器彼此独立。
- `object[]` 或 `Span<object?>` 的 access 会将 `ref`/`out` 写回缓冲区，并对值类型实参和返回值进行装箱。`Ref<T>` 不使用该缓冲区槽位。
- 没有 setter 但返回 `ref` 的属性仍可拥有写入 access，除非它是 `ref readonly`。这种写入能力不会使该属性成为可写的 Member。
- 对已装箱值类型的写入会原地更新该装箱对象。参见 [ADR 0001](docs/adr/0001-boxed-value-writes-update-the-box.md)。
- 静态 access 没有 target。实例 access 需要非 null 且兼容的 target：运行时类型必须是声明类型、其子类或实现类型，或者 target 是该值类型的装箱。
- 索引器属性不是 Member，也不适用 `CreateGetter` / `CreateSetter`。请通过 `CreateInvoker` 调用 `get_Item` / `set_Item`。
- 开放泛型类型没有类型访问器。开放泛型方法必须先构造为封闭方法，才能被 access 覆盖。
- `CreateInvoker<TDelegate>()` 使用 `CreateDelegate` 绑定公开精确签名；这种绑定不属于 access。适配绑定（包括 `Ref<T>` 以及 `CreateDelegate` 无法绑定的非公开方法）才属于 access，并在动态代码不可用时抛出异常。
- `net8.0+` 的 `CreateSpanInvoker` 是基于调用方 `Span<object?>` 缓冲区的 access（引用类型的 span 无法使用 `stackalloc`）。它仍会装箱。当动态代码不可用时，回退至 BCL 的 `MethodInvoker` / `ConstructorInvoker`。
- `CanWrite` 表示该类型访问器可以写入该 Member（包括构造完成后的 init-only 属性）。这并不意味着 C# 源码可以直接赋值，也不会强制校验 `required`。
- 一个名称至多标识一个 Member，不保证哪一个声明胜出。Ordinal 在其所属的类型访问器之外不具备标识意义。

## 明确非目标（Non-goals）

- `IDynamicMetaObjectProvider` / `dynamic` / DLR 分派 —— 属于 [dynamitey](https://github.com/ekonbenefits/dynamitey) 的范畴
- 用户自定义或推断转换 —— 仅支持有界转换集合
- 按名称访问的成员路径与事件 —— 1.0 之后考虑
- NRT / 上下文元数据 —— 1.0 之后的文档工具；XML 文档读取已交付于 `Skymly.FastReflection.XmlDocs`
- 编译期源生成访问 —— 1.0 之后的配套生成器

## AOT 与裁剪（Trimming）

所有包均以 `IsAotCompatible` 构建，使裁剪、AOT 与单文件分析告警（IL2xxx/IL3xxx）在库构建期即失败，而不是在使用方发布时才暴露。契约规范（[policy #9](https://github.com/Skymly/FastReflection/issues/9)）：

- `CreateInvoker<TDelegate>` 是唯一标注了 `RequiresDynamicCode` 的公开成员 —— 因为适配绑定需要动态代码。`Unreflect<TDelegate>` 是面向公开签名的 AOT 安全精确绑定。
- `TypeAccessor.Create`、`ObjectAccessor.Create`、`CreateFactory(Type)` 与 `TDelegate` 类型参数带有 `DynamicallyAccessedMembers` 并集标注，因此裁剪时会保留访问器域可发现的成员。
- 当动态代码不可用时，普通反射能完成的 access 仍可完成（Span invoker 会回退到 BCL `MethodInvoker`/`ConstructorInvoker`）；适配 `Ref<T>` 的绑定则会抛出 `NotSupportedException`。
- CI 中的 linux-x64 `PublishAot` 冒烟应用会对该契约进行端到端验证。附属包遵循同样的标注规范。

静态访问器缓存以强引用将 `Type` 作为键，这会固定可回收的 `AssemblyLoadContext` —— 核心库不做卸载承诺（已在[缓存调研](https://github.com/Skymly/FastReflection/blob/827160a58d0648e69532b56ac408679f0b4c2c24/docs/research/2026-10-24-cache-alc-unload.md)中通过探针验证）。附属包缓存使用 `ConditionalWeakTable`，随程序集一同释放。

## 从 fast-member 迁移

核心库逐项覆盖了 fast-member 的常用接口；`ObjectReader` 移至 `Skymly.FastReflection.ObjectReader` 包。

| FastMember | Skymly.FastReflection |
|---|---|
| `TypeAccessor.Create(type)` | `TypeAccessor.Create(type)` —— 完全一致，包含 `allowNonPublicAccessors` |
| `accessor[target, "Name"]` 读/写 | 相同索引器 —— 名称未命中抛出 `MissingMemberException`；写入不可写 Member 抛出 `InvalidOperationException`（**差异点**） |
| `ObjectAccessor.Create(obj)`, `wrapped["Name"]` | 相同 —— `Equals` 保持对称：访问器永不等于原始 target |
| `CreateNew()`, `CreateNewSupported` | 相同；额外增加用于绑定构造的 `CreateNew(args, namedArgs)` / `TryCreateNew` |
| `GetMembers()` → `MemberSet` | `GetMembers()` → `IReadOnlyList<Member>`；无 `MemberSet`/`GetMembersSupported` —— 请使用 LINQ 过滤 |
| `Member.Name`/`.Type`/`.Ordinal`/`.CanRead`/`.CanWrite`/`.IsDefined`/`.GetAttribute` | 名称相同 |
| `[Ordinal(n)]` | `FastReflection.OrdinalAttribute` —— **差异点**：`Member.Ordinal` 是排序后列表中的*位置*，而不是特性值；特性仅用于排序 |
| `ObjectReader.Create(source, members)` | `Skymly.FastReflection.ObjectReader` 包 —— **差异点**：未知成员名在 `Create` 时即抛出 `MissingMemberException`（不会静默生成 `object` 列） |
| 继承子类化访问器 | wontfix —— `Create` 是唯一获取途径 |

本库不提供 `FastMember` 源码兼容垫片（wontfix）—— 请对照本表迁移调用点。

## 从 Dynamitey 迁移

所有生产常用的动词都映射到 `TypeAccessor`/`ObjectAccessor` 上的绑定调用 —— 使用同一名称查找域，无 DLR 或 `Microsoft.CSharp` 依赖。

| Dynamitey (`Dynamic.*`) | Skymly.FastReflection |
|---|---|
| `InvokeGet` / `InvokeSet` | 按名称调用 `GetValue` / `SetValue`（外加 `TryGetValue` / `TrySetValue`） |
| `InvokeMember` / `InvokeMemberAction` | `Invoke` / `TryInvoke`（对于 action 忽略装箱返回值） |
| `InvokeConstructor` | 类型访问器上的 `CreateNew(args, namedArgs)` / `TryCreateNew` |
| `InvokeGetIndex` / `InvokeSetIndex` | 传入 `params object?[]? index` 的 `GetValue` / `SetValue` —— 索引器按其 Item 名称绑定 |
| `InvokeContext` / `StaticContext`（静态） | `TypeAccessor` 上的绑定调用；静态成员忽略 `target` |
| 命名实参 / 泛型方法 | `Invoke(…, namedArgs, typeArguments)` —— 命名实参按名重排；类型实参显式传入（不做推断） |
| `InvokeConvert` | `ReflectionExtensions.CreateConverter(sourceType, targetType)` —— **差异点**：仅限有界转换；绝不查找用户自定义的 `op_Implicit`/`op_Explicit` |
| `GetMemberNames` | 任一访问器上的 `GetMemberNames()` |
| `CacheableInvocation` / `CreateCallSite` | 自动处理 —— 绑定后的 invoker 保存在访问器缓存中 |
| 对已有可调用对象执行 `Dynamic.Invoke` / `InvokeAction` | wontfix —— 通过 `delegate.DynamicInvoke(…)` 或基于其 `Method` 调用 `CreateInvoker` 组合 |
| `InvokeSetAll`、成员链、按名称事件、运算符动词、COM、`IDMOP` 目标 | wontfix / 1.0 之后 —— 参见 [1.0 规划](docs/plans/1.0.md) |
| `Curry` / `Tupler` / `Builder` / `DynamicObjects` 系列 / `Linq` / `ConvertAll` | wontfix |
| `IsDBNull`, `GenericDelegateType`, `FastDynamicInvoke`, `ClearCaches`, `ApplyEquivalentType` | wontfix —— BCL 已涵盖的基础能力 |

**差异点：任何路径均不产生 `TargetInvocationException`** —— 无论 emit 路径还是反射回退路径，被调用方异常均原样透传（ADR-0004）。所有名称均区分大小写。

## 从 Namotion.Reflection 迁移

XML 文档读取交付于附属包 `Skymly.FastReflection.XmlDocs`，使用相同的方法拼写，命名空间为 `FastReflection.XmlDocs`。

| Namotion.Reflection | Skymly.FastReflection.XmlDocs |
|---|---|
| `GetXmlDocsSummary` / `GetXmlDocsRemarks` | `MemberInfo`/`Type` 上名称完全一致 |
| `GetXmlDocsTag(name)` / `GetXmlDocsElement()` | 完全一致；元素类型为 `XElement?` |
| `parameterInfo.GetXmlDocs()` | 完全一致 |
| `XmlDocsOptions` (`PathToXmlFile`, `ResolveExternalXmlDocs`) | 相同的选项类型 |
| `GetXmlDocsPath`, `ToXmlDocsContent`, `GetXmlDocsRecordPropertySummary` | wontfix |
| `XmlDocs.ClearCache` / `CachedType.ClearCache` | 无公开刷新方法 —— 按程序集缓存随 `Assembly` 释放 |
| `Namotion.Reflection.Cecil` XML 文档 | wontfix |
| `ContextualType`/`CachedType`, `ToContextual*`/`GetContextual*`, 特性拆分, NRT `Nullability` | 1.0 之后的文档工具 |
| `HasProperty` / `TryGetPropertyValue` | wontfix —— `TryGetOrdinal`/`TryGetValue` 已覆盖该能力 |

## 从 DotNext.Reflection 迁移

| DotNext.Reflection | Skymly.FastReflection |
|---|---|
| `method.Unreflect<TDelegate>()` | `MethodInfo.Unreflect<TDelegate>()` —— 相同精确绑定契约，仅限公开签名 |
| `Unreflect()` → `DynamicInvoker` | `CreateInvoker()` → `FastInvoker`；构造函数 → `FastConstructorInvoker`；net8+ `CreateSpanInvoker` → `FastSpanInvoker`/`FastSpanConstructorInvoker` |
| `MemberGetter`/`MemberSetter` 委托转换 | `CreateGetter`/`CreateSetter` —— 支持开放形式与 `<TDeclaring, TValue>` 强类型形式 |
| `Type<T>.Method`/`.Property`/`.Field`, `MemberCache`, `IMember` 成员对象 | wontfix —— `TypeAccessor.Create(typeof(T))` + `GetMembers()`/`TryGetOrdinal` + `Member.MemberInfo` 覆盖查找与缓存 |
| `Function`/`Procedure` 实参包, `Bind` | wontfix —— `FastInvoker` 与绑定 `Invoke` 覆盖调用 |
| `Field<T,TValue>` `ref` 访问 | wontfix —— 不返回实时引用 |
| `Dynamic.Invoke` / `InvokeAction` | wontfix —— 使用 `Delegate.DynamicInvoke` / `CreateInvoker` 组合 |
| `Reflector.MemberOf`, `ExtensionRegistry`, `MethodExtensions.*`, `Devirtualize`, `DelegateType` | wontfix —— 存在 BCL 等价实现 |

### 明确记录的差异点

迁移时需要注意的差异集中列于此处：

- `Member.Ordinal` 是访问器成员列表中的位置索引 —— fast-member 在此处暴露的是 `[Ordinal]` *特性值*。
- `CreateConverter` 仅应用有界转换集合 —— Dynamitey 的 `InvokeConvert` 会查找用户自定义转换，而本库绝不查找。
- 全路径无 `TargetInvocationException` —— emit 路径与反射回退路径均原样透传被调用方异常。
- `ObjectReader.Create` 快速失败：未知成员名会在创建时直接抛出 `MissingMemberException`，而非产出静默 `object` 列。
- 写入不可写 `Member` 抛出 `InvalidOperationException` 而非 `MissingMemberException`（0.1 的缺陷，已为 1.0 修复 —— 参见 [CHANGELOG](CHANGELOG.md)）。
- 不附带 `FastMember` 源码兼容垫片；本节即等效迁移说明。

## 基准测试（BenchmarkDotNet ShortRun，本机实测）

收益在 .NET Framework 上最为显著，因为 `MethodInfo.Invoke` 在 .NET Framework 上尚未获得 .NET 7+ 引入的运行时级加速：

| 方法 | .NET Framework 4.8.1 | net8.0 |
|---|---:|---:|
| DirectCall | ~0.6 ns | ~0 ns |
| PropertyInfo.GetValue | 109.84 ns | 9.54 ns |
| **CreateGetter** | **8.19 ns（快 13×）** | **5.37 ns** |
| Type accessor by name | 35.95 ns | 13.56 ns |
| Type accessor by ordinal | 7.01 ns | 6.03 ns |
| MethodInfo.Invoke | 216.34 ns | 18.58 ns |
| BCL MethodInvoker | n/a | 11.36 ns |
| **CreateInvoker** | **6.39 ns（快 34×）** | **5.59 ns** |

（每次反射调用分配 24 B = 所有路径共用的已装箱 `int` 返回值。）

## 仓库结构

```
CONTEXT.md                      领域词汇表
CHANGELOG.md                    发布说明（1.0.0 未发布）
docs/adr/                       架构决策记录
src/FastReflection/             核心库（net462;netstandard2.0;net8.0;net10.0）
src/FastReflection.ObjectReader/  附属包：IEnumerable → DbDataReader（相同 TFM 集合）
src/FastReflection.XmlDocs/       附属包：XML 文档读取（相同 TFM 集合）
tests/FastReflection.Tests/     xunit（net472 → net462 构建；net6.0 → netstandard2.0；net8.0；net10.0）
tests/FastReflection.AotSmoke/  PublishAot 冒烟应用（net8.0;net10.0）
benchmarks/                     BenchmarkDotNet（net472;net8.0;net10.0）
docs/research/                  设计调研笔记
```

## 构建与测试

```bash
dotnet build FastReflection.slnx
dotnet test tests/FastReflection.Tests/FastReflection.Tests.csproj            # 全部目标框架
dotnet test tests/FastReflection.Tests/FastReflection.Tests.csproj -f net8.0  # 单个目标框架
dotnet run -c Release -p benchmarks/FastReflection.Benchmarks -f net8.0 -- --job short
dotnet run -c Release -p benchmarks/FastReflection.Benchmarks -f net472 -- --job short
```

## 发布

CI 由 [Nuke](https://nuke.build) 驱动（`build.cmd <Target>`：`Clean`/`Restore`/`Compile`/`Test`/`Pack`/`Publish`）。

- `ci.yml` 在每次向 `main` 推送或提 PR 时运行 `Test`（Windows 上运行全部四个测试目标；Ubuntu 上运行非 Framework 目标及 linux-x64 AOT 冒烟）。
- `publish.yml` 在匹配 `v*.*.*` 的标签上运行 `Publish`，并将三个 `Skymly.FastReflection*` 包推送到 nuget.org。需要名为 `NUGET_API_KEY` 的仓库 Secret；统一版本号来自 `Directory.Build.props` 中的 `<Version>` —— 打标签前请先递增该版本号。

```bash
git tag v0.1.0 && git push --tags   # 触发 nuget.org 发布
```
