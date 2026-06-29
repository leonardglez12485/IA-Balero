using System.Text.Json;
using FinancialChat.Models;

namespace FinancialChat.Services;

public sealed class ConversationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _storePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ConversationStore(IWebHostEnvironment env)
    {
        var dataDir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDir);
        _storePath = Path.Combine(dataDir, "conversations.json");
    }

    public async Task<IReadOnlyList<ConversationListItem>> ListAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var conversations = await ReadAllUnsafeAsync();
            return conversations
                .OrderByDescending(c => c.UpdatedAt)
                .Select(c => new ConversationListItem
                {
                    Id = c.Id,
                    Title = c.Title,
                    UpdatedAt = c.UpdatedAt,
                    LastModel = c.LastModel
                })
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredConversation> CreateAsync(string model)
    {
        await _gate.WaitAsync();
        try
        {
            var conversations = await ReadAllUnsafeAsync();
            var conversation = new StoredConversation
            {
                LastModel = model,
                UpdatedAt = DateTime.Now
            };

            conversations.Add(conversation);
            await WriteAllUnsafeAsync(conversations);
            return conversation;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredConversation?> GetAsync(Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            var conversations = await ReadAllUnsafeAsync();
            return conversations.FirstOrDefault(c => c.Id == id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddMessageAsync(Guid conversationId, MessageRole role, string content, string model)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        await _gate.WaitAsync();
        try
        {
            var conversations = await ReadAllUnsafeAsync();
            var conversation = conversations.FirstOrDefault(c => c.Id == conversationId);
            if (conversation is null)
                return;

            conversation.Messages.Add(new StoredChatMessage
            {
                Role = role,
                Content = content,
                Model = model
            });

            if (role == MessageRole.User && conversation.Messages.Count(m => m.Role == MessageRole.User) == 1)
                conversation.Title = BuildTitle(content);

            conversation.LastModel = model;
            conversation.UpdatedAt = DateTime.Now;
            await WriteAllUnsafeAsync(conversations);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateThreadAsync(Guid conversationId, string? threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            return;

        await _gate.WaitAsync();
        try
        {
            var conversations = await ReadAllUnsafeAsync();
            var conversation = conversations.FirstOrDefault(c => c.Id == conversationId);
            if (conversation is null)
                return;

            conversation.CodexThreadId = threadId;
            conversation.UpdatedAt = DateTime.Now;
            await WriteAllUnsafeAsync(conversations);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateUsageAsync(Guid conversationId, CodexUsageSnapshot usage)
    {
        await _gate.WaitAsync();
        try
        {
            var conversations = await ReadAllUnsafeAsync();
            var conversation = conversations.FirstOrDefault(c => c.Id == conversationId);
            if (conversation is null)
                return;

            conversation.TotalTokens = usage.TotalTokens;
            conversation.EstimatedCost = usage.EstimatedCost;
            conversation.LastModel = usage.Model;
            conversation.UpdatedAt = DateTime.Now;
            await WriteAllUnsafeAsync(conversations);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid conversationId)
    {
        await _gate.WaitAsync();
        try
        {
            var conversations = await ReadAllUnsafeAsync();
            var removed = conversations.RemoveAll(c => c.Id == conversationId);
            if (removed > 0)
                await WriteAllUnsafeAsync(conversations);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<StoredConversation>> ReadAllUnsafeAsync()
    {
        if (!File.Exists(_storePath))
            return [];

        await using var stream = File.OpenRead(_storePath);
        return await JsonSerializer.DeserializeAsync<List<StoredConversation>>(stream, JsonOptions) ?? [];
    }

    private async Task WriteAllUnsafeAsync(List<StoredConversation> conversations)
    {
        var tempPath = _storePath + ".tmp";
        await using (var stream = File.Create(tempPath))
            await JsonSerializer.SerializeAsync(stream, conversations, JsonOptions);

        File.Copy(tempPath, _storePath, overwrite: true);
        File.Delete(tempPath);
    }

    private static string BuildTitle(string content)
    {
        var clean = string.Join(' ', content.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length <= 48)
            return clean;

        return clean[..48].TrimEnd() + "...";
    }
}
