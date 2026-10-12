using System.Reflection;
using BenchmarkDotNet.Attributes;
using FastReflection;
using BclMethodInvoker = System.Reflection.MethodInvoker;

[MemoryDiagnoser]
public class MemberAccessBenchmarks
{
    public class Target
    {
        public int Value { get; set; } = 42;
        public int Add(int a, int b) => a + b;
    }

    private readonly Target _target = new();
    private PropertyInfo _prop = null!;
    private MethodInfo _method = null!;
    private Func<object, object> _getter = null!;
    private TypeAccessor _accessor = null!;
    private int _ordinal;
    private FastInvoker _invoker = null!;
    private BclMethodInvoker _bclInvoker = null!;
    private object[] _args = { 2, 3 };

    [GlobalSetup]
    public void Setup()
    {
        _prop = typeof(Target).GetProperty(nameof(Target.Value))!;
        _method = typeof(Target).GetMethod(nameof(Target.Add))!;
        _getter = _prop.CreateGetter()!;
        _accessor = TypeAccessor.Create(typeof(Target));
        _accessor.TryGetOrdinal(nameof(Target.Value), out _ordinal);
        _invoker = _method.CreateInvoker();
        _bclInvoker = BclMethodInvoker.Create(_method);
    }

    [Benchmark(Baseline = true)]
    public int DirectCall() => _target.Value;

    [Benchmark]
    public object PropertyInfo_GetValue() => _prop.GetValue(_target)!;

    [Benchmark]
    public object FastReflection_Getter() => _getter(_target);

    [Benchmark]
    public object TypeAccessor_ByName() => _accessor[_target, "Value"]!;

    [Benchmark]
    public object TypeAccessor_ByOrdinal() => _accessor.GetValue(_target, _ordinal)!;

    [Benchmark]
    public object MethodInfo_Invoke() => _method.Invoke(_target, _args)!;

    [Benchmark]
    public object BclMethodInvoker_Invoke() => _bclInvoker.Invoke(_target, _args.AsSpan())!;

    [Benchmark]
    public object FastReflection_Invoker() => _invoker(_target, _args)!;
}
