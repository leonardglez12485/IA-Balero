namespace FinancialChat.Models;

public sealed class StoredConversation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = "Nuevo chat";
    public string? CodexThreadId { get; set; }
    public string LastModel { get; set; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public long TotalTokens { get; set; }
    public decimal EstimatedCost { get; set; }
    public List<StoredChatMessage> Messages { get; set; } = [];
}

public sealed class StoredChatMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public MessageRole Role { get; init; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public string Model { get; set; } = string.Empty;
}

public sealed class ConversationListItem
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public DateTime UpdatedAt { get; init; }
    public string LastModel { get; init; } = string.Empty;
}
