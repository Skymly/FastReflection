# FastReflection

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | **日本語**

実行時にすでに判明しているプロパティ、フィールド、メソッド、またはコンストラクターを、再利用可能なアクセス（access）へ変換します。動的コード生成が利用可能な場合はコンパイル済みデリゲートがホットパスとなり、利用できない場合でも通常のリフレクションで完了できるアクセスは完了し、完了できないものは失敗します。適合（adaptation）を伴わないバインドは access ではありません。

型アクセサー（type accessor）は、クローズド型における単一可視性のインスタンスプロパティおよびフィールドを列挙します。このライブラリには、動的オブジェクトや DLR ディスパッチ、ユーザー定義変換または暗黙推論変換、メンバーパス構文や名前によるイベント参照、コンテキストメタデータや null 許容性セマンティクス（XML ドキュメントの読み取りは `Skymly.FastReflection.XmlDocs` で提供）、およびコンパイル時生成アクセスは含まれません。用語は [CONTEXT.md](CONTEXT.md) に定義されています。

**ターゲットフレームワーク**: `net462`, `netstandard2.0`, `net8.0`, `net10.0`

## クイックスタート

```csharp
using FastReflection;
using System.Reflection;

// メソッドに対する access。ref/out は配列へ書き戻され、値型はボックス化されます。
MethodInfo add = typeof(Foo).GetMethod("Add")!;
FastInvoker invoker = add.CreateInvoker();
object? result = invoker(target, new object?[] { 2, 3 });

// 非インデクサープロパティに対する access。静的 access は target を持ちません。
Func<object?, object?> get = typeof(Foo).GetProperty("Value")!.CreateGetter();
Action<object?, object?> set = typeof(Foo).GetProperty("Value")!.CreateSetter();
set(target, 42);

// access ではない：厳密なシグネチャ一致、適合なし、フォールバックなし。
var typed = add.Unreflect<Func<Foo, int, int, int>>();
int sum = typed(foo, 2, 3);

// Ref は 1 つの参照渡しパラメーターを代理します。access は Value をインプレースで読み書きします。
delegate bool Parse(Foo target, string text, out Ref<int> value);
var parse = typeof(Foo).GetMethod("TryParse")!.CreateInvoker<Parse>();
parse(foo, "12", out var parsed); // parsed.Value == 12

// 型アクセサー：単一可視性におけるクローズド型の Members。名前は大文字・小文字を区別します。
TypeAccessor typeAccessor = TypeAccessor.Create(typeof(Foo));
typeAccessor[target, "Value"] = 42;
int ord = typeAccessor.TryGetOrdinal("Value", out var i) ? i : -1;
object? v = typeAccessor.GetValue(target, ord); // Ordinal はこの型アクセサー内のインデックスであり、名前ではありません
object? instance = typeAccessor.CreateNew();    // Member ではありません；値型の既定値は access ではありません

// オブジェクトアクセサー：オブジェクトの実行時型に対応する型アクセサーをそのオブジェクトにバインドしたもの。
ObjectAccessor wrapped = ObjectAccessor.Create(target);
wrapped["Value"] = 42;
```

## バインド呼び出し

バインド呼び出し（bound call）は、大文字・小文字を区別する名前と引数シグネチャを一度だけ解決し、そのあと適合済み invoker として access と同じ仕組み（リフレクションフォールバックを含む）で実行します。これは access でも Member でもありません。ドメインとしてはインスタンス Member ドメインにメソッド、インデクサー、静的メンバー、コンストラクターを追加したものです。

