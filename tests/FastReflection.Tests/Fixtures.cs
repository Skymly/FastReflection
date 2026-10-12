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
        private string _privateField = "pf";
        public const int ConstField = 3;
        public static int StaticProp { get; set; }
        public static string? StaticField;
        public int this[int i] => i * 2;

        public Sample() { }
        private Sample(int v) { Value = v; }

        public int Add(int a, int b) => a + b;
        public static int StaticAdd(int a, int b) => a + b;
        public void Nothing() { }
        public bool TryParse(string s, out int v)
        {
            if (int.TryParse(s, out v)) return true;
            v = -1;
            return false;
        }
        public void Swap(ref int a, ref int b) { var t = a; a = b; b = t; }
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
    }

    public class NoPublicCtor
    {
        private NoPublicCtor() { }
        public NoPublicCtor(int x) { }
    }
}
