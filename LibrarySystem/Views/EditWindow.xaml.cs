using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LibrarySystem.Data;
using LibrarySystem.Pages;
using LibrarySystem.Services;

namespace LibrarySystem.Views;

/// <summary>Окно добавления / редактирования записи. Поля строятся по описанию (FieldDef).</summary>
public partial class EditWindow : Window
{
    private readonly string _table;
    private readonly IReadOnlyList<FieldDef> _fields;
    private readonly long? _id;
    private readonly List<FrameworkElement> _inputs = new();

    public EditWindow(string title, string table, IReadOnlyList<FieldDef> fields, long? id)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        Title = title;
        _table = table;
        _fields = fields;
        _id = id;

        Form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

        for (int i = 0; i < fields.Count; i++)
        {
            FieldDef f = fields[i];
            Form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = f.Required ? f.Caption + " *" : f.Caption,
                Style = (Style)FindResource("Label"),
                Margin = new Thickness(0, 0, 18, 14),
            };
            Grid.SetRow(label, i);
            Form.Children.Add(label);

            FrameworkElement input = CreateInput(f);
            input.Margin = new Thickness(0, 0, 0, 14);
            Grid.SetRow(input, i);
            Grid.SetColumn(input, 1);
            Form.Children.Add(input);
            _inputs.Add(input);
        }

        if (id != null) LoadValues();
    }

    private FrameworkElement CreateInput(FieldDef f)
    {
        switch (f.Kind)
        {
            case FieldKind.Date:
                return new DatePicker { DisplayDateEnd = DateTime.Today, SelectedDate = DateTime.Today.AddYears(-20), MinHeight = 32 };

            case FieldKind.Lookup:
            {
                DataTable items = Database.Query(f.LookupSql!);
                var box = new ComboBox
                {
                    Style = (Style)FindResource("InputCombo"),
                    ItemsSource = items.DefaultView,
                    SelectedValuePath = items.Columns[0].ColumnName,
                    DisplayMemberPath = items.Columns[1].ColumnName,
                };
                if (items.Rows.Count <= 2) box.SelectedIndex = 0;     // короткие списки (например, статус) — первое значение по умолчанию
                return box;
            }

            case FieldKind.Choice:
                return new ComboBox { Style = (Style)FindResource("InputCombo"), ItemsSource = f.Choices, SelectedIndex = 0 };

            case FieldKind.Password:
                return new PasswordBox { MinHeight = 32, Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center };

            default:
                return new TextBox { Style = (Style)FindResource("InputBox") };
        }
    }

    /// <summary>При редактировании подставляет текущие значения записи в поля.</summary>
    private void LoadValues()
    {
        DataTable t = Database.Query($"SELECT * FROM {_table} WHERE id = @id", ("@id", _id));
        if (t.Rows.Count == 0) return;
        DataRow row = t.Rows[0];

        for (int i = 0; i < _fields.Count; i++)
        {
            FieldDef f = _fields[i];
            if (f.Kind == FieldKind.Password) continue;                 // пароль не показываем; пусто = не менять
            object v = row[f.Column];
            if (v is DBNull) continue;

            switch (_inputs[i])
            {
                case DatePicker dp:
                    if (DateTime.TryParseExact(Convert.ToString(v, CultureInfo.InvariantCulture), "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d))
                        dp.SelectedDate = d;
                    break;
                case ComboBox cb when f.Kind == FieldKind.Lookup:
                    cb.SelectedValue = Convert.ToInt64(v);
                    break;
                case ComboBox cb:
                    cb.SelectedItem = Convert.ToString(v, CultureInfo.InvariantCulture);
                    break;
                case TextBox tb when f.Kind == FieldKind.Real:
                    tb.Text = Convert.ToDouble(v).ToString(CultureInfo.InvariantCulture);
                    break;
                case TextBox tb:
                    tb.Text = Convert.ToString(v, CultureInfo.InvariantCulture);
                    break;
            }
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Появление окна: плавное проявление и небольшой подъём
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        Shift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        if (_inputs.Count > 0) _inputs[0].Focus();
    }

    private static bool Fail(string message, FrameworkElement input)
    {
        Ui.Error(message);
        input.Focus();
        return false;
    }

    /// <summary>Проверка введённых значений. Возвращает false и показывает понятное сообщение при ошибке.</summary>
    private bool Collect(List<string> cols, List<object?> vals)
    {
        for (int i = 0; i < _fields.Count; i++)
        {
            FieldDef f = _fields[i];
            FrameworkElement input = _inputs[i];
            string name = f.Caption;

            switch (f.Kind)
            {
                case FieldKind.Text:
                {
                    string text = ((TextBox)input).Text.Trim();
                    if (f.Required && text.Length == 0) return Fail($"Поле «{name}» обязательно для заполнения.", input);
                    if (text.Length > 200) return Fail($"Поле «{name}» слишком длинное (максимум 200 символов).", input);
                    cols.Add(f.Column);
                    vals.Add(text.Length == 0 ? null : text);
                    break;
                }
                case FieldKind.Integer:
                {
                    string text = ((TextBox)input).Text.Trim();
                    object? value = null;
                    if (text.Length == 0)
                    {
                        if (f.Required) return Fail($"Поле «{name}» обязательно для заполнения.", input);
                    }
                    else
                    {
                        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) || n < 0 || n > 100_000_000)
                            return Fail($"Поле «{name}» должно быть целым неотрицательным числом.", input);
                        value = n;
                    }
                    cols.Add(f.Column);
                    vals.Add(value);
                    break;
                }
                case FieldKind.Real:
                {
                    string text = ((TextBox)input).Text.Trim().Replace(',', '.');
                    object? value = null;
                    if (text.Length == 0)
                    {
                        if (f.Required) return Fail($"Поле «{name}» обязательно для заполнения.", input);
                    }
                    else
                    {
                        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || d < 0 || d > 1_000_000)
                            return Fail($"Поле «{name}» должно быть неотрицательным числом.", input);
                        value = d;
                    }
                    cols.Add(f.Column);
                    vals.Add(value);
                    break;
                }
                case FieldKind.Date:
                {
                    DateTime? date = ((DatePicker)input).SelectedDate;
                    if (date is null) return Fail($"Укажите дату в поле «{name}».", input);
                    if (date > DateTime.Today) return Fail($"Поле «{name}»: дата не может быть в будущем.", input);
                    cols.Add(f.Column);
                    vals.Add(date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    break;
                }
                case FieldKind.Lookup:
                {
                    var box = (ComboBox)input;
                    if (box.SelectedValue is null) return Fail($"Выберите значение в поле «{name}».", input);
                    cols.Add(f.Column);
                    vals.Add(Convert.ToInt64(box.SelectedValue));
                    break;
                }
                case FieldKind.Choice:
                {
                    var box = (ComboBox)input;
                    if (box.SelectedItem is null) return Fail($"Выберите значение в поле «{name}».", input);
                    cols.Add(f.Column);
                    vals.Add(box.SelectedItem.ToString());
                    break;
                }
                case FieldKind.Password:
                {
                    string password = ((PasswordBox)input).Password;
                    if (_id is null && password.Length == 0) return Fail("Укажите пароль.", input);
                    if (password.Length > 0)
                    {
                        if (password.Length < 6) return Fail("Пароль должен быть не короче 6 символов.", input);
                        string salt = Passwords.NewSalt();           // пароль хранится только в виде хэша
                        cols.Add("salt");
                        vals.Add(salt);
                        cols.Add("pass_hash");
                        vals.Add(Passwords.Hash(password, salt));
                    }
                    break;
                }
            }
        }
        return true;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var cols = new List<string>();
        var vals = new List<object?>();
        if (!Collect(cols, vals)) return;

        // параметризованный запрос: значения не подставляются в текст SQL
        var args = cols.Select((_, i) => ($"@v{i}", vals[i])).ToList();
        string sql;
        if (_id is null)
        {
            sql = $"INSERT INTO {_table}({string.Join(", ", cols)}) VALUES({string.Join(", ", cols.Select((_, i) => $"@v{i}"))})";
        }
        else
        {
            sql = $"UPDATE {_table} SET {string.Join(", ", cols.Select((c, i) => $"{c} = @v{i}"))} WHERE id = @id";
            args.Add(("@id", _id));
        }

        try
        {
            Database.Execute(sql, args.ToArray());
            if (_table == "books")      // пересчёт свободных экземпляров
                Database.Execute("UPDATE books SET copies_avail = copies_total - " +
                                 "(SELECT COUNT(*) FROM loans WHERE loans.book_id = books.id AND loans.returned_at IS NULL)");
            Audit.Log((_id is null ? "Добавление записи" : "Изменение записи") + " в таблице " + _table);
        }
        catch (Exception ex)
        {
            string message = ex.Message;
            if (message.Contains("UNIQUE")) message = "Запись с таким значением уже существует.";
            else if (message.Contains("CHECK")) message = "Одно из значений не проходит проверку. Проверьте числа и количество экземпляров.";
            Ui.Error(message);
            return;
        }
        DialogResult = true;
    }
}
