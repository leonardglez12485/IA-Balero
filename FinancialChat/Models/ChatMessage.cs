namespace FinancialChat.Models;

public enum MessageRole { User, Assistant, System }

public class ChatMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public MessageRole Role { get; init; }
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public bool IsStreaming { get; set; } = false;

    public static ChatMessage FromUser(string content) => new()
    {
        Role = MessageRole.User,
        Content = content
    };

    public static ChatMessage FromAssistant(string content = "", bool isStreaming = false) => new()
    {
        Role = MessageRole.Assistant,
        Content = content,
        IsStreaming = isStreaming
    };

    public static ChatMessage FromStored(MessageRole role, string content, DateTime timestamp) => new()
    {
        Role = role,
        Content = content,
        Timestamp = timestamp
    };
}
