using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace LibrarySystem.Data;

/// <summary>
/// Доступ к базе данных SQLite. Все значения передаются параметрами (@имя),
/// а не склеиваются в строку SQL — это защита от SQL-инъекций.
/// </summary>
public static class Database
{
    private static SqliteConnection? _conn;
    private static SqliteTransaction? _tx;

    /// <summary>Папка данных: %LocalAppData%\LibrarySystem (туда можно писать без прав администратора).</summary>
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibrarySystem");

    public static string DbPath => Path.Combine(DataDir, "library.db");
    public static string BackupDir => Path.Combine(DataDir, "Backups");

    /// <summary>Открывает БД; если файла ещё нет — создаёт его из Data\schema.sql (таблицы + тестовые данные).</summary>
    public static void Initialize()
    {
        Directory.CreateDirectory(DataDir);
        bool isNew = !File.Exists(DbPath);
        _conn = new SqliteConnection($"Data Source={DbPath};Foreign Keys=True");
        _conn.Open();
        if (isNew) CreateFromSchema();
        ApplyUpgrades();                // обновляет и новую, и уже существующую базу до текущей версии схемы
    }

    /// <summary>Текущая версия схемы. Хранится в самой базе (PRAGMA user_version).</summary>
    public const int SchemaVersion = 2;

    /// <summary>Применяет недостающие обновления схемы: триггеры, представления, история изменений.</summary>
    private static void ApplyUpgrades()
    {
        long version = Scalar<long>("PRAGMA user_version");
        if (version < 2)
        {
            string script = Path.Combine(AppContext.BaseDirectory, "Data", "upgrade_v2.sql");
            if (!File.Exists(script))
                throw new FileNotFoundException("Не найден файл обновления схемы базы данных.", script);
            ExecuteScript(File.ReadAllText(script, Encoding.UTF8));
            ExecuteScript("PRAGMA user_version = 2;");
        }
    }

    private static void CreateFromSchema()
    {
        try
        {
            string schema = Path.Combine(AppContext.BaseDirectory, "Data", "schema.sql");
            if (!File.Exists(schema))
                throw new FileNotFoundException("Не найден файл схемы базы данных.", schema);
            ExecuteScript(File.ReadAllText(schema, Encoding.UTF8));
        }
        catch
        {
            // неудачная инициализация не должна оставлять «полупустую» базу
            _conn.Dispose();
            _conn = null;
            SqliteConnection.ClearAllPools();
            File.Delete(DbPath);
            throw;
        }
    }

    public static void ExecuteScript(string script)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = script;
        cmd.ExecuteNonQuery();
    }

    private static SqliteCommand Create(string sql, (string Name, object? Value)[] args)
    {
        var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = _tx;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    /// <summary>Выполняет INSERT/UPDATE/DELETE, возвращает число затронутых строк.</summary>
    public static int Execute(string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Create(sql, args);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Выполняет SELECT и возвращает таблицу (типы столбцов определяются по данным).</summary>
    public static DataTable Query(string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Create(sql, args);
        using var reader = cmd.ExecuteReader();

        int n = reader.FieldCount;
        var names = new string[n];
        for (int i = 0; i < n; i++) names[i] = reader.GetName(i);

        var raw = new List<object[]>();
        while (reader.Read())
        {
            var row = new object[n];
            for (int i = 0; i < n; i++) row[i] = reader.GetValue(i);
            raw.Add(row);
        }

        var table = new DataTable();
        for (int i = 0; i < n; i++)
        {
            Type type = typeof(string);
            foreach (var row in raw)
            {
                if (row[i] is DBNull) continue;
                type = row[i] switch { long => typeof(long), double => typeof(double), _ => typeof(string) };
                break;
            }
            table.Columns.Add(names[i], type);
        }

        foreach (var row in raw)
        {
            var dr = table.NewRow();
            for (int i = 0; i < n; i++)
                dr[i] = row[i] is DBNull
                    ? DBNull.Value
                    : Convert.ChangeType(row[i], table.Columns[i].DataType, CultureInfo.InvariantCulture);
            table.Rows.Add(dr);
        }
        return table;
    }

    /// <summary>Возвращает первое значение первой строки (или default, если пусто / NULL).</summary>
    public static T? Scalar<T>(string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Create(sql, args);
        object? o = cmd.ExecuteScalar();
        if (o is null || o is DBNull) return default;
        return (T)Convert.ChangeType(o, typeof(T), CultureInfo.InvariantCulture);
    }

    /// <summary>Выполняет набор операций как одну транзакцию: либо всё, либо ничего.</summary>
    public static void InTransaction(Action work)
    {
        if (_tx != null) { work(); return; }
        using var tx = _conn!.BeginTransaction();
        _tx = tx;
        try
        {
            work();
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
        finally
        {
            _tx = null;
        }
    }

    /// <summary>Резервная копия: VACUUM INTO создаёт целостную копию базы в папке Backups.</summary>
    public static string Backup()
    {
        Directory.CreateDirectory(BackupDir);
        string file = Path.Combine(BackupDir, $"library_{DateTime.Now:yyyyMMdd_HHmmss}.db");
        Execute("VACUUM INTO @path", ("@path", file));
        return file;
    }
}
