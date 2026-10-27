using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using FrObjectReader = FastReflection.ObjectReader.ObjectReader;

namespace FastReflection.AotSmoke
{
    /// <summary>
    /// Asserts the dynamic-code-unavailable contract end-to-end through a real PublishAot binary.
    /// Exit 0 when every scenario passes; otherwise the scenario's 1-based index, with its name on stderr.
    /// External behavior only — no internal seam.
    /// </summary>
    internal static class Program
    {
        // The #9 rooting contract: every member the scenarios touch is kept by this annotation.
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        private static Type PocoType => typeof(SmokePoco);

        private static int Main()
        {
            var scenarios = new (string Name, Action Run)[]
            {
                ("dynamic-code-unavailable", DynamicCodeUnavailable),
                ("unreflect-exact-bind", UnreflectExactBind),
                ("rooted-accessor-read-write", RootedAccessorReadWrite),
                ("getter-fallback", GetterFallback),
                ("invoker-fallback", InvokerFallback),
                ("span-invoker-fallback", SpanInvokerFallback),
                ("bound-call-fallback", BoundCallFallback),
                ("factory-fallback", FactoryFallback),
                ("ref-typed-invoker-not-supported", RefTypedInvokerNotSupported),
                ("object-reader-fallback", ObjectReaderFallback),
            };

            for (var i = 0; i < scenarios.Length; i++)
            {
                try
                {
                    scenarios[i].Run();
                    Console.WriteLine($"PASS {scenarios[i].Name}");
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"FAIL {scenarios[i].Name}: {e.GetType().Name}: {e.Message}");
                    return i + 1;
                }
            }

            return 0;
        }

        // A mispublished binary (JIT, or dynamic code somehow available) fails here —
        // the fallback contract is only proven when dynamic code is genuinely absent.
        private static void DynamicCodeUnavailable()
        {
            Check(!RuntimeFeature.IsDynamicCodeSupported, "RuntimeFeature.IsDynamicCodeSupported is true");
            Check(!RuntimeFeature.IsDynamicCodeCompiled, "RuntimeFeature.IsDynamicCodeCompiled is true");
        }

        private static void UnreflectExactBind()
        {
            var twice = PocoType.GetMethod(nameof(SmokePoco.Twice))!.Unreflect<Func<int, int>>();
            Equal(42, twice(21), "Unreflect Twice(21)");
        }

        private static void RootedAccessorReadWrite()
        {
            var accessor = TypeAccessor.Create(PocoType);
            var poco = new SmokePoco();

            Check(accessor.TryGetOrdinal(nameof(SmokePoco.Name), out var nameOrdinal), "ordinal for Name missing");
            Equal("initial", accessor.GetValue(poco, nameOrdinal), "read Name by ordinal");
            Equal(0, accessor[poco, nameof(SmokePoco.Count)], "read Count by name");

            accessor[poco, nameof(SmokePoco.Name)] = "updated";
            accessor.SetValue(poco, nameOrdinal, "again");
            Equal("again", poco.Name, "write Name");
            accessor[poco, nameof(SmokePoco.Count)] = 41;
            Equal(41, poco.Count, "write Count");

            if (accessor.CreateNew() is not SmokePoco fresh)
                throw new InvalidOperationException("CreateNew did not produce a SmokePoco");
            Equal("initial", fresh.Name, "CreateNew member state");
        }

        private static void GetterFallback()
        {
            var poco = new SmokePoco { Count = 5 };
            var nameGet = PocoType.GetProperty(nameof(SmokePoco.Name))!.CreateGetter();
            var countGet = PocoType.GetField(nameof(SmokePoco.Count))!.CreateGetter();
            Equal("initial", nameGet(poco), "property getter");
            Equal(5, countGet(poco), "field getter");
        }

        private static void InvokerFallback()
        {
            var poco = new SmokePoco();
            var add = PocoType.GetMethod(nameof(SmokePoco.Add))!.CreateInvoker();
            Equal(9, add(poco, new object?[] { 4, 5 }), "Add(4, 5)");

            var swap = PocoType.GetMethod(nameof(SmokePoco.Swap))!.CreateInvoker();
            var args = new object?[] { 3, 4 };
            Equal(null, swap(poco, args), "Swap return");
            Equal(4, args[0], "Swap write-back a");
            Equal(3, args[1], "Swap write-back b");
        }

        private static void SpanInvokerFallback()
        {
            var poco = new SmokePoco();
            var add = PocoType.GetMethod(nameof(SmokePoco.Add))!.CreateSpanInvoker();
            Equal(9, add(poco, new object?[] { 4, 5 }.AsSpan()), "span Add(4, 5)");
        }

        // A bound call resolving and completing through reflection completion (#35).
        private static void BoundCallFallback()
        {
            var accessor = TypeAccessor.Create(PocoType);
            var poco = new SmokePoco();

            Equal(9, accessor.Invoke(poco, nameof(SmokePoco.Add), new object?[] { 4, 5 }), "bound Add(4, 5)");
            Equal(6, accessor.Invoke(poco, nameof(SmokePoco.Twice), new object?[] { 3 }), "bound static Twice(3)");
            Equal("initial", accessor.GetValue(poco, nameof(SmokePoco.Name)), "bound get Name");
            accessor.SetValue(poco, nameof(SmokePoco.Count), 7);
            Equal(7, poco.Count, "bound set Count");

            if (accessor.CreateNew(Array.Empty<object?>()) is not SmokePoco fresh)
                throw new InvalidOperationException("bound CreateNew did not produce a SmokePoco");
            Equal("initial", fresh.Name, "bound CreateNew member state");
        }

        private static void FactoryFallback()
        {
            if (PocoType.CreateFactory()() is not SmokePoco poco)
                throw new InvalidOperationException("factory did not produce a SmokePoco");
            Equal("initial", poco.Name, "factory member state");
        }

        private static void RefTypedInvokerNotSupported()
        {
            var method = PocoType.GetMethod(nameof(SmokePoco.Increment))!;
            try
            {
                method.CreateInvoker<IncrementRef>();
                throw new InvalidOperationException("expected NotSupportedException, bind completed");
            }
            catch (NotSupportedException e)
            {
                Check(e.Message.Contains("dynamic code"), $"unexpected message: {e.Message}");
            }
        }

        // The ObjectReader satellite under no dynamic code (#36): column binding and reads
        // complete through the same reflection-completion path as the core accessor.
        private static void ObjectReaderFallback()
        {
            var rows = new List<SmokePoco>
            {
                new() { Name = "a", Count = 1 },
                new() { Name = "b", Count = 2 },
            };
            using var reader = FrObjectReader.Create(rows, nameof(SmokePoco.Name), nameof(SmokePoco.Count));
            Equal(2, reader.FieldCount, "reader field count");
            Check(reader.Read(), "reader first row");
            Equal("a", reader.GetString(0), "reader Name");
            Equal(1, reader.GetInt32(1), "reader Count");
            Check(reader.Read(), "reader second row");
            Check(!reader.Read(), "reader exhausted");
        }

        private delegate void IncrementRef(SmokePoco target, ref Ref<int> value);

        private static void Check(bool condition, string what)
        {
            if (!condition)
                throw new InvalidOperationException(what);
        }

        private static void Equal(object? expected, object? actual, string what)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException($"{what}: expected {expected ?? "<null>"}, got {actual ?? "<null>"}");
        }
    }
}
