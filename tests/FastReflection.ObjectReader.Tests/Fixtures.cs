namespace FastReflection.ObjectReader.Tests
{
    public class Person
    {
        [Ordinal(1)] public string? Name { get; set; }
        [Ordinal(0)] public int Id { get; set; }
        public int? Score { get; set; }
        public bool Active { get; set; }
        public DateTime Born { get; set; }
        public int Age;
        public string ReadOnly => "ro";
        internal string? Secret { get; set; }
        public static int Counter;

        public Person() { }
        public Person(int id, string? name, int? score = null, bool active = true)
        {
            Id = id;
            Name = name;
            Score = score;
            Active = active;
        }
    }

    public class Student : Person
    {
        public string? School { get; set; }
    }
}
