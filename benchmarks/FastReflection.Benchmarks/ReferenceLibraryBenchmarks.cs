using System.Reflection;
using BenchmarkDotNet.Attributes;
#if NET8_0_OR_GREATER
using DotNext.Reflection;
#endif

// Reference-library comparison rows (docs/specs/1.0-quality-gates.md — Benchmark gate):
// reported next to the library rows, never gated — the gate set is benchmarks/baseline.json
// only. New libraries join as their features land (Dynamitey bound calls, Namotion XmlDocs,
// fast-member ObjectReader feed); DotNext.Reflection has no net472-compatible asset, so its
// rows exist on the net8.0 leg only.
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
#if NET8_0_OR_GREATER
    private DynamicInvoker _dotNextInvoker = null!;
    private Func<Target, int, int, int> _dotNextUnreflect = null!;
    private readonly object[] _invokeArgs = { 2, 3 };
#endif

    [GlobalSetup]
    public void Setup()
    {
        _fastMember = FastMember.TypeAccessor.Create(typeof(Target));
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

#if NET8_0_OR_GREATER
    [Benchmark]
    public object DotNext_Invoker() => _dotNextInvoker(_target, _invokeArgs.AsSpan())!;

    [Benchmark]
    public int DotNext_Unreflect() => _dotNextUnreflect(_target, 2, 3);
#endif
}
