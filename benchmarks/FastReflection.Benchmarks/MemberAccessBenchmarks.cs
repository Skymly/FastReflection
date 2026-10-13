using System.Reflection;
using BenchmarkDotNet.Attributes;
using FastReflection;
#if NET8_0_OR_GREATER
using BclMethodInvoker = System.Reflection.MethodInvoker;
#endif

[MemoryDiagnoser]
public class MemberAccessBenchmarks
{
    public class Target
    {
        public int Value { get; set; } = 42;
        public int Add(int a, int b) => a + b;
        public bool TryParse(string s, out int v)
        {
            if (int.TryParse(s, out v)) return true;
            v = -1;
            return false;
        }
    }

    private readonly Target _target = new();
    private PropertyInfo _prop = null!;
    private MethodInfo _method = null!;
    private Func<object, object> _getter = null!;
    private TypeAccessor _accessor = null!;
    private int _ordinal;
    private FastInvoker _invoker = null!;
    private FastInvoker _parseObject = null!;
    private Parse _parseRef = null!;
    private readonly object[] _parseArgs = { "12", 0 };
    private Ref<int> _refSlot;
    private int _directSlot;
#if NET8_0_OR_GREATER
    private BclMethodInvoker _bclInvoker = null!;
    private FastSpanInvoker _spanInvoker = null!;
#endif
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
        var parse = typeof(Target).GetMethod(nameof(Target.TryParse))!;
        _parseObject = parse.CreateInvoker();
        _parseRef = parse.CreateInvoker<Parse>();
#if NET8_0_OR_GREATER
        _bclInvoker = BclMethodInvoker.Create(_method);
        _spanInvoker = _method.CreateSpanInvoker();
#endif
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

#if NET8_0_OR_GREATER
    [Benchmark]
    public object BclMethodInvoker_Invoke() => _bclInvoker.Invoke(_target, _args.AsSpan())!;
#endif

    [Benchmark]
    public object FastReflection_Invoker() => _invoker(_target, _args)!;

#if NET8_0_OR_GREATER
    [Benchmark]
    public object FastReflection_SpanInvoker() => _spanInvoker(_target, _args.AsSpan())!;
#endif

    [Benchmark]
    public bool TryParse_Direct() => _target.TryParse("12", out _directSlot);

    [Benchmark]
    public bool TryParse_ObjectArray()
    {
        _parseArgs[0] = "12";
        return (bool)_parseObject(_target, _parseArgs)!;
    }

    [Benchmark]
    public bool TryParse_Ref() => _parseRef(_target, "12", out _refSlot);

    private delegate bool Parse(Target target, string text, out Ref<int> value);
}
