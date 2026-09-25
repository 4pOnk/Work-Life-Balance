namespace WorkLifeBalance.Domain;

public sealed record TodoItem(string Id, string Title, int Weight, string Date, long? CompletedAt, string? CompletedDate);
public sealed record TodoDay(string Date, string Today, IReadOnlyList<TodoItem> Items);
public sealed record TodoDraft(string Title, int Weight, string Date)
{
    public TodoDraft Validate(DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Trim().Length > 240 || Title.Any(char.IsControl))
            throw new ArgumentException("Название задачи: от 1 до 240 символов, без переносов строк.");
        if (Weight is < 1 or > 5) throw new ArgumentException("Вес задачи должен быть от 1 до 5.");
        if (!DateOnly.TryParseExact(Date, "yyyy-MM-dd", out var date) || date < today || date.Year > 9998)
            throw new ArgumentException("Задачу можно запланировать на сегодня или будущий день.");
        return this with { Title = Title.Trim() };
    }
}
