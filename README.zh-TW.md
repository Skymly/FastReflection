# FastReflection

[English](README.md) | [简体中文](README.zh-CN.md) | **繁體中文** | [日本語](README.ja.md)

[![CI](https://github.com/Skymly/FastReflection/actions/workflows/ci.yml/badge.svg)](https://github.com/Skymly/FastReflection/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

高效能 .NET 執行階段反射程式庫。在支援動態程式碼產生的環境中使用編譯委派走熱路徑，在受限環境中自動備援至一般反射（相容 NativeAOT 與裁剪）。

- **成員存取器（Access）**：提供 `CreateInvoker`、`CreateSpanInvoker`、`CreateGetter`、`CreateSetter` 與 `Ref<T>` 傳址參數支援，另提供無調適精確繫結的 `Unreflect<TDelegate>()`。
- **型別與物件存取器**：`TypeAccessor` 與 `ObjectAccessor` 支援依名稱或序號快速讀寫屬性/欄位、`CreateNew`，以及含多載解析的繫結呼叫（`Invoke`、`GetValue`、`SetValue`）。
- **NativeAOT 與裁剪就緒**：標註 `IsAotCompatible` 與 `DynamicallyAccessedMembers`，並透過 `net8.0` / `net10.0` 的 `PublishAot` 冒煙應用程式驗證。
- **配套套件**：`Skymly.FastReflection.ObjectReader`（`DbDataReader` 轉接）與 `Skymly.FastReflection.XmlDocs`（XML 文件讀取）。

**目標架構**：`net462`、`netstandard2.0`、`net8.0`、`net10.0` · 領域詞彙表：[CONTEXT.md](CONTEXT.md)

> **範圍說明**：聚焦執行階段已知成員與封閉型別存取器（`TypeAccessor` 僅列出執行個體屬性與欄位）；不包含 DLR / `dynamic` 分派、使用者自訂轉換、成員路徑語法/依名稱查詢事件，以及編譯時期原始碼產生。

## 快速開始

```csharp
using FastReflection;
using System.Reflection;

// 基於方法的 access。ref/out 會寫回陣列；實值型別會進行裝箱。
MethodInfo add = typeof(Foo).GetMethod("Add")!;
FastInvoker invoker = add.CreateInvoker();
object? result = invoker(target, new object?[] { 2, 3 });

// 基於非索引子屬性的 access。靜態 access 沒有 target。
Func<object?, object?> get = typeof(Foo).GetProperty("Value")!.CreateGetter();
Action<object?, object?> set = typeof(Foo).GetProperty("Value")!.CreateSetter();
set(target, 42);

// 不屬於 access：精確簽章繫結，無調適、無備援。
var typed = add.Unreflect<Func<Foo, int, int, int>>();
int sum = typed(foo, 2, 3);

// Ref 代表一個傳址參數。access 會就地讀寫 Value。
delegate bool Parse(Foo target, string text, out Ref<int> value);
var parse = typeof(Foo).GetMethod("TryParse")!.CreateInvoker<Parse>();
parse(foo, "12", out var parsed); // parsed.Value == 12

// 型別存取器：封閉型別在單一可見性下的 Members。名稱區分大小寫。
TypeAccessor typeAccessor = TypeAccessor.Create(typeof(Foo));
typeAccessor[target, "Value"] = 42;
int ord = typeAccessor.TryGetOrdinal("Value", out var i) ? i : -1;
object? v = typeAccessor.GetValue(target, ord); // Ordinal 是此型別存取器中的索引，而非名稱
object? instance = typeAccessor.CreateNew();    // 不是 Member；實值型別的預設值不是 access

// 物件存取器：物件執行階段型別對應的型別存取器，並繫結至該物件。
ObjectAccessor wrapped = ObjectAccessor.Create(target);
wrapped["Value"] = 42;
```

## 繫結呼叫

繫結呼叫（bound call）會一次性解析區分大小寫的名稱加上引數簽章，接著作為調適後的 invoker 透過與 access 相同的機制執行——包含反射備援。它不屬於 access，也不屬於 Member：其領域在執行個體 Member 領域上擴充了方法、索引子、靜態成員與建構函式。

```csharp
TypeAccessor accessor = TypeAccessor.Create(typeof(Foo));

accessor.Invoke(target, "Add", 2, 3);                       // 依名稱呼叫方法；解析多載
accessor.SetValue(target, "Value", 42);                     // 依名稱設定屬性/欄位
object? item = accessor.GetValue(target, "Item", 0);        // 索引子以其 Item 名稱繫結
object created = accessor.CreateNew(new object?[] { "x" }); // 依引數匹配建構函式

// 靜態成員以相同方式繫結，並忽略 target。
accessor.Invoke(null, "GetDefault", Array.Empty<object?>());

// 具名引數依參數名稱重新排序；泛型方法接收明確的型別引數。
accessor.Invoke(target, "Configure", args, namedArgs, typeArguments: new[] { typeof(int) });

// 每個動詞皆有對應的 Try* 雙生方法：名稱未命中或繫結失敗時回傳 false，不吞掉其他例外。
accessor.TryInvoke(target, "Add", args, out object? result);

// 所有不重複的可繫結名稱：方法、屬性、欄位與索引子 Item 名稱。
IReadOnlyList<string> names = accessor.GetMemberNames();

// 有界轉換器 —— 非名稱查詢。失敗拋出 InvalidCastException。
Func<object?, object?> convert = ReflectionExtensions.CreateConverter(typeof(int), typeof(long));
```

- 引數轉換僅限於核准的集合：恆等轉換、參考指派、Unbox、`Nullable<T>`、列舉↔基礎整數，以及 C# 規格定義的基元擴寬（含 `→decimal`，不含 `byte→char`）；`null` 僅適用於可為 Null 的目標。絕不套用使用者自訂轉換、泛型型別推斷、常數窄化與擴充方法搜尋。
- `params` 支援一般形式或展開形式繫結；選用參數自動填補；無需替換的候選優先於需填補的候選。具名引數比對區分大小寫。
- 契約化的失敗順序（[ADR-0004](docs/adr/0004-one-exception-contract-for-emit-and-reflection-completion.md)）：`name` 為 null → `ArgumentNullException`；名稱不存在 → `MissingMemberException`；無適用候選 → `MissingMethodException`；最佳符合平手 → `AmbiguousMatchException`；`args` 為 null → `ArgumentNullException`；target 規則不符 → `ArgumentNullException`/`InvalidCastException`；逐引數轉換失敗 → `InvalidCastException`；被呼叫端例外狀況原樣向上傳遞不包裝。`Try*` 僅於查詢階段回傳 `false`。
- 繫結後的 invoker 以成員種類、名稱與正規化簽章為索引鍵快取於型別存取器上；不提供公開清除方法，成長僅受限於呼叫端點的多樣性。

## Members 與排序

`GetMembers()` 回傳一個型別存取器的 `IReadOnlyList<Member>`——僅包含執行個體屬性與欄位，絕不包含方法、索引子、常數或靜態成員。

```csharp
foreach (Member m in TypeAccessor.Create(typeof(Foo)).GetMembers())
{
    string name = m.Name;          // 區分大小寫
    Type type = m.Type;
    int ordinal = m.Ordinal;       // 在該存取器清單中的位置索引
    bool rw = m.CanRead && m.CanWrite;
    var attr = m.GetAttribute<DisplayAttribute>();
}
```

`OrdinalAttribute`（套用於 `property`/`field`，接收一個 `int` 值）決定清單順序：具屬性的成員排在前面並依值遞增排序；未套用屬性的成員接續其後；相等的值維持列舉順序。`Member.Ordinal` 始終維持結果清單中的位置索引——該屬性僅為排序輸入，絕非對外可見的索引值。

## 值得注意的功能

- 只要您持有反射物件，即可為非公開屬性、欄位、方法或建構函式建立 access。同一封閉型別的公開與非公開型別存取器互不相同。
- `object[]` 或 `Span<object?>` 的 access 會將 `ref`/`out` 寫回緩衝區，並對實值型別引數與傳回值進行裝箱。`Ref<T>` 並非該緩衝區槽位。
- 無 setter 但傳回 `ref` 的屬性仍可擁有寫入 access，除非其為 `ref readonly`。該寫入不會讓屬性成為可寫的 Member。
- 對已裝箱實值型別的寫入會就地更新該裝箱物件。請參閱 [ADR 0001](docs/adr/0001-boxed-value-writes-update-the-box.md)。
- 靜態 access 沒有 target。執行個體 access 需要非 null 且相容的 target：執行階段型別為宣告型別、子型別或實作型別，或 target 為該實值型別的裝箱。
- 索引子屬性不是 Member，亦非 `CreateGetter` / `CreateSetter` 的對象。請透過 `CreateInvoker` 呼叫 `get_Item` / `set_Item`。
- 開放式泛型型別沒有型別存取器。開放式泛型方法必須先建構後方可由 access 涵蓋。
- `CreateInvoker<TDelegate>()` 使用 `CreateDelegate` 繫結公開精確簽章；該繫結不是 access。調適繫結（包含 `Ref<T>` 與 `CreateDelegate` 無法繫結的非公開方法）才是 access，在動態程式碼不可用時會拋出例外狀況。
- `net8.0+` 的 `CreateSpanInvoker` 是基於呼叫端 `Span<object?>` 緩衝區的 access（參考型別的 span 無法使用 `stackalloc`）。它仍會進行裝箱。當動態程式碼不可用時，會備援至 BCL `MethodInvoker` / `ConstructorInvoker`。
- `CanWrite` 表示該型別存取器可寫入該 Member，包含建構後的 init-only 屬性。這並不代表 C# 可直接指派，且不強制驗證 `required`。
- 一個名稱最多識別一個 Member。不保證哪一個宣告勝出。Ordinal 在您持有的型別存取器之外不具識別意義。

## 明確非目標（Non-goals）

- `IDynamicMetaObjectProvider` / `dynamic` / DLR 分派 —— 屬於 [dynamitey](https://github.com/ekonbenefits/dynamitey) 的範疇
- 使用者自訂或推斷轉換 —— 僅限有界轉換集合
- 依名稱存取成員路徑與事件 —— 1.0 之後
- NRT / 內容中繼資料 —— 1.0 之後的文件工具；XML 文件讀取提供於 `Skymly.FastReflection.XmlDocs`
- 編譯時期原始碼產生存取 —— 1.0 之後的配套產生器

## AOT 與裁剪（Trimming）

各套件均以 `IsAotCompatible` 建置，因此裁剪、AOT 與單一檔案診斷問題（IL2xxx/IL3xxx）會在建置階段失敗，而非等到取用方發布時才發生。契約規範（[policy #9](https://github.com/Skymly/FastReflection/issues/9)）：

- `CreateInvoker<TDelegate>` 是唯一帶有 `RequiresDynamicCode` 的公開成員 —— 調適繫結需要它。`Unreflect<TDelegate>` 則是適用於公開簽章的 AOT 安全精確繫結。
- `TypeAccessor.Create`、`ObjectAccessor.Create`、`CreateFactory(Type)` 與 `TDelegate` 參數皆標註了 `DynamicallyAccessedMembers` 聯集，使裁剪保留存取器領域可探索的成員。
- 當動態程式碼不可用時，一般反射能完成的 access 仍可完成（Span invoker 會備援至 BCL `MethodInvoker`/`ConstructorInvoker`）；調適型 `Ref<T>` 繫結則會拋出 `NotSupportedException`。
- CI 中的 linux-x64 `PublishAot` 冒煙應用程式會對契約進行端對端驗證。附屬套件亦採用相同的標註規範。

靜態存取器快取會強參考 `Type` 作為索引鍵，這會釘住可回收的 `AssemblyLoadContext` —— 核心程式庫不做卸載承諾（已在[快取研究](https://github.com/Skymly/FastReflection/blob/827160a58d0648e69532b56ac408679f0b4c2c24/docs/research/2026-10-24-cache-alc-unload.md)中以探針驗證）。附屬套件快取採用 `ConditionalWeakTable`，會隨其組件一併釋放。

## 從 fast-member 遷移

核心程式庫一對一涵蓋 fast-member 的常用介面；`ObjectReader` 已移至 `Skymly.FastReflection.ObjectReader` 套件。

| FastMember | Skymly.FastReflection |
|---|---|
| `TypeAccessor.Create(type)` | `TypeAccessor.Create(type)` —— 完全一致，含 `allowNonPublicAccessors` |
| `accessor[target, "Name"]` get/set | 相同索引子 —— 名稱未命中拋出 `MissingMemberException`；寫入不可寫的 Member 拋出 `InvalidOperationException`（**差異**） |
| `ObjectAccessor.Create(obj)`, `wrapped["Name"]` | 相同 —— `Equals` 具對稱性：存取器永不等於原始 target |
| `CreateNew()`, `CreateNewSupported` | 相同；另新增用於繫結建構的 `CreateNew(args, namedArgs)` / `TryCreateNew` |
| `GetMembers()` → `MemberSet` | `GetMembers()` → `IReadOnlyList<Member>`；無 `MemberSet`/`GetMembersSupported` —— 請以 LINQ 篩選 |
| `Member.Name`/`.Type`/`.Ordinal`/`.CanRead`/`.CanWrite`/`.IsDefined`/`.GetAttribute` | 名稱相同 |
| `[Ordinal(n)]` | `FastReflection.OrdinalAttribute` —— **差異**：`Member.Ordinal` 是已排序清單中的*位置*，而非屬性值；該屬性僅用於排序 |
| `ObjectReader.Create(source, members)` | `Skymly.FastReflection.ObjectReader` 套件 —— **差異**：未知成員名稱會在 `Create` 時失敗並拋出 `MissingMemberException`（不產生靜默 `object` 資料行） |
| 繼承子類別化存取器 | wontfix —— `Create` 是唯一取得途徑 |

不提供 `FastMember` 原始碼相容墊片（wontfix）—— 請依此表移植呼叫端點。

## 從 Dynamitey 遷移

所有生產環境常用的動詞皆對應至 `TypeAccessor`/`ObjectAccessor` 上的繫結呼叫 —— 使用同一名稱查詢領域，不依賴 DLR 或 `Microsoft.CSharp`。

| Dynamitey (`Dynamic.*`) | Skymly.FastReflection |
|---|---|
| `InvokeGet` / `InvokeSet` | 依名稱呼叫 `GetValue` / `SetValue`（另有 `TryGetValue` / `TrySetValue`） |
| `InvokeMember` / `InvokeMemberAction` | `Invoke` / `TryInvoke`（action 可忽略裝箱傳回值） |
| `InvokeConstructor` | 型別存取器上的 `CreateNew(args, namedArgs)` / `TryCreateNew` |
| `InvokeGetIndex` / `InvokeSetIndex` | 含 `params object?[]? index` 的 `GetValue` / `SetValue` —— 索引子依其 Item 名稱繫結 |
| `InvokeContext` / `StaticContext`（靜態） | `TypeAccessor` 上的繫結呼叫；靜態成員忽略 `target` |
| 具名引數 / 泛型方法 | `Invoke(…, namedArgs, typeArguments)` —— 具名引數依名稱重排；型別引數為明確指定（無推斷） |
| `InvokeConvert` | `ReflectionExtensions.CreateConverter(sourceType, targetType)` —— **差異**：僅限有界轉換；絕不查詢使用者自訂 `op_Implicit`/`op_Explicit` |
| `GetMemberNames` | 任一存取器上的 `GetMemberNames()` |
| `CacheableInvocation` / `CreateCallSite` | 自動完成 —— 繫結後的 invoker 快取於存取器快取中 |
| 對持有之可呼叫物件執行 `Dynamic.Invoke` / `InvokeAction` | wontfix —— 搭配 `delegate.DynamicInvoke(…)` 或基於其 `Method` 的 `CreateInvoker` 組合使用 |
| `InvokeSetAll`、成員鏈、依名稱事件、運算子動詞、COM、`IDMOP` 目標 | wontfix / 1.0 之後 —— 請參閱 [1.0 計畫](docs/plans/1.0.md) |
| `Curry` / `Tupler` / `Builder` / `DynamicObjects` 家族 / `Linq` / `ConvertAll` | wontfix |
| `IsDBNull`, `GenericDelegateType`, `FastDynamicInvoke`, `ClearCaches`, `ApplyEquivalentType` | wontfix —— BCL 已涵蓋之基礎設施 |

**差異：任何路徑皆不會出現 `TargetInvocationException`** —— 無論 emit 或反射備援路徑，被呼叫端例外狀況一律原樣傳遞（ADR-0004）。所有名稱皆區分大小寫。

## 從 Namotion.Reflection 遷移

XML 文件讀取提供於 `Skymly.FastReflection.XmlDocs` 附屬套件，方法拼字相同，命名空間為 `FastReflection.XmlDocs`。

| Namotion.Reflection | Skymly.FastReflection.XmlDocs |
|---|---|
| `GetXmlDocsSummary` / `GetXmlDocsRemarks` | `MemberInfo`/`Type` 上名稱完全相同 |
| `GetXmlDocsTag(name)` / `GetXmlDocsElement()` | 完全相同；元素為 `XElement?` |
| `parameterInfo.GetXmlDocs()` | 完全相同 |
| `XmlDocsOptions` (`PathToXmlFile`, `ResolveExternalXmlDocs`) | 相同的選項型別 |
| `GetXmlDocsPath`, `ToXmlDocsContent`, `GetXmlDocsRecordPropertySummary` | wontfix |
| `XmlDocs.ClearCache` / `CachedType.ClearCache` | 無公開清除方法 —— 依組件區分的快取隨其 `Assembly` 釋放 |
| `Namotion.Reflection.Cecil` XML 文件 | wontfix |
| `ContextualType`/`CachedType`, `ToContextual*`/`GetContextual*`, 屬性拆分, NRT `Nullability` | 1.0 之後的文件工具 |
| `HasProperty` / `TryGetPropertyValue` | wontfix —— `TryGetOrdinal`/`TryGetValue` 已涵蓋此能力 |

## 從 DotNext.Reflection 遷移

| DotNext.Reflection | Skymly.FastReflection |
|---|---|
| `method.Unreflect<TDelegate>()` | `MethodInfo.Unreflect<TDelegate>()` —— 相同精確繫結契約，僅限公開簽章 |
| `Unreflect()` → `DynamicInvoker` | `CreateInvoker()` → `FastInvoker`；建構函式 → `FastConstructorInvoker`；net8+ `CreateSpanInvoker` → `FastSpanInvoker`/`FastSpanConstructorInvoker` |
| `MemberGetter`/`MemberSetter` 委派轉換 | `CreateGetter`/`CreateSetter` —— 開放形式與 `<TDeclaring, TValue>` 具型別形式 |
| `Type<T>.Method`/`.Property`/`.Field`, `MemberCache`, `IMember` 成員物件 | wontfix —— `TypeAccessor.Create(typeof(T))` + `GetMembers()`/`TryGetOrdinal` + `Member.MemberInfo` 涵蓋查詢與快取 |
| `Function`/`Procedure` 引數封包, `Bind` | wontfix —— `FastInvoker` 與繫結 `Invoke` 涵蓋呼叫 |
| `Field<T,TValue>` `ref` 存取 | wontfix —— 不傳回即時參考 |
| `Dynamic.Invoke` / `InvokeAction` | wontfix —— `Delegate.DynamicInvoke` / `CreateInvoker` 組合 |
| `Reflector.MemberOf`, `ExtensionRegistry`, `MethodExtensions.*`, `Devirtualize`, `DelegateType` | wontfix —— 已有 BCL 等價實作 |

### 明確列出的差異

遷移時必須了解的差異彙整如下：

- `Member.Ordinal` 是存取器成員清單中的位置索引 —— fast-member 在該處暴露的是 `[Ordinal]` *屬性值*。
- `CreateConverter` 僅套用有界轉換集合 —— Dynamitey 的 `InvokeConvert` 會查詢使用者自訂轉換，而本程式庫絕不查詢。
- 任何地方皆無 `TargetInvocationException` —— emit 與反射備援路徑皆原樣傳遞被呼叫端例外狀況。
- `ObjectReader.Create` 快速失敗：未知成員名稱在建立時直接拋出 `MissingMemberException`，而非產生靜默的 `object` 資料行。
- 寫入不可寫的 `Member` 會拋出 `InvalidOperationException`，而非 `MissingMemberException`（此為 0.1 缺陷，已於 1.0 修正 —— 參閱 [CHANGELOG](CHANGELOG.md)）。
- 不隨套件提供 `FastMember` 原始碼相容墊片；本節即為相容對照說明。

## 基準測試（BenchmarkDotNet ShortRun，本機實測）

效益在 .NET Framework 上最為顯著，因為 `MethodInfo.Invoke` 在 .NET Framework 尚未具備 .NET 7+ 加入的執行階段層級最佳化：

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

（每次反射呼叫配置 24 B = 所有路徑共用的已裝箱 `int` 傳回值。）

## 專案結構

```
CONTEXT.md                      領域詞彙表
CHANGELOG.md                    發行說明（1.0.0 未發布）
docs/adr/                       架構決策紀錄
src/FastReflection/             核心程式庫（net462;netstandard2.0;net8.0;net10.0）
src/FastReflection.ObjectReader/  附屬套件：IEnumerable → DbDataReader（相同 TFM 集合）
src/FastReflection.XmlDocs/       附屬套件：XML 文件讀取（相同 TFM 集合）
tests/FastReflection.Tests/     xunit（net472 → net462 建置；net6.0 → netstandard2.0；net8.0；net10.0）
tests/FastReflection.AotSmoke/  PublishAot 冒煙應用程式（net8.0;net10.0）
benchmarks/                     BenchmarkDotNet（net472;net8.0;net10.0）
docs/research/                  設計研究筆記
```

## 建置與測試

```bash
dotnet build FastReflection.slnx
dotnet test tests/FastReflection.Tests/FastReflection.Tests.csproj            # 全部目標框架
dotnet test tests/FastReflection.Tests/FastReflection.Tests.csproj -f net8.0  # 單一目標框架
dotnet run -c Release -p benchmarks/FastReflection.Benchmarks -f net8.0 -- --job short
dotnet run -c Release -p benchmarks/FastReflection.Benchmarks -f net472 -- --job short
```

## 發布

CI 由 [Nuke](https://nuke.build) 驅動（`build.cmd <Target>`：`Clean`/`Restore`/`Compile`/`Test`/`Pack`/`Publish`）。

- `ci.yml` 會在每次推送或對 `main` 發起 PR 時執行 `Test`（Windows 上執行全部四個測試框架；Ubuntu 上執行非 Framework 測試以及 linux-x64 AOT 冒煙）。
- `publish.yml` 會在符合 `v*.*.*` 的標籤上執行 `Publish`，並將三個 `Skymly.FastReflection*` 套件推送至 nuget.org。需要名為 `NUGET_API_KEY` 的儲存庫 Secret；同步鎖定的套件版本來自 `Directory.Build.props` 中的 `<Version>` —— 打標籤前請先遞增該版本號。

```bash
git tag v0.1.0 && git push --tags   # 觸發 nuget.org 發布
```
