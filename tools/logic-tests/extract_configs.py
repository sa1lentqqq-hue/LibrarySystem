"""
Достаёт из исходников приложения настоящие SQL-запросы и настройки экранов (сортировка по умолчанию,
фильтры), чтобы проверка выполняла именно то, что написано в коде. Результат — configs.json.
Запуск:  python3 extract_configs.py
"""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent / "LibrarySystem"


def read(rel: str) -> str:
    return (ROOT / rel).read_text(encoding="utf-8-sig")


def parse_literal(s: str, i: int):
    """Разбирает C#-литерал строки (обычный или @-дословный); возвращает (значение, позиция после)."""
    if s.startswith('@"', i):
        j, out = i + 2, []
        while True:
            if s[j] == '"':
                if s[j + 1:j + 2] == '"':
                    out.append('"'); j += 2; continue
                return "".join(out), j + 1
            out.append(s[j]); j += 1
    j, out = i + 1, []
    while True:
        c = s[j]
        if c == "\\":
            out.append(s[j + 1]); j += 2; continue
        if c == '"':
            return "".join(out), j + 1
        out.append(c); j += 1


def literal_concat(s: str, i: int):
    """Литерал или несколько литералов, соединённых знаком +."""
    parts = []
    while True:
        while s[i] in " \t\r\n":
            i += 1
        value, i = parse_literal(s, i)
        parts.append(value)
        k = i
        while s[k] in " \t\r\n":
            k += 1
        if s[k] == "+":
            i = k + 1
            continue
        return "".join(parts), i


configs = []

# таблицы из PageFactory (книги, читатели, жанры, пользователи, журнал)
factory = read("Pages/PageFactory.cs")
for m in re.finditer(r'new TableConfig\("(\w+)",\s*', factory):
    sql, end = literal_concat(factory, m.end())
    nxt = factory.find('case "', end)
    block = factory[end: nxt if nxt > 0 else len(factory)]
    sort = re.search(r'DefaultSort:\s*"([^"]+)"', block)
    configs.append({
        "name": "table:" + m.group(1), "sql": sql,
        "sort": sort.group(1) if sort else None,
        "desc": bool(re.search(r"DefaultDescending:\s*true", block)),
        "drops": [{"col": c, "values": None} for c in re.findall(r'new DropFilter\("([^"]+)"\)', block)],
    })

# выдачи и штрафы: фиксированные значения фильтра по статусу
for rel, name, grid in (("Pages/LoansPage.xaml.cs", "loans", "LoansGrid"), ("Pages/FinesPage.xaml.cs", "fines", "FinesGrid")):
    src = read(rel)
    sql, _ = literal_concat(src, re.search(r"_data = Database\.Query\(\s*", src).end())
    m = re.search(r'Configure\(%s,\s*"(\w+)",\s*(true|false),\s*new DropFilter\("(\w+)",\s*new\[\]\s*\{([^}]*)\}' % grid, src)
    configs.append({"name": name, "sql": sql, "sort": m.group(1), "desc": m.group(2) == "true",
                    "drops": [{"col": m.group(3), "values": re.findall(r'"([^"]+)"', m.group(4))}]})

# результаты поиска
src = read("Pages/SearchPage.xaml.cs")
sql, _ = literal_concat(src, re.search(r"DataTable table = Database\.Query\(\s*", src).end())
m = re.search(r'Configure\(ResultGrid,\s*"(\w+)",\s*(true|false),\s*new DropFilter\("(\w+)"\)', src)
configs.append({"name": "search", "sql": sql, "sort": m.group(1), "desc": m.group(2) == "true",
                "drops": [{"col": m.group(3), "values": None}]})

(Path(__file__).parent / "configs.json").write_text(json.dumps(configs, ensure_ascii=False, indent=1), encoding="utf-8")
print(f"Извлечено экранов: {len(configs)}")
