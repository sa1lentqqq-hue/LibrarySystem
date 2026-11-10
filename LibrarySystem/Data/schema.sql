-- Библиотечная система: схема БД и тестовые данные (SQLite)
-- Выполняется автоматически при первом запуске программы (если файла БД ещё нет).
PRAGMA foreign_keys = ON;

CREATE TABLE users (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    login      TEXT NOT NULL UNIQUE,
    salt       TEXT NOT NULL,
    pass_hash  TEXT NOT NULL,                       -- PBKDF2-SHA256(пароль, salt), hex
    role       TEXT NOT NULL CHECK (role IN ('admin','user')),
    full_name  TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT (date('now','localtime'))
);

CREATE TABLE genres (
    id   INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE
);

CREATE TABLE books (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    title        TEXT NOT NULL,
    author       TEXT NOT NULL,
    genre_id     INTEGER NOT NULL REFERENCES genres(id),
    year         INTEGER CHECK (year IS NULL OR year BETWEEN 1000 AND 2100),
    copies_total INTEGER NOT NULL CHECK (copies_total >= 0),
    copies_avail INTEGER NOT NULL DEFAULT 0 CHECK (copies_avail >= 0),
    price        REAL    NOT NULL DEFAULT 0 CHECK (price >= 0)
);

CREATE TABLE readers (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    card_no       TEXT NOT NULL UNIQUE,
    full_name     TEXT NOT NULL,
    phone         TEXT,
    birth_date    TEXT,
    registered_at TEXT NOT NULL DEFAULT (date('now','localtime')),
    blocked       INTEGER NOT NULL DEFAULT 0 CHECK (blocked IN (0,1))
);

CREATE TABLE loans (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    reader_id   INTEGER NOT NULL REFERENCES readers(id),
    book_id     INTEGER NOT NULL REFERENCES books(id),
    issued_at   TEXT NOT NULL,
    due_at      TEXT NOT NULL,
    returned_at TEXT
);

CREATE TABLE fines (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    loan_id    INTEGER NOT NULL REFERENCES loans(id) ON DELETE CASCADE,
    amount     REAL NOT NULL CHECK (amount >= 0),
    reason     TEXT NOT NULL,
    paid       INTEGER NOT NULL DEFAULT 0 CHECK (paid IN (0,1)),
    created_at TEXT NOT NULL DEFAULT (date('now','localtime'))
);

