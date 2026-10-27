using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace FastReflection.ObjectReader
{
    /// <summary>
    /// Reads an <see cref="IEnumerable"/> source as a forward-only <see cref="DbDataReader"/> for the
    /// <see cref="DataTable.Load(IDataReader)"/> and SqlBulkCopy path. Each column is one
    /// <see cref="Member"/> of the source element type's public <see cref="TypeAccessor"/>; values are
    /// read through that accessor. Obtain an instance only through <see cref="Create{T}"/> or
    /// <see cref="Create(Type, IEnumerable, string[])"/>.
    /// </summary>
    public sealed class ObjectReader : DbDataReader
    {
        // Mirrors the core's TypeAccessor.Create discovery union (internal there) — the
        // element type is forwarded to that entry point. Keep in sync; the AotSmoke
        // object-reader scenario is the parity check.
        private const DynamicallyAccessedMemberTypes DiscoveryMemberTypes =
            DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties
            | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields
            | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods
            | DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors
            | DynamicallyAccessedMemberTypes.Interfaces;

        private readonly struct Column
        {
            internal readonly string Name;
            internal readonly int MemberOrdinal;
            // Carries the BCL DbDataReader.GetFieldType contract for the stored column type.
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)]
            internal readonly Type FieldType;
            internal readonly bool AllowDbNull;

            internal Column(string name, int memberOrdinal,
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)]
                Type fieldType, bool allowDbNull)
            {
                Name = name;
                MemberOrdinal = memberOrdinal;
                FieldType = fieldType;
                AllowDbNull = allowDbNull;
            }
        }

        private readonly TypeAccessor _accessor;
        private readonly IEnumerator _rows;
        private readonly Column[] _columns;
        private readonly Dictionary<string, int> _nameToColumn;

        private bool _closed;
        private bool _exhausted;
        private bool _onRow;
        private bool _hasPendingRow;
        private object? _pendingRow;
        private object? _current;

        private ObjectReader(TypeAccessor accessor, IEnumerable source, Column[] columns)
        {
            _accessor = accessor;
            _rows = source.GetEnumerator();
            _columns = columns;
            _nameToColumn = new Dictionary<string, int>(columns.Length, StringComparer.Ordinal);
            for (var i = 0; i < columns.Length; i++)
            {
                // A duplicated column name resolves to the first column carrying it, matching the
                // ADO GetOrdinal convention.
                if (!_nameToColumn.ContainsKey(columns[i].Name))
                    _nameToColumn.Add(columns[i].Name, i);
            }
        }

        /// <summary>
        /// Creates a reader over <paramref name="source"/> with one column per supplied member name, in
        /// the order supplied — or, when no names are given, one column per <see cref="Member"/> of
        /// <typeparamref name="T"/>'s public <see cref="TypeAccessor"/> in accessor order.
        /// </summary>
        /// <typeparam name="T">The element type whose public accessor supplies the columns.</typeparam>
        /// <param name="source">The rows to read.</param>
        /// <param name="members">Case-sensitive member names to expose as columns, in column order.</param>
        /// <returns>The reader, positioned before the first row.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
        /// <exception cref="MissingMemberException">A supplied name is not a Member of <typeparamref name="T"/>'s public type accessor.</exception>
        public static ObjectReader Create<[DynamicallyAccessedMembers(DiscoveryMemberTypes)] T>(IEnumerable<T> source, params string[] members)
            => Create(typeof(T), source, members);

        /// <summary>
        /// Creates a reader over <paramref name="source"/> with one column per supplied member name, in
        /// the order supplied — or, when no names are given, one column per <see cref="Member"/> of
        /// <paramref name="elementType"/>'s public <see cref="TypeAccessor"/> in accessor order.
        /// </summary>
        /// <param name="elementType">The element type whose public accessor supplies the columns.</param>
        /// <param name="source">The rows to read.</param>
        /// <param name="members">Case-sensitive member names to expose as columns, in column order.</param>
        /// <returns>The reader, positioned before the first row.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="elementType"/> or <paramref name="source"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="elementType"/> is an open generic.</exception>
        /// <exception cref="MissingMemberException">A supplied name is not a Member of <paramref name="elementType"/>'s public type accessor.</exception>
        [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Column types come from the member signatures of the caller-rooted element type — the union keeps each Type reachable but cannot transitively root a column type's own PublicProperties/PublicFields, so GetFieldType's BCL contract is over-promised for user-typed columns (inherent to any user-typed DbDataReader).")]
        public static ObjectReader Create(
            [DynamicallyAccessedMembers(DiscoveryMemberTypes)]
            Type elementType, IEnumerable source, params string[] members)
        {
            if (elementType == null) throw new ArgumentNullException(nameof(elementType));
            if (source == null) throw new ArgumentNullException(nameof(source));
            var accessor = TypeAccessor.Create(elementType);
            var allMembers = accessor.GetMembers();
            var names = members is { Length: > 0 } ? members : AllNames(allMembers);
            var columns = new Column[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                if (!accessor.TryGetOrdinal(names[i], out var memberOrdinal))
                    throw new MissingMemberException(elementType.FullName, names[i]);
                var memberType = allMembers[memberOrdinal].Type;
                var underlying = Nullable.GetUnderlyingType(memberType);
                columns[i] = new Column(
                    names[i],
                    memberOrdinal,
                    underlying ?? memberType,
                    !memberType.IsValueType || underlying != null);
            }

            return new ObjectReader(accessor, source, columns);
        }

        private static string[] AllNames(IReadOnlyList<Member> members)
        {
            var names = new string[members.Count];
            for (var i = 0; i < members.Count; i++)
                names[i] = members[i].Name;
            return names;
        }

        /// <inheritdoc />
        public override int FieldCount => _columns.Length;

        /// <inheritdoc />
        public override bool HasRows
        {
            get
            {
                if (_closed || _exhausted) return false;
                if (_onRow || _hasPendingRow) return true;
                if (_rows.MoveNext())
                {
                    _pendingRow = _rows.Current;
                    _hasPendingRow = true;
                    return true;
                }

                _exhausted = true;
                return false;
            }
        }

        /// <inheritdoc />
        public override bool IsClosed => _closed;

        /// <inheritdoc />
        public override int RecordsAffected => -1;

        /// <inheritdoc />
        public override int Depth => 0;

        /// <inheritdoc />
        public override object this[int ordinal] => GetValue(ordinal);

        /// <inheritdoc />
        public override object this[string name] => GetValue(GetOrdinal(name));

        /// <inheritdoc />
        public override bool Read()
        {
            ThrowIfClosed();
            if (_hasPendingRow)
            {
                _hasPendingRow = false;
                _current = _pendingRow;
                _pendingRow = null;
                _onRow = true;
                return true;
            }

            if (_exhausted) return false;
            if (_rows.MoveNext())
            {
                _current = _rows.Current;
                _onRow = true;
                return true;
            }

            _exhausted = true;
            _onRow = false;
            _current = null;
            return false;
        }

        /// <inheritdoc />
        public override bool NextResult() => false;

        /// <inheritdoc />
        public override void Close()
        {
            if (_closed) return;
            _closed = true;
            _onRow = false;
            _hasPendingRow = false;
            _pendingRow = null;
            _current = null;
            (_rows as IDisposable)?.Dispose();
        }

        /// <inheritdoc />
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override IEnumerator GetEnumerator() => new DbEnumerator(this);

        /// <inheritdoc />
        public override string GetName(int ordinal) => _columns[ordinal].Name;

        /// <inheritdoc />
        public override int GetOrdinal(string name)
            => _nameToColumn.TryGetValue(name, out var ordinal)
                ? ordinal
                : throw new IndexOutOfRangeException($"Column '{name}' does not exist.");

        /// <inheritdoc />
        [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)]
        public override Type GetFieldType(int ordinal) => _columns[ordinal].FieldType;

        /// <inheritdoc />
        public override string GetDataTypeName(int ordinal) => _columns[ordinal].FieldType.Name;

        /// <inheritdoc />
        public override object GetValue(int ordinal)
            => ReadValue(ordinal) ?? DBNull.Value;

        /// <inheritdoc />
        public override int GetValues(object[] values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var count = Math.Min(values.Length, _columns.Length);
            for (var i = 0; i < count; i++)
                values[i] = GetValue(i);
            return count;
        }

        /// <inheritdoc />
        public override bool IsDBNull(int ordinal) => ReadValue(ordinal) is null;

        /// <inheritdoc />
        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);

        /// <inheritdoc />
        public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);

        /// <inheritdoc />
        public override char GetChar(int ordinal) => (char)GetValue(ordinal);

        /// <inheritdoc />
        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);

        /// <inheritdoc />
        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);

        /// <inheritdoc />
        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);

        /// <inheritdoc />
        public override float GetFloat(int ordinal) => (float)GetValue(ordinal);

        /// <inheritdoc />
        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);

        /// <inheritdoc />
        public override short GetInt16(int ordinal) => (short)GetValue(ordinal);

        /// <inheritdoc />
        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);

        /// <inheritdoc />
        public override long GetInt64(int ordinal) => (long)GetValue(ordinal);

        /// <inheritdoc />
        public override string GetString(int ordinal) => (string)GetValue(ordinal);

        /// <inheritdoc />
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        {
            if (GetValue(ordinal) is not byte[] bytes) throw new InvalidCastException();
            var available = SliceLength(bytes.Length, dataOffset);
            if (buffer == null) return available;
            var count = Math.Min(available, length);
            Buffer.BlockCopy(bytes, (int)dataOffset, buffer, bufferOffset, count);
            return count;
        }

        /// <inheritdoc />
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        {
            var value = GetValue(ordinal);
            if (value is not (char[] or string)) throw new InvalidCastException();
            var total = value is string s ? s.Length : ((char[])value).Length;
            var available = SliceLength(total, dataOffset);
            if (buffer == null) return available;
            var count = Math.Min(available, length);
            if (value is string str)
                str.CopyTo((int)dataOffset, buffer, bufferOffset, count);
            else
                Array.Copy((char[])value, dataOffset, buffer, bufferOffset, count);
            return count;
        }

        /// <inheritdoc />
        [UnconditionalSuppressMessage("Trimming", "IL2111", Justification = "The DataType column is typed as System.Type — a runtime BCL type whose members are always rooted; DataColumn's reflective probe of dataType (its TypeInitializer) is safe against it.")]
        public override DataTable? GetSchemaTable()
        {
            ThrowIfClosed();
            var table = new DataTable("SchemaTable");
            table.Columns.Add("ColumnOrdinal", typeof(int));
            table.Columns.Add("ColumnName", typeof(string));
            table.Columns.Add("DataType", typeof(Type));
            table.Columns.Add("ColumnSize", typeof(int));
            table.Columns.Add("AllowDBNull", typeof(bool));
            for (var i = 0; i < _columns.Length; i++)
            {
                var row = table.NewRow();
                row["ColumnOrdinal"] = i;
                row["ColumnName"] = _columns[i].Name;
                row["DataType"] = _columns[i].FieldType;
                row["ColumnSize"] = -1;
                row["AllowDBNull"] = _columns[i].AllowDbNull;
                table.Rows.Add(row);
            }

            return table;
        }

        private object? ReadValue(int ordinal)
        {
            ThrowIfClosed();
            if ((uint)ordinal >= (uint)_columns.Length) throw new IndexOutOfRangeException();
            if (!_onRow) throw new InvalidOperationException("Invalid attempt to read when no data is present.");
            return _accessor.GetValue(_current, _columns[ordinal].MemberOrdinal);
        }

        private void ThrowIfClosed()
        {
            if (_closed) throw new InvalidOperationException("Invalid attempt to read when the reader is closed.");
        }

        private static int SliceLength(int total, long dataOffset)
        {
            var offset = checked((int)dataOffset);
            if ((uint)offset > (uint)total) throw new ArgumentOutOfRangeException(nameof(dataOffset));
            return total - offset;
        }
    }
}
