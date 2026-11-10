// Тестовая «прослойка» с API Microsoft.Data.Sqlite поверх настоящей нативной SQLite (libsqlite3).
// Нужна только для проверки логики в песочнице, где нельзя скачать NuGet-пакет. В проект не входит.
using System.Runtime.InteropServices;
using System.Text;
namespace Microsoft.Data.Sqlite;

public class SqliteException : Exception { public SqliteException(string m) : base(m) { } }

internal static class N
{
    const string L = "libsqlite3.so.0";
    [DllImport(L)] public static extern int sqlite3_open_v2(byte[] f, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(L)] public static extern int sqlite3_close_v2(IntPtr db);
    [DllImport(L)] public static extern int sqlite3_prepare_v2(IntPtr db, IntPtr sql, int n, out IntPtr stmt, out IntPtr tail);
    [DllImport(L)] public static extern int sqlite3_step(IntPtr s);
    [DllImport(L)] public static extern int sqlite3_finalize(IntPtr s);
    [DllImport(L)] public static extern int sqlite3_column_count(IntPtr s);
    [DllImport(L)] public static extern IntPtr sqlite3_column_name(IntPtr s, int i);
    [DllImport(L)] public static extern int sqlite3_column_type(IntPtr s, int i);
    [DllImport(L)] public static extern long sqlite3_column_int64(IntPtr s, int i);
    [DllImport(L)] public static extern double sqlite3_column_double(IntPtr s, int i);
    [DllImport(L)] public static extern IntPtr sqlite3_column_text(IntPtr s, int i);
    [DllImport(L)] public static extern int sqlite3_bind_parameter_index(IntPtr s, byte[] name);
    [DllImport(L)] public static extern int sqlite3_bind_null(IntPtr s, int i);
    [DllImport(L)] public static extern int sqlite3_bind_int64(IntPtr s, int i, long v);
    [DllImport(L)] public static extern int sqlite3_bind_double(IntPtr s, int i, double v);
    [DllImport(L)] public static extern int sqlite3_bind_text(IntPtr s, int i, byte[] t, int n, IntPtr d);
    [DllImport(L)] public static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport(L)] public static extern int sqlite3_changes(IntPtr db);
    public static byte[] Z(string s) => Encoding.UTF8.GetBytes(s + "\0");
    public static string Str(IntPtr p) => Marshal.PtrToStringUTF8(p) ?? "";
}

public class SqliteParameterCollection
{
    internal readonly List<(string Name, object Value)> Items = new();
    public object AddWithValue(string n, object v) { Items.Add((n, v)); return v; }
}

public class SqliteDataReader : IDisposable
{
    private readonly IntPtr _db, _st; private bool _done;
    internal SqliteDataReader(IntPtr db, IntPtr st) { _db = db; _st = st; }
    public int FieldCount => N.sqlite3_column_count(_st);
    public string GetName(int i) => N.Str(N.sqlite3_column_name(_st, i));
    public bool Read()
    {
        if (_done) return false;
        int rc = N.sqlite3_step(_st);
        if (rc == 100) return true;
        _done = true;
        if (rc == 101) return false;
        throw new SqliteException($"SQLite Error {rc}: '{N.Str(N.sqlite3_errmsg(_db))}'.");
    }
    public object GetValue(int i) => N.sqlite3_column_type(_st, i) switch
    {
        1 => N.sqlite3_column_int64(_st, i),
        2 => N.sqlite3_column_double(_st, i),
        5 => DBNull.Value,
        _ => N.Str(N.sqlite3_column_text(_st, i)),
    };
    public void Dispose() { N.sqlite3_finalize(_st); }
}

public class SqliteTransaction : IDisposable
{
    private readonly SqliteConnection _c; private bool _open = true;
    internal SqliteTransaction(SqliteConnection c) { _c = c; _c.Run("BEGIN"); }
    public void Commit() { _c.Run("COMMIT"); _open = false; }
    public void Rollback() { if (_open) { _c.Run("ROLLBACK"); _open = false; } }
    public void Dispose() { if (_open) { _c.Run("ROLLBACK"); _open = false; } }
}