CREATE TABLE action_log (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id    INTEGER REFERENCES users(id) ON DELETE SET NULL,
    action     TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE INDEX idx_books_genre   ON books(genre_id);
CREATE INDEX idx_loans_reader  ON loans(reader_id);
CREATE INDEX idx_loans_book    ON loans(book_id);
CREATE INDEX idx_fines_loan    ON fines(loan_id);

BEGIN;

-- Пользователи (admin / Admin2026; anna, oleg / User2026)
INSERT INTO users(login, salt, pass_hash, role, full_name) VALUES ('admin', 'a3f19c0d52b7e8411f6c0a9d7e2b4c58', '1953063cecbe599eb9386559ce4d3b2cc619db52924f1f99a453a2c69dd407a5', 'admin', 'Администратор системы');
INSERT INTO users(login, salt, pass_hash, role, full_name) VALUES ('anna', '7be2d4a91c30f6580d1e94b7a3c52f86', 'cb306ac579910e2f272ca60cb947fe14e1575d5e06a97c205cc7cdcad53d66af', 'user', 'Волкова Анна Игоревна');
INSERT INTO users(login, salt, pass_hash, role, full_name) VALUES ('oleg', 'c05a8e17f2d94b36e8a710d93b4c6f25', '87f1bb516f8a01abbf16bbc5c32c6ae804c2455f9cf7e4cf7fb2b63d57fb972e', 'user', 'Громов Олег Петрович');

-- Жанры
INSERT INTO genres(name) VALUES
    ('Классика'),
    ('Фантастика'),
    ('Фэнтези'),
    ('Детектив'),
    ('Наука'),
    ('Программирование'),
    ('История'),
    ('Детская');

-- Книги
INSERT INTO books(title, author, genre_id, year, copies_total, copies_avail, price) VALUES
    ('Война и мир', 'Л. Н. Толстой', 1, 1869, 4, 0, 18.50),
    ('Преступление и наказание', 'Ф. М. Достоевский', 1, 1866, 3, 0, 12.00),
    ('Мастер и Маргарита', 'М. А. Булгаков', 1, 1967, 4, 0, 15.00),
    ('Евгений Онегин', 'А. С. Пушкин', 1, 1833, 2, 0, 9.50),
    ('Мёртвые души', 'Н. В. Гоголь', 1, 1842, 2, 0, 10.00),
    ('Отцы и дети', 'И. С. Тургенев', 1, 1862, 2, 0, 9.00),
    ('Дюна', 'Ф. Герберт', 2, 1965, 3, 0, 22.00),
    ('Солярис', 'С. Лем', 2, 1961, 2, 0, 14.00),
    ('1984', 'Дж. Оруэлл', 2, 1949, 3, 0, 13.50),
    ('Пикник на обочине', 'А. и Б. Стругацкие', 2, 1972, 3, 0, 12.50),
    ('Основание', 'А. Азимов', 2, 1951, 2, 0, 16.00),
    ('Властелин колец', 'Дж. Р. Р. Толкин', 3, 1954, 4, 0, 35.00),
    ('Хоббит', 'Дж. Р. Р. Толкин', 3, 1937, 3, 0, 20.00),
    ('Ведьмак: Последнее желание', 'А. Сапковский', 3, 1993, 2, 0, 17.00),
    ('Убийство в «Восточном экспрессе»', 'А. Кристи', 4, 1934, 3, 0, 11.00),
    ('Собака Баскервилей', 'А. К. Дойл', 4, 1902, 2, 0, 10.50),
    ('Десять негритят', 'А. Кристи', 4, 1939, 2, 0, 11.00),
    ('Имя розы', 'У. Эко', 4, 1980, 2, 0, 19.00),
    ('Краткая история времени', 'С. Хокинг', 5, 1988, 3, 0, 21.00),
    ('Происхождение видов', 'Ч. Дарвин', 5, 1859, 2, 0, 18.00),
    ('Эгоистичный ген', 'Р. Докинз', 5, 1976, 2, 0, 20.00),
    ('Чистый код', 'Р. Мартин', 6, 2008, 4, 0, 45.00),
    ('Совершенный код', 'С. Макконнелл', 6, 2004, 2, 0, 55.00),
    ('Язык программирования C++', 'Б. Страуструп', 6, 2013, 3, 0, 60.00),
    ('Паттерны проектирования', 'Э. Гамма и др.', 6, 1994, 3, 0, 48.00),
    ('Алгоритмы: построение и анализ', 'Т. Кормен и др.', 6, 1990, 2, 0, 65.00),
    ('Sapiens: Краткая история человечества', 'Ю. Харари', 7, 2011, 3, 0, 24.00),
    ('Архипелаг ГУЛАГ', 'А. И. Солженицын', 7, 1973, 2, 0, 25.00),
    ('Маленький принц', 'А. де Сент-Экзюпери', 8, 1943, 4, 0, 8.00),
    ('Алиса в Стране чудес', 'Л. Кэрролл', 8, 1865, 2, 0, 9.00),
    ('Карлсон, который живёт на крыше', 'А. Линдгрен', 8, 1955, 3, 0, 9.50);

-- Читатели
INSERT INTO readers(card_no, full_name, phone, birth_date, registered_at, blocked) VALUES
    ('ЧБ-0001', 'Иванов Пётр Сергеевич', '+375 29 111-22-33', '2001-03-14', date('now','localtime','-200 days'), 0),
    ('ЧБ-0002', 'Петрова Мария Андреевна', '+375 29 222-33-44', '1999-11-02', date('now','localtime','-193 days'), 0),
    ('ЧБ-0003', 'Сидоров Алексей Николаевич', '+375 33 333-44-55', '2002-07-21', date('now','localtime','-186 days'), 0),
    ('ЧБ-0004', 'Козлова Елена Викторовна', '+375 44 444-55-66', '1985-01-30', date('now','localtime','-179 days'), 0),
    ('ЧБ-0005', 'Новиков Дмитрий Олегович', '+375 29 555-66-77', '2003-05-09', date('now','localtime','-172 days'), 0),
    ('ЧБ-0006', 'Морозова Ольга Игоревна', '+375 25 666-77-88', '1995-09-17', date('now','localtime','-165 days'), 0),
    ('ЧБ-0007', 'Волков Сергей Павлович', '+375 29 777-88-99', '1990-12-05', date('now','localtime','-158 days'), 0),
    ('ЧБ-0008', 'Лебедева Анастасия Романовна', '+375 33 888-99-00', '2004-02-11', date('now','localtime','-151 days'), 0),
    ('ЧБ-0009', 'Соколов Андрей Валерьевич', '+375 44 999-00-11', '1978-06-23', date('now','localtime','-144 days'), 0),
    ('ЧБ-0010', 'Михайлова Татьяна Юрьевна', '+375 29 101-11-21', '1992-04-18', date('now','localtime','-137 days'), 0),
    ('ЧБ-0011', 'Фёдоров Николай Иванович', '+375 25 121-31-41', '2000-08-27', date('now','localtime','-130 days'), 0),
    ('ЧБ-0012', 'Орлова Виктория Сергеевна', '+375 29 141-51-61', '2001-10-03', date('now','localtime','-123 days'), 0),
    ('ЧБ-0013', 'Беляев Максим Александрович', '+375 33 161-71-81', '2002-01-15', date('now','localtime','-116 days'), 0),
    ('ЧБ-0014', 'Григорьева Дарья Михайловна', '+375 44 181-91-02', '1998-03-29', date('now','localtime','-109 days'), 0),
    ('ЧБ-0015', 'Захаров Илья Денисович', '+375 29 202-12-22', '2005-09-08', date('now','localtime','-102 days'), 0),
    ('ЧБ-0016', 'Николаева Полина Артёмовна', '+375 25 232-42-52', '2003-12-19', date('now','localtime','-95 days'), 0),
    ('ЧБ-0017', 'Крылов Владислав Эдуардович', '+375 29 262-72-82', '1987-07-07', date('now','localtime','-88 days'), 0),
    ('ЧБ-0018', 'Тихонова Ирина Леонидовна', '+375 33 292-03-13', '1996-05-25', date('now','localtime','-81 days'), 1);

-- Выдачи (даты считаются от текущего дня, чтобы просрочки были всегда)
INSERT INTO loans(reader_id, book_id, issued_at, due_at, returned_at) VALUES
    (1, 22, date('now','localtime','-60 days'), date('now','localtime','-46 days'), date('now','localtime','-50 days')),
    (2, 3, date('now','localtime','-55 days'), date('now','localtime','-41 days'), date('now','localtime','-45 days')),
    (3, 10, date('now','localtime','-50 days'), date('now','localtime','-36 days'), date('now','localtime','-40 days')),
    (4, 27, date('now','localtime','-48 days'), date('now','localtime','-34 days'), date('now','localtime','-36 days')),
    (5, 7, date('now','localtime','-45 days'), date('now','localtime','-31 days'), date('now','localtime','-33 days')),
    (6, 29, date('now','localtime','-44 days'), date('now','localtime','-30 days'), date('now','localtime','-35 days')),
    (7, 19, date('now','localtime','-42 days'), date('now','localtime','-28 days'), date('now','localtime','-30 days')),
    (8, 13, date('now','localtime','-40 days'), date('now','localtime','-26 days'), date('now','localtime','-27 days')),
    (9, 15, date('now','localtime','-38 days'), date('now','localtime','-24 days'), date('now','localtime','-25 days')),
    (10, 12, date('now','localtime','-35 days'), date('now','localtime','-21 days'), date('now','localtime','-22 days')),
    (11, 2, date('now','localtime','-70 days'), date('now','localtime','-56 days'), date('now','localtime','-46 days')),
    (12, 9, date('now','localtime','-65 days'), date('now','localtime','-51 days'), date('now','localtime','-41 days')),
    (13, 22, date('now','localtime','-60 days'), date('now','localtime','-46 days'), date('now','localtime','-36 days')),
    (14, 24, date('now','localtime','-58 days'), date('now','localtime','-44 days'), date('now','localtime','-30 days')),
    (15, 25, date('now','localtime','-52 days'), date('now','localtime','-38 days'), date('now','localtime','-33 days')),
    (16, 16, date('now','localtime','-50 days'), date('now','localtime','-36 days'), date('now','localtime','-31 days')),
    (17, 14, date('now','localtime','-80 days'), date('now','localtime','-66 days'), date('now','localtime','-60 days')),
    (2, 12, date('now','localtime','-5 days'), date('now','localtime','+9 days'), NULL),
    (3, 22, date('now','localtime','-3 days'), date('now','localtime','+11 days'), NULL),
    (4, 7, date('now','localtime','-8 days'), date('now','localtime','+6 days'), NULL),
    (5, 18, date('now','localtime','-2 days'), date('now','localtime','+12 days'), NULL),
    (6, 9, date('now','localtime','-10 days'), date('now','localtime','+4 days'), NULL),
    (9, 27, date('now','localtime','-1 days'), date('now','localtime','+13 days'), NULL),
    (10, 1, date('now','localtime','-6 days'), date('now','localtime','+8 days'), NULL),
    (11, 3, date('now','localtime','-4 days'), date('now','localtime','+10 days'), NULL),
    (7, 24, date('now','localtime','-30 days'), date('now','localtime','-16 days'), NULL),
    (8, 1, date('now','localtime','-25 days'), date('now','localtime','-11 days'), NULL),
    (12, 19, date('now','localtime','-22 days'), date('now','localtime','-8 days'), NULL),
    (14, 28, date('now','localtime','-40 days'), date('now','localtime','-26 days'), NULL),
    (16, 26, date('now','localtime','-18 days'), date('now','localtime','-4 days'), NULL);

-- Потерянная книга №14 списывается из фонда
UPDATE books SET copies_total = copies_total - 1 WHERE id = 14;

-- Штрафы
INSERT INTO fines(loan_id, amount, reason, paid, created_at) VALUES
    (11, 5.00, 'Просрочка', 1, date('now','localtime','-46 days')),
    (12, 5.00, 'Просрочка', 1, date('now','localtime','-41 days')),
    (13, 5.00, 'Просрочка', 0, date('now','localtime','-36 days')),
    (14, 7.00, 'Просрочка', 1, date('now','localtime','-30 days')),
    (15, 2.50, 'Просрочка', 1, date('now','localtime','-33 days')),
    (16, 2.50, 'Просрочка', 0, date('now','localtime','-31 days')),
    (17, 17.00, 'Утеря', 0, date('now','localtime','-60 days')),
    (26, 8.00, 'Просрочка', 0, date('now','localtime','-1 days')),
    (27, 5.50, 'Просрочка', 0, date('now','localtime','-1 days'));

-- Журнал действий
INSERT INTO action_log(user_id, action, created_at) VALUES
    (1, 'Вход в систему', datetime('now','localtime','-12 days')),
    (1, 'Добавление записи в таблице books', datetime('now','localtime','-11 days')),
    (2, 'Вход в систему', datetime('now','localtime','-9 days')),
    (2, 'Выдача книги: читатель 11, книга 3', datetime('now','localtime','-4 days')),
    (3, 'Вход в систему', datetime('now','localtime','-2 days')),
    (1, 'Резервная копия БД', datetime('now','localtime','-1 days'));

-- Свободные экземпляры = всего − на руках
UPDATE books SET copies_avail = copies_total - (SELECT COUNT(*) FROM loans WHERE loans.book_id = books.id AND loans.returned_at IS NULL);

COMMIT;