```csharp
TypeAccessor accessor = TypeAccessor.Create(typeof(Foo));

accessor.Invoke(target, "Add", 2, 3);                       // 名前によるメソッド呼び出し；オーバーロード解決
accessor.SetValue(target, "Value", 42);                     // 名前によるプロパティ/フィールド設定
object? item = accessor.GetValue(target, "Item", 0);        // インデクサーはその項目名（Item 等）でバインド
object created = accessor.CreateNew(new object?[] { "x" }); // 引数によるコンストラクター呼び出し

// 静的メンバーも同じ方法でバインドされ、target は無視されます。
accessor.Invoke(null, "GetDefault", Array.Empty<object?>());

// 名前付き引数はパラメーター名で並び替え、ジェネリックには型引数を明示的に渡します。
accessor.Invoke(target, "Configure", args, namedArgs, typeArguments: new[] { typeof(int) });

// 各動詞には Try* の対になるメソッドがあります：名前未検出またはバインド失敗時は false を返し、それ以外は握りつぶしません。
accessor.TryInvoke(target, "Add", args, out object? result);

// 重複なしのバインド可能名一覧：メソッド、プロパティ、フィールド、およびインデクサー項目名。
IReadOnlyList<string> names = accessor.GetMemberNames();

// 有界コンバーター — 名前検索ではありません。失敗時は InvalidCastException。
Func<object?, object?> convert = ReflectionExtensions.CreateConverter(typeof(int), typeof(long));
```

- 引数変換は許可された集合のみに限定されます：恒等変換、参照代入、ボックス化解除、`Nullable<T>`、列挙型↔基底型、および C# 仕様のプリミティブ拡大変換（`→decimal` を含み、`byte→char` を除く）；`null` は null 許容型ターゲットにのみ適用されます。ユーザー定義変換、ジェネリック型推論、定数縮小変換、拡張メソッド探索は一切適用されません。
- `params` は通常形式・展開形式の双方でバインドされます；省略可能パラメーターは補完されます；補完なしで一致する候補は補完が必要な候補より優先されます。名前付き引数の一致は大文字・小文字を区別します。
- 契約上の失敗順序（[ADR-0004](docs/adr/0004-one-exception-contract-for-emit-and-reflection-completion.md)）：`name` が null → `ArgumentNullException`；名前が存在しない → `MissingMemberException`；該当候補なし → `MissingMethodException`；最適候補の同点 → `AmbiguousMatchException`；`args` が null → `ArgumentNullException`；target 規則違反 → `ArgumentNullException`/`InvalidCastException`；各引数変換失敗 → `InvalidCastException`；呼び出し先の例外はラップされずそのまま伝播します。`Try*` が `false` を返すのはルックアップ段階のみです。
- バインド済み invoker はメンバー種別・名前・正規化シグネチャをキーとして型アクセサー上にキャッシュされます；公開フラッシュ API はなく、増加量は呼び出し箇所の種類によってのみ制限されます。

## Members と並び順

`GetMembers()` は 1 つの型アクセサーにおける `IReadOnlyList<Member>` — インスタンスプロパティとフィールドのみ — を返し、メソッド、インデクサー、定数、静的メンバーは決して含みません。

```csharp
foreach (Member m in TypeAccessor.Create(typeof(Foo)).GetMembers())
{
    string name = m.Name;          // 大文字・小文字を区別
    Type type = m.Type;
    int ordinal = m.Ordinal;       // このアクセサーのリスト内での位置インデックス
    bool rw = m.CanRead && m.CanWrite;
    var attr = m.GetAttribute<DisplayAttribute>();
}
```

`OrdinalAttribute`（`property`/`field` に適用し、`int` 値を 1 つ指定）はリストの並び順を制御します：属性付きのメンバーが値の昇順で先頭に並び、属性のないメンバーがそれに続き、値が等しい場合は列挙順が維持されます。`Member.Ordinal` は結果リストにおける位置インデックスのままであり、この属性は並び替えの入力であって可視のインデックスではありません。

## 主な機能

