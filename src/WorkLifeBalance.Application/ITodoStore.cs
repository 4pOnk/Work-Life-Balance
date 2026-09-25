using WorkLifeBalance.Domain;

namespace WorkLifeBalance.Application;

public interface ITodoStore
{
    TodoDay ReadTodos(DateOnly date, DateOnly today);
    TodoItem CreateTodo(TodoDraft draft, DateOnly today);
    void UpdateTodo(string id, TodoDraft draft, DateOnly today);
    void CompleteTodo(string id, bool completed, DateOnly today, long now);
    void DeleteTodo(string id, DateOnly today);
}
