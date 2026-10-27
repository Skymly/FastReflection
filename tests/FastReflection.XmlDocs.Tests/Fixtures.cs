namespace FastReflection.XmlDocs.Tests
{
    /// <summary>Person summary text.</summary>
    /// <remarks>Person remarks text.</remarks>
    /// <example>Person example text.</example>
    public class Person
    {
        /// <summary>Creates a person.</summary>
        public Person()
        {
        }

        /// <summary>Raised on rename.</summary>
        public event EventHandler? Renamed
        {
            add { }
            remove { }
        }

        /// <summary>Name summary with <see cref="Person"/> inline.</summary>
        public string? Name { get; set; }

        /// <summary>Age field summary.</summary>
        /// <remarks>Age remarks.</remarks>
        public int Age;

        /// <summary>Combines two values.</summary>
        /// <param name="a">The first value.</param>
        /// <param name="b">The second value.</param>
        /// <returns>The combined text.</returns>
        /// <response code="200">The OK response.</response>
        public string Combine(int a, string b) => b + a;

        /// <summary>
        /// Multi-line summary
        /// second line here.
        /// </summary>
        /// <param name="values">The values.</param>
        public int Sum(params int[] values) => values.Sum();

        public int Undocumented() => 0;

        public int UndocumentedParam(int x) => x;
    }

    /// <summary>Converts summary.</summary>
    public class Converts
    {
        /// <summary>Converts to an int.</summary>
        public static implicit operator int(Converts c) => 0;

        /// <summary>Reads a ref parameter.</summary>
        /// <param name="a">The array.</param>
        /// <param name="b">The ref value.</param>
        public void Ref(int[] a, ref int b) => b = a.Length;

        /// <summary>Reads a pointer parameter.</summary>
        public unsafe void Ptr(int* p) => *p = 1;

        /// <summary>Reads a two-dimensional array.</summary>
        /// <param name="grid">The grid.</param>
        public void Grid(int[,] grid) { }

        /// <summary>Reads a two-argument constructed generic.</summary>
        /// <param name="map">The map.</param>
        public void Map(Dictionary<int, string> map) { }
    }

    /// <summary>Box summary.</summary>
    public class Box<T>
    {
        /// <summary>Wraps the value.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The wrapped value.</returns>
        public T Wrap(T value) => value;

        /// <summary>Takes a list of items.</summary>
        /// <param name="items">The items.</param>
        public int Take(List<T> items) => items.Count;

        /// <summary>Indexer summary.</summary>
        /// <param name="index">The index.</param>
        public T this[T index] => index;
    }

    /// <summary>Outer summary.</summary>
    public class Outer
    {
        /// <summary>Nested type summary.</summary>
        public class Inner { }
    }
}