- リフレクションオブジェクトを保持していれば、非公開のプロパティ、フィールド、メソッド、コンストラクターに対しても access を構築できます。同じクローズド型に対する公開用と非公開用の型アクセサーは別個に扱われます。
- `object[]` または `Span<object?>` を用いる access は、`ref`/`out` をバッファーへ書き戻し、値型の引数と戻り値をボックス化します。`Ref<T>` はそのバッファースロットではありません。
- setter がなくても `ref` を返すプロパティは、それが `ref readonly` でない限り書き込み用 access を持てます。ただしその書き込みによってプロパティが書き込み可能な Member になるわけではありません。
- ボックス化された値型への書き込みはそのボックス自体を更新します。[ADR 0001](docs/adr/0001-boxed-value-writes-update-the-box.md) を参照してください。
- 静的 access には target が不要です。インスタンス access には null ではなく互換性のある target（実行時型が宣言型・派生型・実装型のいずれか、またはその値型のボックス）が必要です。
- インデクサープロパティは Member ではなく、`CreateGetter` / `CreateSetter` の対象にもなりません。`CreateInvoker` 経由で `get_Item` / `set_Item` を呼び出してください。
- オープンジェネリック型には型アクセサーがありません。オープンジェネリックメソッドは access で扱う前に型構築する必要があります。
- `CreateInvoker<TDelegate>()` は `CreateDelegate` を用いて公開の厳密なシグネチャにバインドします；そのバインドは access ではありません。`Ref<T>` や `CreateDelegate` でバインドできない非公開メソッドなどを含む適合バインドは access であり、動的コードが利用できない場合は例外をスローします。
- `net8.0+` の `CreateSpanInvoker` は呼び出し側の `Span<object?>` バッファー上で動作する access です（参照型の span は `stackalloc` できません）。ボックス化は引き続き発生します。動的コードが利用できない場合は BCL の `MethodInvoker` / `ConstructorInvoker` にフォールバックします。
- `CanWrite` はその型アクセサーがその Member に書き込めること（コンストラクター実行後の init-only プロパティを含む）を意味します。C# から代入可能であることや `required` の強制を意味するわけではありません。
- 1 つの名前は最大 1 つの Member のみを識別します。どの宣言が優先されるかは保証されません。Ordinal は保持している型アクセサーの外では識別子になりません。

## 明示的な対象外（Non-goals）

