namespace FastReflection.AotSmoke
{
    /// <summary>Reflection target for the smoke scenarios. All members are kept by the DynamicallyAccessedMembers root in Program.</summary>
    public class SmokePoco
    {
        public string Name { get; set; } = "initial";
        public int Count;

        public SmokePoco() { }

        public static int Twice(int x) => x * 2;
        public int Add(int a, int b) => a + b;
        public void Swap(ref int a, ref int b) { var t = a; a = b; b = t; }
        public void Increment(ref int value) => value++;
    }
}
