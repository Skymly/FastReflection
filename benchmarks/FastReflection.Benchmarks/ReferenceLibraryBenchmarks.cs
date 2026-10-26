using System.Reflection;
using BenchmarkDotNet.Attributes;
#if NET8_0_OR_GREATER
using DotNext.Reflection;
#endif

// Reference-library comparison rows (docs/specs/1.0-quality-gates.md — Benchmark gate):
// reported next to the library rows, never gated — the gate set is benchmarks/baseline.json
// only. New libraries join as their features land (Namotion XmlDocs, fast-member ObjectReader
// feed); DotNext.Reflection has no net472-compatible asset, so its rows exist on the net8.0
// leg only. The #35 bound-call rows pair each Dynamitey verb with the library verb it
// compares against — all report-only.
[MemoryDiagnoser]
public class ReferenceLibraryBenchmarks
{
    public class Target
    {
        public int Value { get; set; } = 42;
        public int Add(int a, int b) => a + b;
    }

    private readonly Target _target = new();
    private FastMember.TypeAccessor _fastMember = null!;
    private FastReflection.TypeAccessor _frAccessor = null!;
    private Func<object, object> _frConverter = null!;
    private readonly object[] _invokeArgs = { 2, 3 };
#if NET8_0_OR_GREATER
    private DynamicInvoker _dotNextInvoker = null!;
    private Func<Target, int, int, int> _dotNextUnreflect = null!;
#endif

    [GlobalSetup]
    public void Setup()
    {
        _fastMember = FastMember.TypeAccessor.Create(typeof(Target));
        _frAccessor = FastReflection.TypeAccessor.Create(typeof(Target));
        _frConverter = FastReflection.ReflectionExtensions.CreateConverter(typeof(int), typeof(long));
#if NET8_0_OR_GREATER
        var method = typeof(Target).GetMethod(nameof(Target.Add))!;
        _dotNextInvoker = method.Unreflect();
        _dotNextUnreflect = method.Unreflect<Func<Target, int, int, int>>();
#endif
    }

    [Benchmark]
    public object FastMember_AccessorGet() => _fastMember[_target, "Value"]!;

    [Benchmark]
    public void FastMember_AccessorSet() => _fastMember[_target, "Value"] = 7;

    [Benchmark]
    public object Dynamitey_InvokeMember() => Dynamitey.Dynamic.InvokeMember(_target, nameof(Target.Add), _invokeArgs)!;

    [Benchmark]
    public object Dynamitey_InvokeGet() => Dynamitey.Dynamic.InvokeGet(_target, nameof(Target.Value))!;

    [Benchmark]
    public object Dynamitey_InvokeSet() => Dynamitey.Dynamic.InvokeSet(_target, nameof(Target.Value), 7)!;

    [Benchmark]
    public object Dynamitey_InvokeConvert() => Dynamitey.Dynamic.InvokeConvert(5, typeof(long))!;

    [Benchmark]
    public object FastReflection_BoundInvoke() => _frAccessor.Invoke(_target, nameof(Target.Add), _invokeArgs)!;

    [Benchmark]
    public object FastReflection_BoundGet() => _frAccessor.GetValue(_target, nameof(Target.Value))!;

    [Benchmark]
    public void FastReflection_BoundSet() => _frAccessor.SetValue(_target, nameof(Target.Value), 7);

    [Benchmark]
    public object FastReflection_Convert() => _frConverter(5)!;

#if NET8_0_OR_GREATER
    [Benchmark]
    public object DotNext_Invoker() => _dotNextInvoker(_target, _invokeArgs.AsSpan())!;

    [Benchmark]
    public int DotNext_Unreflect() => _dotNextUnreflect(_target, 2, 3);
#endif
}
