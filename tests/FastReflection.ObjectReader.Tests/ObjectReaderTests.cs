using System.Collections;
using System.Data;
using System.Data.Common;
using System.Reflection;
using Xunit;

namespace FastReflection.ObjectReader.Tests
{
    public class ObjectReaderTests
    {
        private static Person[] ThreePeople() => new[]
        {
            new Person(1, "alice", 10, true) { Age = 30, Born = new DateTime(1995, 1, 2) },
            new Person(2, null, null, false) { Age = 25 },
            new Student { Id = 3, Name = "carol", Score = 7, Active = true, Age = 20, School = "state" },
        };

        [Fact]
        public void Shape_SealedWithNoPublicCtor()
        {
            var type = typeof(ObjectReader);
            Assert.True(type.IsSealed);
            Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        }

        [Fact]
        public void Create_SuppliedMembers_YieldColumnsInSuppliedOrder()
        {
            using var reader = ObjectReader.Create(ThreePeople(), "Name", "Age", "Id");

            Assert.Equal(3, reader.FieldCount);
            Assert.Equal("Name", reader.GetName(0));
            Assert.Equal("Age", reader.GetName(1));
            Assert.Equal("Id", reader.GetName(2));
            Assert.Equal(1, reader.GetOrdinal("Age"));
            Assert.Equal(typeof(string), reader.GetFieldType(0));
            Assert.Equal(typeof(int), reader.GetFieldType(1));
        }

        [Fact]
        public void Create_NoMembers_YieldsAllPublicMembersInAccessorOrder()
        {
            using var reader = ObjectReader.Create(ThreePeople());

            var accessor = TypeAccessor.Create(typeof(Person));
            var members = accessor.GetMembers();
            Assert.Equal(members.Count, reader.FieldCount);
            for (var i = 0; i < members.Count; i++)
                Assert.Equal(members[i].Name, reader.GetName(i));

            // OrdinalAttribute orders Id (0) before Name (1) ahead of unattributed members.
            Assert.Equal("Id", reader.GetName(0));
            Assert.Equal("Name", reader.GetName(1));
        }

        [Fact]
        public void Create_NoMembers_ExcludesNonPublicAndStatic()
        {
            using var reader = ObjectReader.Create(ThreePeople());

            var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            Assert.DoesNotContain("Secret", names);
            Assert.DoesNotContain("Counter", names);
        }

        [Fact]
        public void Create_UnknownMember_ThrowsMissingMember()
        {
            Assert.Throws<MissingMemberException>(() => ObjectReader.Create(ThreePeople(), "Nope"));
            Assert.Throws<MissingMemberException>(() => ObjectReader.Create(ThreePeople(), "name")); // case-sensitive
            Assert.Throws<MissingMemberException>(() => ObjectReader.Create(ThreePeople(), "Secret")); // non-public is missing
        }

