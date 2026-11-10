using LibrarySystem.Data;

namespace LibrarySystem.Services;

/// <summary>Журнал действий: кто и что делал (виден администратору).</summary>
public static class Audit
{
    public static void Log(string action)
    {
        try
        {
            if (Session.UserId > 0)
                Database.Execute("INSERT INTO action_log(user_id, action) VALUES(@u, @a)",
                    ("@u", Session.UserId), ("@a", action));
            else
                Database.Execute("INSERT INTO action_log(user_id, action) VALUES(NULL, @a)", ("@a", action));
        }
        catch
        {
            // сбой журнала не должен ломать основную операцию
        }
    }
}