public class SqliteCommand : IDisposable
{
    private readonly SqliteConnection _c;
    internal SqliteCommand(SqliteConnection c) { _c = c; }
    public string CommandText { get; set; } = "";
    public SqliteTransaction? Transaction { get; set; }
    public SqliteParameterCollection Parameters { get; } = new();

    private void Bind(IntPtr st)
    {
        foreach (var (name, v) in Parameters.Items)
        {
            int idx = N.sqlite3_bind_parameter_index(st, N.Z(name));
            if (idx == 0) continue;
            switch (v)
            {
                case null: case DBNull: N.sqlite3_bind_null(st, idx); break;
                case bool b: N.sqlite3_bind_int64(st, idx, b ? 1 : 0); break;
                case int or long or short or byte: N.sqlite3_bind_int64(st, idx, Convert.ToInt64(v)); break;
                case double or float or decimal: N.sqlite3_bind_double(st, idx, Convert.ToDouble(v)); break;
                default: { var t = Encoding.UTF8.GetBytes(Convert.ToString(v) ?? ""); N.sqlite3_bind_text(st, idx, t, t.Length, new IntPtr(-1)); break; }
            }
        }
    }

    public int ExecuteNonQuery()
    {
        IntPtr buf = Marshal.StringToCoTaskMemUTF8(CommandText);
        try
        {
            IntPtr cur = buf;
            while (Marshal.ReadByte(cur) != 0)
            {
                int rc = N.sqlite3_prepare_v2(_c.Db, cur, -1, out IntPtr st, out IntPtr tail);
                if (rc != 0) throw new SqliteException($"SQLite Error {rc}: '{N.Str(N.sqlite3_errmsg(_c.Db))}'.");
                if (st != IntPtr.Zero)
                {
                    Bind(st);
                    int s;
                    do { s = N.sqlite3_step(st); } while (s == 100);
                    N.sqlite3_finalize(st);
                    if (s != 101) throw new SqliteException($"SQLite Error {s}: '{N.Str(N.sqlite3_errmsg(_c.Db))}'.");
                }
                cur = tail;
            }
            return N.sqlite3_changes(_c.Db);
        }
        finally { Marshal.FreeCoTaskMem(buf); }
    }

    public SqliteDataReader ExecuteReader()
    {
        IntPtr buf = Marshal.StringToCoTaskMemUTF8(CommandText);
        try
        {
            int rc = N.sqlite3_prepare_v2(_c.Db, buf, -1, out IntPtr st, out _);
            if (rc != 0) throw new SqliteException($"SQLite Error {rc}: '{N.Str(N.sqlite3_errmsg(_c.Db))}'.");
            Bind(st);
            return new SqliteDataReader(_c.Db, st);
        }
        finally { Marshal.FreeCoTaskMem(buf); }
    }

    public object? ExecuteScalar()
    {
        using var r = ExecuteReader();
        return r.Read() ? r.GetValue(0) : null;
    }
    public void Dispose() { }
}

public class SqliteConnection : IDisposable
{
    private readonly string _cs; internal IntPtr Db;
    public SqliteConnection(string cs) { _cs = cs; }
    public void Open()
    {
        string path = _cs.Split(';')[0].Replace("Data Source=", "");
        int rc = N.sqlite3_open_v2(N.Z(path), out Db, 6 /*READWRITE|CREATE*/, IntPtr.Zero);
        if (rc != 0) throw new SqliteException("open failed " + rc);
        if (_cs.Contains("Foreign Keys=True")) Run("PRAGMA foreign_keys = ON");
    }
    internal void Run(string sql) { using var c = new SqliteCommand(this) { CommandText = sql }; c.ExecuteNonQuery(); }
    public SqliteCommand CreateCommand() => new(this);
    public SqliteTransaction BeginTransaction() => new(this);
    public static void ClearAllPools() { }
    public void Dispose() { if (Db != IntPtr.Zero) { N.sqlite3_close_v2(Db); Db = IntPtr.Zero; } }
}