        [Fact]
        public void Create_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => ObjectReader.Create<Person>(null!));
            Assert.Throws<ArgumentNullException>(() => ObjectReader.Create(null!, ThreePeople()));
        }

        [Fact]
        public void Create_EmptyOrNullMembersArray_YieldsAllMembers()
        {
            using var empty = ObjectReader.Create(ThreePeople(), Array.Empty<string>());
            using var nul = ObjectReader.Create(ThreePeople(), (string[])null!);

            var accessor = TypeAccessor.Create(typeof(Person));
            Assert.Equal(accessor.GetMembers().Count, empty.FieldCount);
            Assert.Equal(accessor.GetMembers().Count, nul.FieldCount);
        }

        [Fact]
        public void Create_TypeOverload_ReadsNonGenericSource()
        {
            using var reader = ObjectReader.Create(typeof(Person), (IEnumerable)ThreePeople(), "Id");

            Assert.Equal(1, reader.FieldCount);
            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetValue(0));
        }

        [Fact]
        public void Read_SubtypeRows_ReadThroughBaseAccessor()
        {
            using var reader = ObjectReader.Create(ThreePeople(), "Id", "Name");

            var ids = new List<object>();
            while (reader.Read())
                ids.Add(reader.GetValue(0));
            Assert.Equal(new object[] { 1, 2, 3 }, ids);
        }

        [Fact]
        public void GetValue_NullMember_ReadsAsDBNull()
        {
            using var reader = ObjectReader.Create(ThreePeople(), "Name", "Score");

            Assert.True(reader.Read()); // alice/10
            Assert.True(reader.Read()); // null/null
            Assert.Same(DBNull.Value, reader.GetValue(0));
            Assert.Same(DBNull.Value, reader.GetValue(1));
            Assert.True(reader.IsDBNull(0));
            Assert.True(reader.IsDBNull(1));
        }

        [Fact]
        public void TypedGetters_ReadValuesAndThrowOnMismatch()
        {
            using var reader = ObjectReader.Create(ThreePeople(), "Id", "Name", "Active", "Born", "Score");

            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal("alice", reader.GetString(1));
            Assert.True(reader.GetBoolean(2));
            Assert.Equal(new DateTime(1995, 1, 2), reader.GetDateTime(3));
            Assert.Equal(10, reader.GetInt32(4));
            Assert.Throws<InvalidCastException>(() => reader.GetString(0));

            Assert.True(reader.Read());
            Assert.Throws<InvalidCastException>(() => reader.GetString(1)); // DBNull is not a string
        }

        [Fact]
        public void GetValues_FillsArrayUpToFieldCount()
        {
            using var reader = ObjectReader.Create(ThreePeople(), "Id", "Name");
            Assert.True(reader.Read());

            var values = new object[4];
            Assert.Equal(2, reader.GetValues(values));
            Assert.Equal(new object?[] { 1, "alice", null, null }, values);
        }

        [Fact]
        public void Indexer_ByOrdinalAndName()
        {
            using var reader = ObjectReader.Create(ThreePeople(), "Id", "Name");
            Assert.True(reader.Read());

            Assert.Equal(1, reader[0]);
            Assert.Equal("alice", reader["Name"]);
        }

        [Fact]
        public void GetOrdinal_CaseSensitiveMiss_Throws()
        {
            using var reader = ObjectReader.Create(ThreePeople(), "Name");

            Assert.Equal(0, reader.GetOrdinal("Name"));
            Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("name"));
        }

        [Fact]
        public void GetFieldType_UnwrapsNullable()
        {
            using var reader = ObjectReader.Create(ThreePeople(), "Score");

            Assert.Equal(typeof(int), reader.GetFieldType(0));
            Assert.Equal("Int32", reader.GetDataTypeName(0));
        }

        [Fact]
        public void GetSchemaTable_ReportsFiveSpecifiedColumns()
        {
            using var reader = ObjectReader.Create(ThreePeople(), "Id", "Name", "Score", "Active");
            var schema = reader.GetSchemaTable();

            Assert.NotNull(schema);
            Assert.Equal(new[] { "ColumnOrdinal", "ColumnName", "DataType", "ColumnSize", "AllowDBNull" },
                schema!.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray());
            Assert.Equal(4, schema.Rows.Count);
            Assert.Equal(1, schema.Rows[1]["ColumnOrdinal"]);
            Assert.Equal("Name", schema.Rows[1]["ColumnName"]);
            Assert.Equal(typeof(int), schema.Rows[2]["DataType"]); // Nullable<int> unwraps
            Assert.Equal(true, schema.Rows[1]["AllowDBNull"]);
            Assert.Equal(true, schema.Rows[2]["AllowDBNull"]);
            Assert.Equal(false, schema.Rows[3]["AllowDBNull"]);
        }

        [Fact]
        public void DataTable_Load_EndToEnd()
        {
            var table = new DataTable();
            using (var reader = ObjectReader.Create(ThreePeople(), "Id", "Name", "Score", "Active"))
                table.Load(reader);

            Assert.Equal(new[] { "Id", "Name", "Score", "Active" },
                table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray());
            Assert.Equal(3, table.Rows.Count);
            Assert.Equal(1, table.Rows[0]["Id"]);
            Assert.Equal("alice", table.Rows[0]["Name"]);
            Assert.Same(DBNull.Value, table.Rows[1]["Name"]);
            Assert.Same(DBNull.Value, table.Rows[1]["Score"]);
            Assert.Equal(3, table.Rows[2]["Id"]); // Student row reads through the Person accessor
        }

        [Fact]
        public void Enumerator_YieldsDbEnumeratorRows()
        {
            using var reader = ObjectReader.Create(ThreePeople(), "Id");

            var enumerator = reader.GetEnumerator();
            Assert.IsType<DbEnumerator>(enumerator);

            var ids = new List<object>();
            while (enumerator.MoveNext())
                ids.Add(((IDataRecord)enumerator.Current).GetValue(0));
            Assert.Equal(new object[] { 1, 2, 3 }, ids);
        }

        [Fact]
        public void HasRows_PeeksWithoutConsuming()
        {
            using var reader = ObjectReader.Create(ThreePeople(), "Id");

            Assert.True(reader.HasRows);
            Assert.True(reader.Read()); // the peeked row is not skipped
            Assert.Equal(1, reader.GetValue(0));
            Assert.True(reader.Read());
            Assert.True(reader.Read());
            Assert.True(reader.HasRows); // still positioned on a row
            Assert.False(reader.Read());
            Assert.False(reader.HasRows);
        }

        [Fact]
        public void HasRows_EmptySource_False()
        {
            using var reader = ObjectReader.Create(Array.Empty<Person>(), "Id");

            Assert.False(reader.HasRows);
            Assert.False(reader.Read());
        }

        [Fact]
        public void Read_BeforeFirstPosition_GetValueThrows()
        {
            using var reader = ObjectReader.Create(ThreePeople(), "Id");

            Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
            while (reader.Read()) { } // exhaust
            Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
        }

        [Fact]
        public void Close_EndsTheReader()
        {
            var reader = ObjectReader.Create(ThreePeople(), "Id");

            Assert.False(reader.IsClosed);
            Assert.True(reader.Read());
            reader.Close();

            Assert.True(reader.IsClosed);
            Assert.False(reader.HasRows);
            Assert.Throws<InvalidOperationException>(() => reader.Read());
            Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
        }

        [Fact]
        public void TypedGetters_RemainingTypes_ReadValues()
        {
            var guid = Guid.NewGuid();
            var items = new[]
            {
                new Scalars { Byte = 0x7F, Char = 'x', Dec = 1.5m, Dbl = 2.5, Flt = 3.5f, G = guid, I16 = -12, I64 = long.MaxValue },
            };
            using var reader = ObjectReader.Create(items, "Byte", "Char", "Dec", "Dbl", "Flt", "G", "I16", "I64");

            Assert.True(reader.Read());
            Assert.Equal(0x7F, reader.GetByte(0));
            Assert.Equal('x', reader.GetChar(1));
            Assert.Equal(1.5m, reader.GetDecimal(2));
            Assert.Equal(2.5, reader.GetDouble(3));
            Assert.Equal(3.5f, reader.GetFloat(4));
            Assert.Equal(guid, reader.GetGuid(5));
            Assert.Equal((short)-12, reader.GetInt16(6));
            Assert.Equal(long.MaxValue, reader.GetInt64(7));
        }

        [Fact]
        public void GetBytes_ReadsSlicesAndRejectsNonByteColumns()
        {
            var items = new[] { new Scalars { Bytes = new byte[] { 1, 2, 3, 4, 5 }, Byte = 9 } };
            using var reader = ObjectReader.Create(items, "Bytes", "Byte");

            Assert.True(reader.Read());
            Assert.Equal(5, reader.GetBytes(0, 0, null, 0, 0));

            var buffer = new byte[3];
            Assert.Equal(3, reader.GetBytes(0, 1, buffer, 0, 3));
            Assert.Equal(new byte[] { 2, 3, 4 }, buffer);

            Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, 6, null, 0, 0));
            Assert.Throws<InvalidCastException>(() => reader.GetBytes(1, 0, null, 0, 0));
        }

        [Fact]
        public void GetChars_ReadsCharArrayAndStringAndRejectsOtherColumns()
        {
            var items = new[] { new Scalars { Chars = new[] { 'a', 'b', 'c', 'd' }, Str = "wxyz", Byte = 9 } };
            using var reader = ObjectReader.Create(items, "Chars", "Str", "Byte");

            Assert.True(reader.Read());
            Assert.Equal(4, reader.GetChars(0, 0, null, 0, 0));

            var slice = new char[2];
            Assert.Equal(2, reader.GetChars(0, 1, slice, 0, 2));
            Assert.Equal(new[] { 'b', 'c' }, slice);

            Assert.Equal(4, reader.GetChars(1, 0, null, 0, 0));
            var text = new char[4];
            Assert.Equal(4, reader.GetChars(1, 0, text, 0, 4));
            Assert.Equal("wxyz", new string(text));

            Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetChars(0, 9, null, 0, 0));
            Assert.Throws<InvalidCastException>(() => reader.GetChars(2, 0, null, 0, 0));
        }

        [Fact]
        public void Metadata_Statics()
        {
            using var reader = ObjectReader.Create(ThreePeople(), "Id");

            Assert.Equal(0, reader.Depth);
            Assert.Equal(-1, reader.RecordsAffected);
            Assert.False(reader.NextResult());
        }
    }
}
