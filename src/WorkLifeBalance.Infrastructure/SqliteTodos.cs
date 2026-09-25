using WorkLifeBalance.Domain;
using Microsoft.Data.Sqlite;

namespace WorkLifeBalance.Infrastructure;

public sealed partial class SqliteTrackerStore
{
    // Overdue dates are projected onto today, so rollover also works after downtime without a daily writer.
    public TodoDay ReadTodos(DateOnly date, DateOnly today)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,title,weight,planned_day,completed_at,completed_day FROM todos
            WHERE completed_day=$day OR (completed_at IS NULL AND $day >= $today AND
                (planned_day=$day OR ($day=$today AND planned_day < $today)))
            ORDER BY rowid;
            """;
        command.Parameters.AddWithValue("$day", date.DayNumber);
        command.Parameters.AddWithValue("$today", today.DayNumber);
        return new(date.ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd"), ReadTodoRows(command, today));
    }

    private IReadOnlyList<TodoItem> ReadCompletedTodos(DateOnly date)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,title,weight,planned_day,completed_at,completed_day FROM todos WHERE completed_day=$day ORDER BY rowid;";
        command.Parameters.AddWithValue("$day", date.DayNumber);
        return ReadTodoRows(command, date);
    }

    private static List<TodoItem> ReadTodoRows(SqliteCommand command, DateOnly today)
    {
        var result = new List<TodoItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var completed = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
            var date = DateOnly.FromDayNumber(completed ?? Math.Max(today.DayNumber, reader.GetInt32(3))).ToString("yyyy-MM-dd");
            result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), date,
                reader.IsDBNull(4) ? null : reader.GetInt64(4), completed.HasValue ? date : null));
        }
        return result;
    }

    public TodoItem CreateTodo(TodoDraft draft, DateOnly today)
    {
        draft = draft.Validate(today);
        var id = Guid.NewGuid().ToString("N");
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO todos(id,title,weight,planned_day) VALUES($id,$title,$weight,$day);";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$title", draft.Title);
        command.Parameters.AddWithValue("$weight", draft.Weight);
        command.Parameters.AddWithValue("$day", DateOnly.ParseExact(draft.Date, "yyyy-MM-dd").DayNumber);
        command.ExecuteNonQuery();
        return new(id, draft.Title, draft.Weight, draft.Date, null, null);
    }

    private TodoItem EditableTodo(string id, DateOnly today)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Неверный идентификатор задачи.");
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,title,weight,planned_day,completed_at,completed_day FROM todos WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id);
        var todo = ReadTodoRows(command, today).SingleOrDefault() ?? throw new ArgumentException("Задача не найдена. Обновите список.");
        if (todo.CompletedDate is { } done && DateOnly.ParseExact(done, "yyyy-MM-dd") < today)
            throw new ArgumentException("Задачи прошлых дней доступны только для чтения.");
        return todo;
    }

    public void UpdateTodo(string id, TodoDraft draft, DateOnly today)
    {
        draft = draft.Validate(today);
        var todo = EditableTodo(id, today);
        if (todo.CompletedAt.HasValue && draft.Date != todo.CompletedDate)
            throw new ArgumentException("Сначала снимите отметку выполнения, чтобы перенести задачу.");
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE todos SET title=$title,weight=$weight,planned_day=$day WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$title", draft.Title);
        command.Parameters.AddWithValue("$weight", draft.Weight);
        command.Parameters.AddWithValue("$day", DateOnly.ParseExact(draft.Date, "yyyy-MM-dd").DayNumber);
        command.ExecuteNonQuery();
    }

    public void CompleteTodo(string id, bool completed, DateOnly today, long now)
    {
        var todo = EditableTodo(id, today);
        if (DateOnly.ParseExact(todo.Date, "yyyy-MM-dd") > today)
            throw new ArgumentException("Будущую задачу пока нельзя отметить выполненной.");
        if (completed == todo.CompletedAt.HasValue) return;
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE todos SET completed_at=$at,completed_day=$completed,planned_day=$day WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$at", completed ? now : DBNull.Value);
        command.Parameters.AddWithValue("$completed", completed ? today.DayNumber : DBNull.Value);
        command.Parameters.AddWithValue("$day", today.DayNumber);
        command.ExecuteNonQuery();
    }

    public void DeleteTodo(string id, DateOnly today)
    {
        EditableTodo(id, today);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM todos WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }
}
