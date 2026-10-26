namespace FastReflection.Tests
{
    public interface ISample
    {
        int Value { get; set; }
        string? Name { get; }
    }

    public interface ISampleExtra
    {
        string Extra { get; }
    }

    public interface ISampleCombined : ISample, ISampleExtra
    {
    }

    public class Sample : ISampleCombined
    {
        public int Value { get; set; } = 7;
        public string? Name { get; set; } = "s";
        public string Extra => "e";
        public string ReadOnlyProp { get; } = "ro";
        public int PrivateSetterProp { get; private set; }
        internal int InternalProp { get; set; }
        private int PrivateProp { get; set; }
        public int PublicField = 5;
        public readonly int ReadOnlyField = 9;
        public long LongProp { get; set; }
        public long LongField;
        public int? NullableProp { get; set; }
        public DayOfWeek DayProp { get; set; }
        public int WriteOnlyProp { set { } }
        public int SetterThrows { get => 0; set => throw new InvalidOperationException("boom"); }
        private string _privateField = "pf";
        public const int ConstField = 3;
        public static int StaticProp { get; set; }
        public static string? StaticField;
        public int this[int i] => i * 2;

        public Sample() { }
        private Sample(int v) { Value = v; }

        public int Add(int a, int b) => a + b;
        public int Bump() { Value++; return Value; }
        public string Tag = "tag";
        public ref string RefTag() => ref Tag;
        public static int StaticAdd(int a, int b) => a + b;
        public void Nothing() { }
        public bool TryParse(string s, out int v)
        {
            if (int.TryParse(s, out v)) return true;
            v = -1;
            return false;
        }
        public long Widen(long x) => x;
        public int Throwing(int x) => throw new InvalidOperationException("boom");
        public void SetThenThrow(ref int x) { x = 99; throw new InvalidOperationException("boom"); }
        public void Swap(ref int a, ref int b) { var t = a; a = b; b = t; }
        public static void StaticSwap(ref int a, ref int b) { var t = a; a = b; b = t; }
        private bool TrySecret(out int v) { v = 7; return true; }
        public int SumIn(in int a, in int b) => a + b;
        private int Secret() => 42;
        public T Echo<T>(T t) => t;
        public ref int RefProp => ref PublicField;
        public int Overload(int a) => a;
        public int Overload(int a, int b) => a + b;

        public void SetPrivateField(string s) => _privateField = s;
        public string GetPrivateField() => _privateField;
        public void SetPrivateProp(int v) => PrivateProp = v;
        public int GetPrivateProp() => PrivateProp;
    }

    public struct SampleStruct
    {
        public int X { get; set; }
        public int Y;

        public SampleStruct(int x, int y)
        {
            X = x;
            Y = y;
        }

        private void SecretSwap(ref int a, ref int b) { var t = a; a = b; b = t; }
    }

    public class NoPublicCtor
    {
        private NoPublicCtor() { }
        public NoPublicCtor(int x) { }
    }

    public class ThrowingCtor
    {
        public ThrowingCtor() => throw new InvalidOperationException("ctor boom");
        public ThrowingCtor(int x) => throw new InvalidOperationException("ctor boom");
    }

    // TypeAccessor caches per (type, visibility) and never revisits the fallback flag,
    // so each contract leg builds its accessor over its own marker subclass.
    public class EmitLegSample : Sample { }
    public class ReflectionLegSample : Sample { }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class MarkerAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
    public sealed class TagAttribute : Attribute
    {
        public TagAttribute(string name) => Name = name;
        public string Name { get; }
    }

    public class AttributedBase
    {
        [Marker]
        public virtual int Overridden { get; set; }
    }

    public class AttributedSample : AttributedBase
    {
        public override int Overridden { get; set; }
        [Tag("p")] public int TaggedProp { get; set; }
        [Tag("f")] public int TaggedField;
        [Tag("a"), Tag("b")] public int TwiceTagged { get; set; }
        public int Plain { get; set; }
    }

    // Unique initial values so ordinal reads can be checked against the sorted member.
    public class OrderedSample
    {
        public int FreeA { get; set; } = 100;
        [Ordinal(10)] public int Ten { get; set; } = 110;
        [Ordinal(1)] public int TieA { get; set; } = 101;
        public int FreeB { get; set; } = 102;
        [Ordinal(1)] public int TieB { get; set; } = 103;
        [Ordinal(-5)] public int MinusFive = 104;
        [Ordinal(1)] public int TieC = 105;
        public int FreeC = 106;
    }

    // Same cache rule as the *LegSample pair above: each ordering leg needs its own type.
    public class EmitLegOrdered : OrderedSample { }
    public class ReflectionLegOrdered : OrderedSample { }

    // ============================== bound calls (#35) ==============================

    /// <summary>The bound-call domain fixture: overloads, optionals, params, generics, indexers, statics, by-ref verbs.</summary>
    public class BoundSample
    {
        public BoundSample() { }
        public BoundSample(int v) { Value = v; }
        public BoundSample(int v, string tag) { Value = v; Tag = tag; }

        public int Value { get; set; } = 7;
        public int GetOnly => 9;
        public int SetOnly { set { } }
        public string? Name { get; set; }
        public int? NullableProp { get; set; }
        public string Tag = "tag";
        public int IndexValue;
        public static int StaticProp { get; set; }
        public static string? StaticField;
        public const int ConstField = 3;

        public int this[int i]
        {
            get => i * 2;
            set => IndexValue = i + value;
        }

        public string this[string s] => s + "!";

        public int Add(int a, int b) => a + b;
        public long Widen(long x) => x;
        public decimal ToDecimal(decimal d) => d;
        public char ToChar(char c) => c;
        public int? Nullables(int? v) => v;
        public DayOfWeek Day(DayOfWeek d) => d;
        public int EnumToInt(int v) => v;
        public void Nothing() { }
        public int Throwing() => throw new InvalidOperationException("boom");
        public int Optional(int a, int b = 2) => a * 10 + b;
        public int OptOver(int a) => a;
        public int OptOver(int a, int b = 5) => a + b;
        public string Named(int first, string second) => first + ":" + second;

        public int Sum(params int[] xs)
        {
            var s = 0;
            foreach (var x in xs) s += x;
            return s;
        }

        public int SumTail(int head, params int[] xs) => head + xs.Length;
        // Betterness: conversion quality precedes the normal-form tie-break — the int
        // candidate beats the widening long one even though it fills a packed params slot.
        public int HeadOrWide(int head, params int[] xs) => head;
        public long HeadOrWide(long x) => x;
        public T Echo<T>(T t) => t;
        public T NewT<T>() where T : new() => new();
        public T RefT<T>(T t) where T : class => t;
        public int Overloaded(int x) => x;
        public long Overloaded(long x) => x;
        public string Amb(int? a, string b) => "a";
        public string Amb(string a, int? b) => "b";

        public int Swap(ref int a, ref int b)
        {
            var t = a;
            a = b;
            b = t;
            return a;
        }

        public bool TryOut(string s, out int v) => int.TryParse(s, out v);
        public int SumIn(in int a, in int b) => a + b;
        public void SetThenThrow(ref int x) { x = 99; throw new InvalidOperationException("boom"); }
        public static int StaticAdd(int a, int b) => a + b;
        public int WithImplicit(int x) => x;
        private int Secret() => 42;
    }

    /// <summary>Only converts through a user-defined op_Implicit — never sanctioned.</summary>
    public class ImplicitArg
    {
        public static implicit operator int(ImplicitArg arg) => 5;
    }

    // Same cache rule as the *LegSample pair above: each bound-call leg needs its own type.
    // Constructors are not inherited — the legs redeclare the ctor shapes for CreateNew binds.
    public class EmitBoundLeg : BoundSample
    {
        public EmitBoundLeg() { }
        public EmitBoundLeg(int v) : base(v) { }
        public EmitBoundLeg(int v, string tag) : base(v, tag) { }
    }

    public class ReflectionBoundLeg : BoundSample
    {
        public ReflectionBoundLeg() { }
        public ReflectionBoundLeg(int v) : base(v) { }
        public ReflectionBoundLeg(int v, string tag) : base(v, tag) { }
    }

    // Per-leg hiding fixtures: 'new' shadows the inherited Add — most-derived wins.
    public class EmitBoundDerived : EmitBoundLeg
    {
        public new int Add(int a, int b) => -1;
    }

    public class ReflectionBoundDerived : ReflectionBoundLeg
    {
        public new int Add(int a, int b) => -1;
    }

    // Per-leg struct fixtures: identical X/Y shapes for the value-type binds.
    public struct EmitBoundStruct
    {
        public int X { get; set; }
        public int Y;

        public EmitBoundStruct(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    public struct ReflectionBoundStruct
    {
        public int X { get; set; }
        public int Y;

        public ReflectionBoundStruct(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    public class OrderedOverrideBase
    {
        [Ordinal(1)] public virtual int Late { get; set; }
    }

    public class OrderedOverrideDerived : OrderedOverrideBase
    {
        public int Early { get; set; }
        public override int Late { get; set; }
    }
}