- `IDynamicMetaObjectProvider` / `dynamic` / DLR ディスパッチ — [dynamitey](https://github.com/ekonbenefits/dynamitey) の領域です
- ユーザー定義変換または推論変換 — 有界変換集合のみ対応
- 名前によるメンバーパスとイベント — 1.0 以降
- NRT / コンテキストメタデータ — 1.0 以降のドキュメントツール向け；XML ドキュメント読み取りは `Skymly.FastReflection.XmlDocs` で提供
- コンパイル時ソース生成アクセス — 1.0 以降のコンパニオンジェネレーター

## AOT とトリミング

パッケージは `IsAotCompatible` を有効にしてビルドされているため、トリム・AOT・単一ファイルに関する検出項目（IL2xxx/IL3xxx）は利用側のパブリッシュ時ではなくライブラリのビルド時にエラーになります。契約（[policy #9](https://github.com/Skymly/FastReflection/issues/9)）：

- `CreateInvoker<TDelegate>` は `RequiresDynamicCode` を持つ唯一の公開メンバーです — 適合バインドに動的コードが必要なためです。公開シグネチャ向けの AOT セーフな厳密バインドには `Unreflect<TDelegate>` を使用してください。
- `TypeAccessor.Create`、`ObjectAccessor.Create`、`CreateFactory(Type)`、および `TDelegate` パラメーターには `DynamicallyAccessedMembers` の和集合が付与されており、トリミング後もアクセサードメインが探索可能なメンバーを保持します。
- 動的コードが利用できない場合、通常のリフレクションで完了できる access は引き続き完了します（span invoker は BCL の `MethodInvoker`/`ConstructorInvoker` にフォールバックします）；適合型 `Ref<T>` バインドは `NotSupportedException` をスローします。
- CI では linux-x64 上の `PublishAot` スモークアプリでこの契約を E2E 検証しています。サテライトパッケージも同様に注釈されています。

静的アクセサーキャッシュは `Type` を強参照でキーに保持するため、収集可能な `AssemblyLoadContext` をピン留めします — コアライブラリはアンロードを保証しません（[キャッシュ調査](https://github.com/Skymly/FastReflection/blob/827160a58d0648e69532b56ac408679f0b4c2c24/docs/research/2026-10-24-cache-alc-unload.md)でプローブ検証済み）。サテライト側のキャッシュは `ConditionalWeakTable` を用いてアセンブリと共に解放されます。

## fast-member からの移行

コアは fast-member で使われる API サーフェスを 1 対 1 でカバーします；`ObjectReader` は `Skymly.FastReflection.ObjectReader` パッケージへ分離されました。

| FastMember | Skymly.FastReflection |
|---|---|
| `TypeAccessor.Create(type)` | `TypeAccessor.Create(type)` — `allowNonPublicAccessors` を含め同一 |
| `accessor[target, "Name"]` get/set | 同じインデクサー — 名前未検出時は `MissingMemberException`；書き込み不可の Member への書き込みは `InvalidOperationException`（**差異**） |
| `ObjectAccessor.Create(obj)`, `wrapped["Name"]` | 同じ — `Equals` は対称：アクセサーは生の target と決して等しくなりません |
| `CreateNew()`, `CreateNewSupported` | 同じ；加えてバインド構築用の `CreateNew(args, namedArgs)` / `TryCreateNew` |
| `GetMembers()` → `MemberSet` | `GetMembers()` → `IReadOnlyList<Member>`；`MemberSet`/`GetMembersSupported` はなし — LINQ でフィルタしてください |
| `Member.Name`/`.Type`/`.Ordinal`/`.CanRead`/`.CanWrite`/`.IsDefined`/`.GetAttribute` | 同名 |
| `[Ordinal(n)]` | `FastReflection.OrdinalAttribute` — **差異**：`Member.Ordinal` は属性値ではなくソート済みリスト内の*位置*です；属性は並び替えのみに使われます |
| `ObjectReader.Create(source, members)` | `Skymly.FastReflection.ObjectReader` パッケージ — **差異**：不明なメンバー名は生成時に `MissingMemberException` で失敗します（サイレントに `object` 列を作りません） |
| アクセサーのサブクラス化 | wontfix — `Create` が唯一の取得経路です |

`FastMember` ソース互換シムは提供されません（wontfix）— この対応表に沿って呼び出し箇所を移植してください。

## Dynamitey からの移行

本番利用されるすべての動詞は `TypeAccessor`/`ObjectAccessor` のバインド呼び出しに対応します — 同じ名前ルックアップドメインであり、DLR や `Microsoft.CSharp` への依存はありません。

| Dynamitey (`Dynamic.*`) | Skymly.FastReflection |
|---|---|
| `InvokeGet` / `InvokeSet` | 名前指定の `GetValue` / `SetValue`（＋ `TryGetValue` / `TrySetValue`） |
| `InvokeMember` / `InvokeMemberAction` | `Invoke` / `TryInvoke`（action の場合はボックス化された戻り値を無視） |
| `InvokeConstructor` | 型アクセサー上の `CreateNew(args, namedArgs)` / `TryCreateNew` |
| `InvokeGetIndex` / `InvokeSetIndex` | `params object?[]? index` を取る `GetValue` / `SetValue` — インデクサーはその項目名でバインド |
| `InvokeContext` / `StaticContext`（静的） | `TypeAccessor` 上のバインド呼び出し；静的メンバーは `target` を無視 |
| 名前付き引数 / ジェネリックメソッド | `Invoke(…, namedArgs, typeArguments)` — 名前付き引数は並び替え、型引数は明示指定（推論なし） |
| `InvokeConvert` | `ReflectionExtensions.CreateConverter(sourceType, targetType)` — **差異**：有界変換のみ；ユーザー定義の `op_Implicit`/`op_Explicit` は一切参照されません |
| `GetMemberNames` | いずれかのアクセサー上の `GetMemberNames()` |
| `CacheableInvocation` / `CreateCallSite` | 自動 — バインド済み invoker はアクセサーキャッシュにキャッシュされます |
| 保持された callable への `Dynamic.Invoke` / `InvokeAction` | wontfix — `delegate.DynamicInvoke(…)` またはその `Method` に対する `CreateInvoker` を組み合わせてください |
| `InvokeSetAll`、メンバーチェーン、名前指定イベント、演算子動詞、COM、`IDMOP` ターゲット | wontfix / 1.0 以降 — [1.0 計画](docs/plans/1.0.md) を参照 |
| `Curry` / `Tupler` / `Builder` / `DynamicObjects` ファミリー / `Linq` / `ConvertAll` | wontfix |
| `IsDBNull`, `GenericDelegateType`, `FastDynamicInvoke`, `ClearCaches`, `ApplyEquivalentType` | wontfix — BCL でカバーされる配管処理 |

**差異：どの経路でも `TargetInvocationException` は発生しません** — emit 経路とリフレクションフォールバック経路の双方で、呼び出し先の例外がラップされずにそのまま伝播します（ADR-0004）。名前は全体を通して大文字・小文字を区別します。

## Namotion.Reflection からの移行

XML ドキュメントの読み取りはサテライトパッケージ `Skymly.FastReflection.XmlDocs` で提供され、同じメソッド名・名前空間 `FastReflection.XmlDocs` を持ちます。

| Namotion.Reflection | Skymly.FastReflection.XmlDocs |
|---|---|
| `GetXmlDocsSummary` / `GetXmlDocsRemarks` | `MemberInfo`/`Type` 上で完全に同じ名前 |
| `GetXmlDocsTag(name)` / `GetXmlDocsElement()` | 同一；要素型は `XElement?` |
| `parameterInfo.GetXmlDocs()` | 同一 |
| `XmlDocsOptions` (`PathToXmlFile`, `ResolveExternalXmlDocs`) | 同じオプション型 |
| `GetXmlDocsPath`, `ToXmlDocsContent`, `GetXmlDocsRecordPropertySummary` | wontfix |
| `XmlDocs.ClearCache` / `CachedType.ClearCache` | 公開フラッシュ API なし — アセンブリ単位キャッシュはその `Assembly` と共に解放 |
| `Namotion.Reflection.Cecil` XML ドキュメント | wontfix |
| `ContextualType`/`CachedType`, `ToContextual*`/`GetContextual*`, 属性分割, NRT `Nullability` | 1.0 以降のドキュメントツール |
| `HasProperty` / `TryGetPropertyValue` | wontfix — `TryGetOrdinal`/`TryGetValue` がその機能をカバー |

## DotNext.Reflection からの移行

| DotNext.Reflection | Skymly.FastReflection |
|---|---|
| `method.Unreflect<TDelegate>()` | `MethodInfo.Unreflect<TDelegate>()` — 同じ厳密バインド契約、公開シグネチャのみ |
| `Unreflect()` → `DynamicInvoker` | `CreateInvoker()` → `FastInvoker`；コンストラクター → `FastConstructorInvoker`；net8+ `CreateSpanInvoker` → `FastSpanInvoker`/`FastSpanConstructorInvoker` |
| `MemberGetter`/`MemberSetter` デリゲート変換 | `CreateGetter`/`CreateSetter` — オープン形式および `<TDeclaring, TValue>` 型付き形式 |
| `Type<T>.Method`/`.Property`/`.Field`, `MemberCache`, `IMember` メンバーオブジェクト | wontfix — `TypeAccessor.Create(typeof(T))` + `GetMembers()`/`TryGetOrdinal` + `Member.MemberInfo` で探索とキャッシュをカバー |
| `Function`/`Procedure` 引数パック, `Bind` | wontfix — `FastInvoker` とバインド `Invoke` で呼び出しをカバー |
| `Field<T,TValue>` の `ref` アクセス | wontfix — ライブ参照は返しません |
| `Dynamic.Invoke` / `InvokeAction` | wontfix — `Delegate.DynamicInvoke` / `CreateInvoker` の組み合わせ |
| `Reflector.MemberOf`, `ExtensionRegistry`, `MethodExtensions.*`, `Devirtualize`, `DelegateType` | wontfix — BCL に等価機能あり |

### 明記された差異一覧

移行時に把握しておくべき差異を 1 か所にまとめています：

- `Member.Ordinal` はアクセサーのメンバーリスト内の位置インデックスです — fast-member では `[Ordinal]` の*属性値*を返します。
- `CreateConverter` は有界変換集合のみを適用します — Dynamitey の `InvokeConvert` はユーザー定義変換を参照しますが、本ライブラリは参照しません。
- どこでも `TargetInvocationException` は発生しません — emit 経路・リフレクションフォールバック経路のいずれでも呼び出し先の例外がそのまま伝播します。
- `ObjectReader.Create` は早期失敗します：不明なメンバー名は生成時に `MissingMemberException` を投げ、サイレントに `object` 列を作ることはありません。
- 書き込み不可の `Member` への書き込みは `MissingMemberException` ではなく `InvalidOperationException` を投げます（0.1 の不具合を 1.0 向けに修正 — [CHANGELOG](CHANGELOG.md) を参照）。
- `FastMember` のソース互換シムは提供されません；このセクション自体がそのシムに相当します。

## ベンチマーク（BenchmarkDotNet ShortRun、ローカルマシン測定）

.NET Framework では `MethodInfo.Invoke` に .NET 7 以降で追加されたランタイムレベルの高速化が入っていないため、効果が最も大きくなります。

| メソッド | .NET Framework 4.8.1 | net8.0 |
|---|---:|---:|
| DirectCall | ~0.6 ns | ~0 ns |
| PropertyInfo.GetValue | 109.84 ns | 9.54 ns |
| **CreateGetter** | **8.19 ns (13× faster)** | **5.37 ns** |
| Type accessor by name | 35.95 ns | 13.56 ns |
| Type accessor by ordinal | 7.01 ns | 6.03 ns |
| MethodInfo.Invoke | 216.34 ns | 18.58 ns |
| BCL MethodInvoker | n/a | 11.36 ns |
| **CreateInvoker** | **6.39 ns (34× faster)** | **5.59 ns** |

（リフレクション呼び出し 1 回あたり 24 B の割り当て ＝ すべての経路で共通のボックス化された `int` 戻り値。）

## 構成

```
CONTEXT.md                      ドメイン用語集
CHANGELOG.md                    リリースノート（1.0.0 未リリース）
docs/adr/                       アーキテクチャ決定記録
src/FastReflection/             コアライブラリ (net462;netstandard2.0;net8.0;net10.0)
src/FastReflection.ObjectReader/  サテライト: IEnumerable → DbDataReader (同一 TFM セット)
src/FastReflection.XmlDocs/       サテライト: XML ドキュメント読み取り (同一 TFM セット)
tests/FastReflection.Tests/     xunit (net472 → net462 ビルド; net6.0 → netstandard2.0; net8.0; net10.0)
tests/FastReflection.AotSmoke/  PublishAot スモークアプリ (net8.0;net10.0)
benchmarks/                     BenchmarkDotNet (net472;net8.0;net10.0)
docs/research/                  設計調査ノート
```

## ビルドとテスト

```bash
dotnet build FastReflection.slnx
dotnet test tests/FastReflection.Tests/FastReflection.Tests.csproj            # 全ターゲット
dotnet test tests/FastReflection.Tests/FastReflection.Tests.csproj -f net8.0  # 単一ターゲット
dotnet run -c Release -p benchmarks/FastReflection.Benchmarks -f net8.0 -- --job short
dotnet run -c Release -p benchmarks/FastReflection.Benchmarks -f net472 -- --job short
```

## リリース

CI は [Nuke](https://nuke.build) で駆動されます（`build.cmd <Target>`: `Clean`/`Restore`/`Compile`/`Test`/`Pack`/`Publish`）。

- `ci.yml` は `main` へのすべてのプッシュ／PR で `Test` を実行します（Windows では全 4 テストターゲット、Ubuntu では非 Framework ターゲットに加えて linux-x64 の AOT スモークを実行）。
- `publish.yml` は `v*.*.*` に一致するタグで `Publish` を実行し、3 つの `Skymly.FastReflection*` パッケージを nuget.org へプッシュします。`NUGET_API_KEY` というリポジトリシークレットが必要です；一括バージョンは `Directory.Build.props` の `<Version>` に由来するため、タグ付け前に更新してください。

```bash
git tag v0.1.0 && git push --tags   # nuget.org への公開をトリガー
```
