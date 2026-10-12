# FastReflection 立项调研报告

> 日期：2026-10-12
> 目的：调研 4 个代表性 .NET 反射增强库（DotNext.Reflection、dynamitey、Namotion.Reflection、fast-member）+ 平台现状，为新库 **FastReflection** 的功能定位与 API 设计提供依据。
> 方法：4 路并行子代理对一手资料（GitHub 源码、官方文档、NuGet、issue/PR、作者博客）调研 + 主代理对平台演进的补充检索。所有关键事实附来源。

---

## 0. 总览对比

| | DotNext.Reflection | dynamitey | Namotion.Reflection | fast-member |
|---|---|---|---|---|
| 仓库 | [dotnet/dotNext](https://github.com/dotnet/dotNext) | [ekonbenefits/dynamitey](https://github.com/ekonbenefits/dynamitey) | [RicoSuter/Namotion.Reflection](https://github.com/RicoSuter/Namotion.Reflection) | [mgravell/fast-member](https://github.com/mgravell/fast-member) |
| 一句话定位 | 四层 API 的快速调用库（强类型委托→统一 DynamicInvoker） | DLR CallSite 缓存的动态调用瑞士军刀 | **语义增强**反射（NRT/XML docs/鸭子类型检查）+ 缓存 | 纯粹的"字符串成员名→高速读写"+ IDataReader 适配 |
| 加速机制 | `CreateDelegate` + 动态编译（DynamicMethod 推断）+ InlineIL | C# RuntimeBinder CallSite 三级缓存（DLR） | 懒初始化 `IPropertyReader/Writer` 委托（仅属性/字段读写） | 手写 IL emit：`TypeBuilder` 实体子类 / `DynamicMethod` / DLR 三档降级 |
| 最新版 | 4.x（.NET 6），**已弃用** | 3.0.3（2023-11），社区 fork `Dynamitey.Community` 接手 | 3.5.1（2026-04），活跃 | 1.5.0（2019-05），**实质停滞** |
| 许可证 | MIT | Apache-2.0 | MIT | Apache-2.0 |
| TFM | netstandard2.1/net6 | netstandard2.0 + net40 | netstandard2.0 + net462 + net8.0 | netstandard2.0 + net461 + netcoreapp2.0（零依赖，~26KB） |
| AOT/trim 友好 | 否（`RequiresUnreferencedCode` 标记动态路径） | 否（重度依赖 RuntimeBinder） | 否（`dynamic` + 文件系统 XML docs） | 否（README 明示；Xamarin.iOS 有实际事故） |
| 下载量/影响 | —（已并入核心包瘦身） | ~34.5M | ~3.25 亿（NJsonSchema/NSwag 依赖） | ~65M+（EFCore.BulkExtensions 等 238 个包依赖） |
| 弃用/停滞原因 | .NET 7 `MethodInfo.Invoke` 大幅提速 + .NET 8 `UnsafeAccessor`（[作者声明](https://github.com/dotnet/dotNext/discussions/142)） | 作者精力转移，社区接手 | —（仍活跃） | 作者 2021 PR 未合并，无新 release |

**四者恰好覆盖"反射增强"的四条正交路线**：调用加速的分层 API（DotNext）、动态语义调用（dynamitey）、元数据语义增强（Namotion）、成员读写加速（fast-member）。FastReflection 的差异化空间在它们的交集之外：现代化（.NET 8+ 原语、AOT 通道）、更广成员覆盖、以及低分配 API。

---

## 1. DotNext.Reflection

### 1.1 基本面与弃用始末

- 独立包 `DotNext.Reflection` 已弃用，4.x（.NET 6）为最后版本；5.x+ 核心包仅保留 `TypeExtensions`/`MethodExtensions`/`TaskType`。维护者 sakno 在 [Discussion #142](https://github.com/dotnet/dotNext/discussions/142) 明确说明弃用原因：**.NET 7 大幅改进 `MethodInfo.Invoke`、`MethodInfo.CreateDelegate` 成为可行内置替代、.NET 8 `UnsafeAccessorAttribute` 提供编译时私有访问官方路径**。
- 来源：[concept.html](https://dotnet.github.io/dotNext/features/concept.html)、[CHANGELOG](https://github.com/dotnet/dotNext/blob/master/CHANGELOG.md)

### 1.2 核心设计：四层 API 分层

```
Layer 4  Type<T>.Method.Get<D> / ExtensionRegistry   ← 成员查找与注册
Layer 3  DynamicInvoker (非泛型 Unreflect)           ← 统一动态入口 ≈1.5–2×
Layer 2  Function<A,R>/Procedure<A> + Ref<T>         ← ref/out/不可见类型 ≈1.4×
Layer 1  Reflector.Unreflect<D>(MethodInfo)          ← 强类型委托 ≈1.0×
```

- `Method<D>`/`Constructor<D>` 继承 `MemberInfo` 并实现 `IMember<M,D>`，暴露 `Invoker` 返回委托 `D`（[API 文档](https://sakno.github.io/dotNext/versions/1.x/api/DotNext.Reflection.Method-1.html)）。
- v3.0 把 `DynamicInvoker` 签名从 `object[](object, object[])` 改为 `object DynamicInvoker(object target, Span<object> args)`——**`Span<object>` 参数容器**，调用方可复用缓冲区/`stackalloc`（值类型仍需 box）。来源：[CHANGELOG](https://github.com/dotnet/dotNext/blob/master/CHANGELOG.md)
- `Ref<T>` 包装把 `ref`/`out`/`in` 参数塞进 `ValueTuple` 单参委托：`Function<(string, Ref<decimal>), bool>` 对应 `bool TryParse(string, out decimal)`。来源：[TryParseReflectionBenchmark.cs](https://github.com/sakno/DotNext/blob/master/src/DotNext.Benchmarks/Reflection/TryParseReflectionBenchmark.cs)
- `Type<T>` 泛型静态查找 + `Get`/`Require` 语义分离（找不到返回 null vs 抛异常）。

### 1.3 实现技术（已证实部分）

- `TaskType.cs` 确认：用 **InlineIL**（Fody weaver，编译时 `Ldtoken`/`Pop` 提取 `RuntimeMethodHandle`）+ `MethodInfo.CreateDelegate<T>()` 缓存强类型委托，零运行时动态代码。来源：[TaskType.cs](https://github.com/dotnet/dotNext/blob/08d2384dc6ff7547317f5e4be1fa5e6e5c81759f/src/DotNext/Reflection/TaskType.cs)
- `DelegateHelpers.CreateOpenDelegate` 明确用 Expression Trees 且**不用动态编译**——与 `DynamicInvoker` 的「动态编译」是两条路径。[delegate.md](https://github.com/sakno/dotNext/blob/gh-pages/docs/features/reflection/delegate.md)
- `DynamicInvoker` 后端**未能从一手资料确认**，最可能是 `DynamicMethod + ILGenerator`（与 .NET 7 内置 `MethodInvoker` 同一路径）。`Devirtualize` 方法上有 `[RequiresUnreferencedCode]`，证实存在运行时代码生成路径。[TypeExtensions.cs](https://github.com/dotnet/dotNext/blob/f0f453aeb95d7c062e32e97a96db3aa5a59a498a/src/DotNext/Reflection/TypeExtensions.cs)

### 1.4 官方性能主张（非独立复现）

精确委托 ≈1.0×；特殊委托 ≈1.4×；object 参数委托 ≈2–3×；`DynamicInvoker` ≈1.5–2×。来源：[fast.html](https://dotnet.github.io/dotNext/features/reflection/fast.html)

### 1.5 借鉴点

- **按"调用方已知多少签名信息"分层**是本库最大价值：Layer1 `CreateDelegate`、Layer2 `Ref<T>`+`Function<>`、Layer3 统一 invoker。
- `Span<object>` 参数、`Get`/`Require` 语义分离、`IMember<M,D>` 统一接口、`ExtensionRegistry` 可注册扩展方法。
- 教训：动态路径要显式 `[RequiresUnreferencedCode]`；AOT 兼容层与动态层应可切换。

---

## 2. dynamitey

### 2.1 基本面

- 定位"DLR 元编程瑞士军刀"：DLR CallSite 缓存做快速动态成员访问，外加 curry/Tuple/动态 LINQ/预制 DynamicObject 基类。Apache-2.0，netstandard2.0+net40，依赖 `Microsoft.CSharp`。最后版本 3.0.3（2023-11）；社区 fork [dynamitey-community/dynamitey](https://fsprojects.github.io/FSharp.Interop.Dynamic/docs/dynamitey.html) 接手。
- 从 ImpromptuInterface 拆出的底层动态调用层；对 ImpromptuInterface 有**软依赖**（超长参数列表的委托类型 emit、ActLike 接口代理兜底转换）。

### 2.2 API 与加速机制

- 入口静态类 `Dynamitey.Dynamic`：`InvokeMember/InvokeMemberAction`（void 方法必须走不同 binder flag，故拆分）、`InvokeGet/InvokeSet`、`InvokeGetIndex/InvokeSetIndex`、`InvokeGetChain("a.b[0].c")`、`InvokeConstructor`、`InvokeConvert/CoerceConvert`（委托适配→ActLike→显式转换→Enum.Parse 多级兜底）、`InvokeBinaryOperator`、`Curry`、`CreateCallSite<T>` 暴露缓存的 CallSite 创建。
- 元数据靠 `params object[]` 里的包装类型编码：`InvokeContext`/`StaticContext`（静态成员）、`InvokeArg`（命名实参）、`InvokeMemberName`（成员名+泛型参数）、`String_OR_InvokeMemberName`（穷人 union）。
- 核心机制（[InvokeHelper-Regular.cs](https://github.com/ekonbenefits/dynamitey/blob/master/Dynamitey/Internal/Optimization/InvokeHelper-Regular.cs)）：
  1. 复刻 C# `dynamic` 编译输出：`Binder.GetMember/InvokeMember...` → `CallSite<T>.Create` → `callSite.Target(...)`，吃 DLR L0/L1/L2 三级缓存；
  2. **全局跨调用点缓存**：`BinderHash` key（成员名+context+argNames+binder类型+flags）按 6 个专用字典分桶（get/set/member/direct/ctor），`LazyBinder` 延迟构造；
  3. **用 DLR 调 DLR**：`CallSite<T>.Create` 是泛型方法，用缓存的 CallSite 来调它，自举绕过 `MakeGenericMethod`+反射；
  4. T4 预生成各元数委托签名，超上限走 ImpromptuInterface 的 `EmitCallSiteFuncType`。
- 微优化：`BareBonesList<T>`、刻意不用 LINQ、一趟扫描拆 `InvokeArg`。
- 补丁：C# binder 不支持**静态属性** → 改写 `get_X/set_X` + `IsStaticType` flag；COM 对象换 `ComBinder`。

### 2.3 性能与局限

- 性能表述都是 **amortized**：首调要跑 C# 语义分析+表达式编译，极重；命中缓存后接近直调（第三方实测 dynamic ≈ 反射的 12–33× 快，比直调慢 ~20×）。来源：[Marc Gravell 基准](https://blog.marcgravell.com/2012/01/playing-with-your-member.html)、[SO](https://stackoverflow.com/questions/13714295/)
- 局限：`BinderCache` 用普通 `Dictionary`，[issue #50](https://github.com/ekonbenefits/dynamitey/issues) 作者自报并发损坏；每次调用分配 `object[]`+装箱；返回 `dynamic` 传染复杂度；AOT/裁剪不友好；委托元数上限依赖外部 emit。

### 2.4 借鉴点

- **双层缓存模型**：全局 key→CallSite 字典 + CallSite 内部按运行时类型的多态内联缓存。纯表达式树方案的等价物：`key → 编译好的 open-instance 委托` + 可选 per-`RuntimeTypeHandle` 二级缓存。
- `CreateCallSite`/`CacheableInvocation` 让调用方持有句柄实现零查找热路径 → FastReflection 应暴露"编译一次、随处调用"的强类型句柄。
- 避坑：并发缓存用 `ConcurrentDictionary`；提供不分配 `object[]` 的重载；静态成员/索引器/事件应有显式 API 而非 hack；若不需要 `IDynamicMetaObjectProvider` 语义，表达式树/`DynamicMethod` 是更短路径。

---

## 3. Namotion.Reflection

### 3.1 基本面

- 定位**语义增强反射**："advanced reflection APIs like XML documentation reading, Nullable Reference Types reflection and string based type checks"，NJsonSchema/NSwag 的主要依赖。MIT，netstandard2.0+net462+net8.0，累计 ~3.25 亿下载，活跃维护（3.5.1 于 2026-04 发布）。
- [CLAUDE.md](https://github.com/RicoSuter/Namotion.Reflection/blob/master/CLAUDE.md) 是仓库内权威结构文档。

### 3.2 核心能力

1. **Cached/Contextual 类型树**：
   ```
   CachedType（无上下文缓存包装，Nullable<T> 解包）
   └── ContextualType（带"出现在哪"的上下文 + nullability）
       └── ContextualMemberInfo → ContextualPropertyInfo / ContextualFieldInfo / ...
   ```
   同一 `string` 在属性 A 可空、在参数 B 不可空——**"类型本身"与"类型出现位置"分离建模**是 NRT 推断能成立的关键。`Type` 给解包后类型、`OriginalType` 留原始、`IsNullableType` 标记。
   来源：[ContextualTypeExtensions.cs](https://github.com/RicoSuter/Namotion.Reflection/blob/master/src/Namotion.Reflection/Context/ContextualTypeExtensions.cs)
2. **NRT 推断**（招牌）：解析编译器写入元数据的 `NullableAttribute`（`byte[] flags`，0=oblivious/1=非空/2=可空）与 `NullableContextAttribute`；构造类型树时用 `ref int nullableFlagsIndex` **游标单遍顺序消费 flags**，零回溯。规范：[roslyn nullable-metadata.md](https://github.com/dotnet/roslyn/blob/main/docs/features/nullable-metadata.md)；作者博客：[rsuter.com](https://blog.rsuter.com/the-output-of-nullable-reference-types-and-how-to-reflect-it/)。运行时校验：`HasValidNullability/EnsureValidNullability/ValidateNullability`。
3. **XML documentation 运行时读取**：`GetXmlDocsSummary/GetXmlDocsTag/GetXmlDocsElement/ToXmlDocsContent`（转文本/Markdown/HTML）；`ConcurrentDictionary<路径, CachingXDocument>` 两级缓存；`XmlDocsOptions`（外部 NuGet/SDK 路径解析、FormattingMode）。成员→元素名生成 `T:/M:/P:/F:/E:` XML doc ID；对泛型 DeclaringType 用 `Module.ResolveMember` 回泛型定义。来源：[XmlDocsExtensions.cs](https://github.com/RicoSuter/Namotion.Reflection/blob/master/src/Namotion.Reflection/XmlDocsExtensions.cs)
4. **字符串鸭子类型检查**：`IsAssignableToTypeName(typeName, TypeNameStyle)`、`GetEnumerableItemType`、`GetDisplayName`（`DictionaryOfStringAndInt32`）——跨程序集判断不用引用类型。
5. **Attribute 三分法**：`ContextAttributes`/`TypeAttributes`/`InheritedAttributes` + `GetContextOrTypeAttribute<T>`。

### 3.3 性能观与实现

- **不是快反射库**：性能优化是"降分配补丁"（lahma 的 PR 系列：减少 LINQ/char[] 分配），调用路径优化只覆盖属性/字段读写。
- `ContextualPropertyInfo.GetValue/SetValue`：`lock(this)` 双重检查 + `AggressiveInlining`，懒创建 `IPropertyReader/Writer`（`Create(declaringType, propertyType, PropertyInfo)` 签名指向闭泛型 `PropertyReader<TDecl,TVal>` + 强类型委托 + 非泛型接口抹平泛型）。来源：[ContextualPropertyInfo.cs](https://github.com/RicoSuter/Namotion.Reflection/blob/master/src/Namotion.Reflection/Context/ContextualPropertyInfo.cs)
- 缓存：全局 `ConcurrentDictionary<record struct CacheKey, object>`，**字符串 key（FullName 拼接）**——[issue #142](https://github.com/RicoSuter/Namotion.Reflection/issues/142) 已报缓存错值 bug。教训：key 应直接用 `Type`/`MemberInfo` 引用或 `ConditionalWeakTable`。
- 反面教材：大量 `dynamic`（Cecil 鸭子类型探测、`GetMemberElementName(dynamic)`）→ 必须带 `Microsoft.CSharp` 依赖、`PublishTrimmed` 下 XML docs 静默失效（[issue #26](https://github.com/RicoSuter/Namotion.Reflection/issues/26)）。

### 3.4 借鉴点

- 双层包装模型（Cached/Contextual）、节点图惰性物化（`GenericArguments`/`ElementType`/成员数组全返回同类节点，可任意深度遍历）、NRT flags 游标算法可直接照抄。
- 属性读写的"闭泛型委托+非泛型接口"模式值得保留并扩展到方法/构造调用。
- 全局静态缓存 + `ClearCache()` 逃生门可用，但应提供可注入 provider。
- 测试组织：专门的 `Tests.FullAssembly` 保证 NRT/XML docs 元数据真实存在。

---

## 4. fast-member

### 4.1 基本面

- Marc Gravell（Dapper/protobuf-net 作者）2012 年作品，只解决一个问题：**"类型+成员名仅运行期已知"的快速成员读写**，外加 `ObjectReader`（DbDataReader 适配）。Apache-2.0，零依赖 ~26KB，最新版 1.5.0（2019-05）后实质停滞。源码仅 6 文件：`TypeAccessor`/`ObjectAccessor`/`ObjectReader`/`MemberSet`/`OrdinalAttribute`/`CallSiteCache`/`TypeHelpers`。

### 4.2 API

- `TypeAccessor.Create(type, allowNonPublicAccessors)` → `accessor[obj, "name"]`：类型级访问器，按 Type 全局缓存（`Hashtable` 读免锁 + 双查锁）。额外能力：`CreateNew()`（有 public 无参构造时 emit `newobj`）、`GetMembers()`。
- `ObjectAccessor.Create(obj)` → `wrapper["name"]`：实例级包装；`IDynamicMetaObjectProvider` 走 DLR `CallSiteCache`，否则委托给 `TypeAccessor`。
- `ObjectReader : DbDataReader`：把 `IEnumerable` 适配成 IDataReader，喂 `SqlBulkCopy.WriteToServer`/`DataTable.Load`；`effectiveTypes`（Nullable 解包）/`allowNull`（BitArray）构造时算好，`GetValue` 统一 `?? DBNull.Value`；`[Ordinal]` 控制列序。
- 覆盖范围：**仅 public instance 的 property+field**；`allowNonPublicAccessors` 只放开 public 属性的非公开 get/set 访问器；无 static/方法/事件/索引器。

### 4.3 底层技术：三档生成策略

```
IDynamicMetaObjectProvider → DynamicAccessor.Singleton（DLR CallSiteCache）
非全公开类型              → DelegateAccessor（DynamicMethod + restrictedSkipVisibility，
                            委托签名 Func<int,object,object>，名字→index 在托管字典查）
全公开类型                → emit RuntimeTypeAccessor 实体子类（TypeBuilder，
                            类内 _map 字段做 name→index）
```

- **`WriteMapImpl` 双宿主共用**：同一段 IL 生成代码服务 TypeBuilder 方法与 DynamicMethod，靠 `mapField==null` 切换参数布局。
- **"名字→整数→switch" 两段分派**：公开 API 收 string，IL 内只对 int `switch`；字符串比较不进热路径。
- by-ref 属性：读 `Ldobj` 解引用+box；**写时先调 getter 拿托管指针再 `Stobj`**——可无 setter 写入 `ref` 属性；`ref readonly` 显式拒绝。
- `Cast`：引用类型 `castclass`，值类型 `unbox.any`（顺带正确处理 `Nullable<T>`）。

### 4.4 性能与局限

- [官方基准](https://blog.marcgravell.com/2012/01/playing-with-your-member.html)（1M 读+写）：直调 14ms / dynamic 268ms / `PropertyInfo` 8879ms / `PropertyDescriptor` 12847ms / **TypeAccessor 73ms**——比 `PropertyInfo` 快 ~120×，距直调 ~5×（差距=字典查找+装箱+间接调用）。首次 emit+JIT 成本高，靠摊销。
- 局限：成员范围窄；不 AOT 友好（README 明示，[EFCore.BulkExtensions #320](https://github.com/borisdj/EFCore.BulkExtensions/issues/320) 有 Xamarin.iOS 事故）；`AssemblyBuilderAccess.Run` 非 collectible 动态程序集累积；全 `object` 签名装箱（struct 写入静默丢失风险）；大小写敏感、无嵌套路径、无类型转换、无 TryGet 语义；`Member.MemberInfo` 不公开。

### 4.5 借鉴点

- 两级分派结构、`WriteMapImpl` 双宿主、`Hashtable` 读免锁缓存、能力探测（`CreateNewSupported`/`GetMembersSupported`）先于抛异常、`RuntimeTypeAccessor.Type` 用 `ldtoken` 烧进 IL、`ObjectReader` 的 `effectiveTypes/allowNull` 预算模式。
- 改进空间：提供 `accessor.TryGetIndex(name)` 让热循环只传 int 摊掉字典查找；泛型 `Func<T,TVal>` 无盒化签名；`RunAndCollect` 动态程序集或 `ConditionalWeakTable`；公开 `MemberInfo`/Attribute 实例/声明顺序/大小写选项——全是其 issue 高频诉求。

---

## 5. 平台现状（.NET 6–10）：为什么前辈们纷纷退场

FastReflection 的设计必须回答"现代 .NET 已内置什么"。（本节为主代理补充检索）

| 内置能力 | 版本 | 事实 | 来源 |
|---|---|---|---|
| `MethodInfo.Invoke` 提速 | .NET 7 | IL emit 支持的 invoke 路径，4 参方法含 `object[]` 分配快 ~3.2×，缓存数组 ~4.4×；首调不生成 IL 保护启动性能 | [dotnet/runtime#67917](https://github.com/dotnet/runtime/pull/67917) |
| `MethodInvoker`/`ConstructorInvoker` | .NET 8 | 公开 API，`Span`-based Invoke，≤4 参无 `object[]` 分配，4 参场景比 Invoke 快 ~2.4–3.3× | [dotnet/runtime#88415](https://github.com/dotnet/runtime/pull/88415) |
| `UnsafeAccessorAttribute` | .NET 8 | `extern static` + 特性标注，运行时生成私有成员访问实现，**零开销、AOT 兼容**；支持 `Constructor/Method/StaticMethod/Field/StaticField`（字段用 `ref T` 返回读写）；实测 19.4ns → 0.35ns | [MS Learn](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.unsafeaccessorattribute)、[API proposal](https://github.com/dotnet/runtime/issues/81741)、[josef.codes 基准](https://josef.codes/unsafeaccessor-is-my-new-best-friend/) |
| `Invoke` 小参数优化 | .NET 6 | ≤8 参的防御性拷贝走栈分配，省 88B/call | [dotnet/runtime#50814](https://github.com/dotnet/runtime/pull/50814) |
| 源生成反射生态 | — | `SourceGeneration.Reflection`（AOT 反射元数据）、`Apparatus.AOT.Reflection`（源生成+`KeyOf<T>` 编译期成员名检查）、`ANcpLua.AotReflection` | [hi-xiaoxin/SourceGeneration.Reflection](https://github.com/hi-xiaoxin/SourceGeneration.Reflection)、[byme8/Apparatus.AOT.Reflection](https://github.com/byme8/Apparatus.AOT.Reflection) |

**结论**：在 **现代 .NET（net7+）**上，纯 `MethodInfo.Invoke` 加速空间被内置优化大幅压缩；`UnsafeAccessor` 解决了"编译时已知成员"的私有访问；源生成吃掉"编译时已知类型"的 AOT 场景。**运行时才知成员 + 要高吞吐 + 要语义丰富的场景仍是空白**。

**但需修正的重要前提**：上述"空间被压缩"只对 net7+ 成立。**在 .NET Framework / .NET Core ≤6 上，`MethodInfo.Invoke` 没有这些优化**——fast-member 官方 ~120× 提速正是 .NET Framework 4.x 时代的实测。老版本运行时上基线反射更慢 → 委托缓存方案的相对收益**更大**。若目标用户包含 .NET Framework 存量用户（企业存量应用、插件宿主、工业软件等），快速反射的价值主张不但成立，而且更强。因此 FastReflection 应**多目标**：`netstandard2.0` 作为最大公约数覆盖 .NET Framework 4.6.1+ / .NET Core 2.0+ / .NET 5+，叠加现代 TFM 使用平台新能力。详见 §6.6。

### 命名可用性

`FastReflection` 之名在 NuGet 上已被多个老包占用（2011 CodePlex 项目 `FastReflection` 1.0.0.20、`FastReflection.NET`、`gfoidl.FastReflection`、`ZKWeb.Fork.FastReflection`），但**没有一个是主流活跃包**——`FastReflection` ID 本身可用的概率高（老包是 `FastReflection`/`FastReflection.NET` 等不同 ID，需发布时实测）；备选 `Skymly.FastReflection` 前缀式命名。

---

## 6. 对 FastReflection 的设计建议

### 6.1 功能定位

主打**"运行时已知成员的高吞吐访问"** 一个核心场景，覆盖前辈们的短板并集：

- 成员种类：property/field/method/ctor/indexer/event（fast-member 只有前两个）
- 可见性：public + non-public + static（fast-member 全无，dynamitey 靠 hack）
- 调用形态：untyped `object` API + 强类型委托 API + 名称访问器 API 三层并存

### 6.2 建议的 API 分层（融合四家所长）

```
Layer 3  统一非类型化入口
         delegate object? FastInvoker(object? target, Span<object?> args);
         + accessor[target, "name"] 字符串分派（fast-member 模式）
         + TryGet/TrySet/能力元数据（CanRead/CanWrite/IsInitOnly）
Layer 2  泛型委托工厂
         method.CreateInvoker<TDelegate>() / prop.CreateGetter<T,TVal>()
         无盒化签名；ref/out 参数：Ref<T> 包装或要求调用方给精确委托
Layer 1  MethodInfo.CreateDelegate<T> 直通（签名编译时已知，零成本）
Layer 0  成员查找缓存：Type<T>.RequireMethod(...) / GetOrAdd 语义
```

### 6.3 实现策略

| 技术 | 用途 | 备注 |
|---|---|---|
| `MethodInfo.CreateDelegate` | Layer 1 与 open-instance 委托（`Func<object,object>` 适配器需一层薄 emit/Expression 包装） | AOT 兼容 |
| `DynamicMethod`/`Expression.Compile` | Layer 2/3 的 `object`↔精确签名适配器、`ref` 属性 `Stobj` 技巧 | 标记 `[RequiresUnreferencedCode]`，用 `RuntimeFeature.IsDynamicCodeCompiled` 探测降级 |
| 表达式树 vs DynamicMethod | 团队熟悉度选表达式树（调试容易）；极限控制选 DynamicMethod（`restrictedSkipVisibility` 非公开成员） | fast-member 证明 DynamicMethod 更灵活（非公开访问器） |
| `UnsafeAccessor` | 无法用于运行时发现的成员（特性要编译时），**不纳入**但文档对比 | — |
| `MethodInvoker`(.NET8)/`MethodInfo.Invoke` | `IsDynamicCodeCompiled==false` 时的兜底路径 | — |
| 可选 Source Generator 配套包 | 编译期已知类型生成静态访问器表（对标 SourceGeneration.Reflection），非 v1 范围 | — |

### 6.4 必须做对的事（各家血泪教训汇总）

1. **缓存 key 用 `Type`/`MemberInfo` 引用**，不用 FullName 字符串（Namotion #142 碰撞教训）；缓存结构 `ConcurrentDictionary` + `ConditionalWeakTable`（ALC 卸载友好，dynamitey #50 并发教训）。
2. **热路径零分配**：`Span<object?>` 参数 + `stackalloc` 兼容重载 + int-index 访问器（`TryGetIndex` 摊掉字符串查找，fast-member 的 Dictionary 查找是其与直调 5× 差距主因）。
3. **值类型装箱可控**：提供 `Func<T,TVal>`/`Action<T,TVal>` 泛型出口；明确文档说明 struct 目标成员写语义（fast-member 静默丢失坑）。
4. **能力探测一等公民**：`CanRead/CanWrite/IsInitOnly/IsStatic` 先于抛异常可查；by-ref/`ref readonly`/`init`/`required`/record/indexer/static 尽早设计进去。
5. **AOT 分级**：`RequiresDynamicCode`/`RequiresUnreferencedCode` 精确标注到方法级；提供 `FastReflectionOptions`（emit/反射兜底开关）。
6. **命名与错误模型**：`Get*`（可空返回）vs `Require*`（抛异常）语义分离（DotNext）；成员未找到抛 `MissingMemberException` 类而非 `ArgumentOutOfRangeException`。
7. **测试基建**：BenchmarkDotNet 对比直调/`MethodInfo.Invoke`/`MethodInvoker`/`dynamic`/Compile 委托五条基线；NRT/XML docs 测试需专门 `<Nullable>enable</Nullable>` 编译产物测试程序集（Namotion 模式）。

### 6.5 不建议做的事

- 不做 NRT 推断/XML docs（Namotion 已做得很好，可直接依赖或文档推荐组合使用）
- 不做 `IDynamicMetaObjectProvider` 兜底（除非定位为 dynamic 网关——那是 dynamitey 的领域，且 C# binder 依赖重）
- 不做 ObjectReader/DbDataReader 适配（fast-member 已占，除非作为可选 satellite 包）

### 6.6 多目标框架策略（.NET Framework 兼容是硬需求）

**为什么必须支持**：见 §5 修正——老版本反射基线慢，加速收益更大；fast-member 65M+ 下载的很大部分即存量 .NET Framework 用户（EFCore.BulkExtensions 等 238 个包依赖它）。

**建议 TFM 组合（已确认为 `net462;netstandard2.0;net8.0`）**——与 Namotion.Reflection 的三目标一致：

- **`net462`**：.NET Framework 用户吃到框架原生程序集——Emit/`DynamicMethod` 在 mscorlib 内置、**零 NuGet 依赖**；规避 netstandard2.0 垫片在 Framework 上著名的绑定重定向地狱（`System.Net.Http`/`System.Memory`/`System.ValueTuple` 是重灾区）。net462 是 netstandard2.0 兼容的实际最低线（net461 理论兼容但有已知缺陷，微软官方建议 net462 起）。
- **`netstandard2.0`**：覆盖 .NET Core 2.0–3.1 / .NET 5–7 及所有中间态消费者（net472+ 也可解析此资产）。fast-member（`netstandard2.0;net461;netcoreapp2.0`）、dynamitey（`netstandard2.0;net40`）都以它为公约数。
- **`net8.0`**：`MethodInvoker` 兜底、`RequiresUnreferencedCode`/`RequiresDynamicCode` 原生标注、Span/ValueTuple 零依赖的现代 API 面；如需 `FrozenDictionary`/`UnsafeAccessor` 文档对比也在这层。

| 能力 | net462 | netstandard2.0 | net8.0 |
|---|---|---|---|
| `DynamicMethod`/`ILGenerator` | ✅ mscorlib 内置，零依赖 | ✅ 需 `System.Reflection.Emit.ILGeneration` + `System.Reflection.Emit.Lightweight` 包（gfoidl.FastReflection 同款依赖） | ✅ 内置 |
| 非公开成员访问 | `DynamicMethod(..., skipVisibility/restrictedSkipVisibility: true)` ✅；**部分信任/中等信任 ASP.NET 场景需 `ReflectionPermission(RestrictedMemberAccess)`**，历史边缘情况但文档要提 | ✅ 同左（`restrictedSkipVisibility` 在 .NET Core 上被忽略但无害） | ✅ 同左（AOT 场景退化为不支持） |
| `Expression.Compile()` | ✅ 但**编译出的委托不能调非公开成员**（无 skipVisibility 通道）→ 非公开路径必须用 DynamicMethod，与 fast-member `DelegateAccessor` 结论一致 | 同左 | 同左 |
| `CreateDelegate` open/closed | ✅ | ✅ | ✅ |
| `MethodInvoker`/`ConstructorInvoker` | ❌ 不存在 → 动态代码不可用时兜底 `MethodInfo.Invoke` | ❌ 同左 | ✅ 兜底更优 |
| `UnsafeAccessor` | ❌（编译时特性，运行时成员场景本就用不上） | ❌ | ✅（文档对比用） |
| `Span<object>`/`stackalloc` 参数 | 需 `System.Memory` 包 → **建议不引**，net462/netstandard2.0 走 `object[]`/`params`，Span 重载仅 net8.0 暴露 | 同左 | ✅ 内置 |
| `ValueTuple`/`Ref<T>` 打包 | 需 `System.ValueTuple` 包 → 同上取舍 | 同左 | ✅ |
| `RequiresUnreferencedCode`/`RequiresDynamicCode` 标注 | 框架无此特性 → **polyfill 内部同名特性类**（链接器按全名识别，业内通行做法） | 同左 | ✅ 内置 |
| `RuntimeFeature.IsDynamicCodeCompiled` | ✅ 可用 → 三 TFM 统一的运行时探测降级路径 | ✅ | ✅ |
| `ConcurrentDictionary`/`ConditionalWeakTable` | ✅ | ✅ | ✅ |
| `RuntimeHelpers.GetUninitializedObject`（跳过 ctor 实例化） | ✅ | ✅ | ✅ |

**.NET Framework 专属注意点**：
- **强签名**：企业 .NET Framework 用户常有强名程序集要求 → 参照 `FastMember.Signed` 模式直接给程序集签名（或出 Signed 变体包）。
- **卸载模型不同**：.NET Framework 是 AppDomain 卸载而非 AssemblyLoadContext → 缓存用 `ConditionalWeakTable` 两边都安全；`DynamicMethod` 本就 collectible；若用 `TypeBuilder` 必须 `RunAndCollect`（netstandard2.0 支持）。
- **部分信任**：`restrictedSkipVisibility` 在 .NET Framework 中等信任下抛 `MethodAccessException`/`SecurityException` 系——提供 `FastReflectionOptions` 关闭非公开访问降级到普通反射。
- **依赖最小化争议**：netstandard2.0 上 Emit 需两个包、`Span` 需 `System.Memory`。fast-member 靠零依赖赢了生态位；建议**核心包零依赖**（`object[]` API + `RuntimeHelpers`），Span/ValueTuple 增强 API 放现代 TFM 或 satellite 包。
- **`init`/`required`/record**：编译器特性在这些 TFM 下也能消费（polyfill attribute），访问器层按普通属性处理即可，但 `required` 在运行时无强制——`CreateNew` 语义与 fast-member 相同：只调 ctor，不校验。
- **强名程序集引用**：若库本身签名且动态程序集要访问 internal 成员，`InternalsVisibleTo("FastReflection_dynamic, PublicKey=...")` 的场景要文档化（.NET Framework 用户真实痛点）。

---

## 7. 主要来源索引

- DotNext：[repo](https://github.com/dotnet/dotNext) · [fast reflection 文档](https://dotnet.github.io/dotNext/features/reflection/fast.html) · [弃用声明](https://github.com/dotnet/dotNext/discussions/142) · [TaskType.cs](https://github.com/dotnet/dotNext/blob/08d2384dc6ff7547317f5e4be1fa5e6e5c81759f/src/DotNext/Reflection/TaskType.cs)
- dynamitey：[repo](https://github.com/ekonbenefits/dynamitey) · [InvokeHelper-Regular.cs](https://github.com/ekonbenefits/dynamitey/blob/master/Dynamitey/Internal/Optimization/InvokeHelper-Regular.cs) · [社区 fork 说明](https://fsprojects.github.io/FSharp.Interop.Dynamic/docs/dynamitey.html)
- Namotion.Reflection：[repo](https://github.com/RicoSuter/Namotion.Reflection) · [CLAUDE.md](https://github.com/RicoSuter/Namotion.Reflection/blob/master/CLAUDE.md) · [ContextualTypeExtensions.cs](https://github.com/RicoSuter/Namotion.Reflection/blob/master/src/Namotion.Reflection/Context/ContextualTypeExtensions.cs) · [XmlDocsExtensions.cs](https://github.com/RicoSuter/Namotion.Reflection/blob/master/src/Namotion.Reflection/XmlDocsExtensions.cs) · [roslyn nullable-metadata](https://github.com/dotnet/roslyn/blob/main/docs/features/nullable-metadata.md)
- fast-member：[repo](https://github.com/mgravell/fast-member) · [官方基准博文](https://blog.marcgravell.com/2012/01/playing-with-your-member.html) · [NuGet](https://www.nuget.org/packages/FastMember)
- 平台：[MethodInfo.Invoke IL emit](https://github.com/dotnet/runtime/pull/67917) · [MethodInvoker/ConstructorInvoker](https://github.com/dotnet/runtime/pull/88415) · [UnsafeAccessor](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.unsafeaccessorattribute) · [UnsafeAccessor proposal](https://github.com/dotnet/runtime/issues/81741)

> 注：四份子代理详细原始报告（含未确认事项标注）存于会话记录；本文档已做交叉提炼。个别底层实现细节（DotNext `DynamicInvoker` 后端、Namotion `PropertyReader` 委托生成方式）未能逐字确认，已标注为推断。
