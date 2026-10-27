namespace FastReflection.XmlDocs.Tests
{
    /// <summary>Person summary text.</summary>
    /// <remarks>Person remarks text.</remarks>
    /// <example>Person example text.</example>
    public class Person
    {
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
