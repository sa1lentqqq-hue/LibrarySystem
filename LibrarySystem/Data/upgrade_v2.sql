-- ============================================================================
--  Обновление схемы до версии 2: целостность данных на уровне БД
--  (триггеры), представления (VIEW) и история изменений книг.
--  Выполняется автоматически при запуске, если PRAGMA user_version < 2.
--  Все команды идемпотентны (IF NOT EXISTS), поэтому повторный запуск безопасен.
-- ============================================================================

-- ---- История изменений цены и количества экземпляров книг ------------------
CREATE TABLE IF NOT EXISTS books_history (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    book_id    INTEGER NOT NULL REFERENCES books(id) ON DELETE CASCADE,
    old_price  REAL,
    new_price  REAL,
    old_total  INTEGER,
    new_total  INTEGER,
    changed_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);
CREATE INDEX IF NOT EXISTS idx_books_history_book ON books_history(book_id);

-- ---- Триггеры: правила библиотеки, которые база защищает сама --------------
-- Даже если в программе появится ошибка или кто-то изменит БД напрямую, нарушить эти правила не получится.

CREATE TRIGGER IF NOT EXISTS trg_loans_before_insert
BEFORE INSERT ON loans
BEGIN
    SELECT RAISE(ABORT, 'Срок возврата не может быть раньше даты выдачи.')
        WHERE NEW.due_at < NEW.issued_at;
    SELECT RAISE(ABORT, 'Читатель заблокирован.')
        WHERE (SELECT blocked FROM readers WHERE id = NEW.reader_id) = 1;
    SELECT RAISE(ABORT, 'Нет свободных экземпляров.')
        WHERE (SELECT copies_avail FROM books WHERE id = NEW.book_id) <= 0;
    SELECT RAISE(ABORT, 'Эта книга уже выдана данному читателю.')
        WHERE EXISTS (SELECT 1 FROM loans
                      WHERE reader_id = NEW.reader_id AND book_id = NEW.book_id AND returned_at IS NULL);
    SELECT RAISE(ABORT, 'Превышен лимит книг на руках.')
        WHERE (SELECT COUNT(*) FROM loans WHERE reader_id = NEW.reader_id AND returned_at IS NULL) >= 5;
    SELECT RAISE(ABORT, 'У читателя есть неоплаченные штрафы.')
        WHERE EXISTS (SELECT 1 FROM fines f JOIN loans l ON l.id = f.loan_id
                      WHERE l.reader_id = NEW.reader_id AND f.paid = 0);
    SELECT RAISE(ABORT, 'У читателя есть просроченные книги.')
        WHERE EXISTS (SELECT 1 FROM loans
                      WHERE reader_id = NEW.reader_id AND returned_at IS NULL
                        AND date(due_at) < date('now','localtime'));
END;

CREATE TRIGGER IF NOT EXISTS trg_loans_before_return
BEFORE UPDATE OF returned_at ON loans
WHEN NEW.returned_at IS NOT NULL
BEGIN
    SELECT RAISE(ABORT, 'Книга уже возвращена.') WHERE OLD.returned_at IS NOT NULL;
    SELECT RAISE(ABORT, 'Дата возврата не может быть раньше даты выдачи.') WHERE NEW.returned_at < OLD.issued_at;
END;

CREATE TRIGGER IF NOT EXISTS trg_books_history
AFTER UPDATE OF price, copies_total ON books
WHEN OLD.price IS NOT NEW.price OR OLD.copies_total IS NOT NEW.copies_total
BEGIN
    INSERT INTO books_history(book_id, old_price, new_price, old_total, new_total)
    VALUES (NEW.id, OLD.price, NEW.price, OLD.copies_total, NEW.copies_total);
END;

-- ---- Представления: готовые «виртуальные таблицы» для отчётов --------------
CREATE VIEW IF NOT EXISTS v_overdue_loans AS
SELECT l.id AS loan_id, r.id AS reader_id, r.card_no, r.full_name, r.phone,
       b.id AS book_id, b.title, l.due_at,
       CAST(julianday(date('now','localtime')) - julianday(l.due_at) AS INTEGER) AS days_overdue
FROM loans l
JOIN readers r ON r.id = l.reader_id
JOIN books b ON b.id = l.book_id
WHERE l.returned_at IS NULL AND date(l.due_at) < date('now','localtime');

CREATE VIEW IF NOT EXISTS v_book_popularity AS
SELECT b.id AS book_id, b.title, b.author, g.name AS genre, COUNT(l.id) AS loans_count
FROM books b
JOIN genres g ON g.id = b.genre_id
LEFT JOIN loans l ON l.book_id = b.id
GROUP BY b.id;

CREATE VIEW IF NOT EXISTS v_reader_fines AS
SELECT r.id AS reader_id, r.full_name, COUNT(f.id) AS fines_count,
       ROUND(SUM(f.amount), 2) AS total,
       ROUND(SUM(CASE WHEN f.paid = 0 THEN f.amount ELSE 0 END), 2) AS unpaid
FROM fines f
JOIN loans l ON l.id = f.loan_id
JOIN readers r ON r.id = l.reader_id
GROUP BY r.id;

CREATE VIEW IF NOT EXISTS v_genre_loans AS
SELECT g.id AS genre_id, g.name AS genre, COUNT(l.id) AS loans_count
FROM genres g
LEFT JOIN books b ON b.genre_id = g.id
LEFT JOIN loans l ON l.book_id = b.id
GROUP BY g.id;

-- Диагностика: книги, у которых «доступно» не равно «всего минус на руках». В исправной базе список пуст.
CREATE VIEW IF NOT EXISTS v_books_consistency AS
SELECT b.id AS book_id, b.title, b.copies_total, b.copies_avail,
       (SELECT COUNT(*) FROM loans l WHERE l.book_id = b.id AND l.returned_at IS NULL) AS on_hand
FROM books b
WHERE b.copies_avail != b.copies_total - (SELECT COUNT(*) FROM loans l WHERE l.book_id = b.id AND l.returned_at IS NULL);
