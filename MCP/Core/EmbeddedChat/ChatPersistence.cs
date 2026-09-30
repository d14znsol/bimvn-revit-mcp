#if REVIT2023 || REVIT2025
using Newtonsoft.Json;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DSCons.RevitMcp.Core.EmbeddedChat;

/// <summary>Local, user-owned chat index. It never writes to the Revit document.</summary>
internal sealed class ChatProjectContext
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsPersistable { get; set; }

    public static ChatProjectContext Create(string? documentPath, string? title)
    {
        if (string.IsNullOrWhiteSpace(documentPath))
            return new ChatProjectContext { Key = "unsaved-" + Guid.NewGuid().ToString("N"), DisplayName = (title ?? "Project chưa lưu") + " (chỉ phiên này)", IsPersistable = false };
        var fullPath = Path.GetFullPath(documentPath).Trim().ToUpperInvariant();
        return new ChatProjectContext
        {
            Key = Hash("2023\n" + fullPath),
            DisplayName = Path.GetFileName(fullPath),
            IsPersistable = true
        };
    }

    private static string Hash(string value)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(item => item.ToString("x2")));
    }
}

internal sealed class ChatTranscriptEntry
{
    public string Role { get; set; } = "assistant";
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class ChatConversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectKey { get; set; } = string.Empty;
    public string ProviderId { get; set; } = "codex";
    public string DisplayName { get; set; } = "Cuộc trò chuyện mới";
    public string? ProviderConversationId { get; set; }
    // Read only during v1 migration. New records use ProviderConversationId so
    // a provider session can never be resumed by another CLI.
    public string? ThreadId { get; set; }
    public string? Model { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<ChatTranscriptEntry> Messages { get; set; } = new();
}

internal sealed class ChatMemoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Scope { get; set; } = "project";
    public string? ProjectKey { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class ChatPersistenceState
{
    public int SchemaVersion { get; set; } = 2;
    public Dictionary<string, string> ActiveConversationIds { get; set; } = new(StringComparer.Ordinal);
    public List<ChatConversation> Conversations { get; set; } = new();
    public List<ChatMemoryEntry> Memories { get; set; } = new();
}

internal sealed class ChatPersistence
{
    public static readonly ChatPersistence Instance = new();
    private readonly object _gate = new();
    private readonly string _path;
    private ChatPersistenceState? _state;

    private ChatPersistence()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSCons", "RevitMcp", "Chat");
        _path = Path.Combine(root, "conversations.json");
    }

    public ChatConversation GetOrCreateActive(ChatProjectContext context, string providerId)
    {
        providerId = NormalizeProviderId(providerId);
        if (!context.IsPersistable) return NewTransient(context, providerId);
        lock (_gate)
        {
            var state = LoadLocked();
            var activeKey = ActiveKey(context, providerId);
            if (state.ActiveConversationIds.TryGetValue(activeKey, out var id))
            {
                var existing = state.Conversations.FirstOrDefault(item => item.Id == id && item.ProjectKey == context.Key && item.ProviderId == providerId);
                if (existing != null) return Clone(existing);
            }
            var created = NewConversationCore(context, providerId);
            state.Conversations.Add(created);
            state.ActiveConversationIds[activeKey] = created.Id;
            SaveLocked(state);
            return Clone(created);
        }
    }

    public IReadOnlyList<ChatConversation> List(ChatProjectContext context, string providerId)
    {
        if (!context.IsPersistable) return Array.Empty<ChatConversation>();
        providerId = NormalizeProviderId(providerId);
        lock (_gate) return LoadLocked().Conversations.Where(item => item.ProjectKey == context.Key && item.ProviderId == providerId).OrderByDescending(item => item.UpdatedUtc).Select(Clone).ToArray();
    }

    public ChatConversation CreateNew(ChatProjectContext context, string providerId)
    {
        providerId = NormalizeProviderId(providerId);
        if (!context.IsPersistable) return NewTransient(context, providerId);
        lock (_gate)
        {
            var state = LoadLocked();
            var created = NewConversationCore(context, providerId);
            state.Conversations.Add(created);
            state.ActiveConversationIds[ActiveKey(context, providerId)] = created.Id;
            SaveLocked(state);
            return Clone(created);
        }
    }

    public ChatConversation? Find(ChatProjectContext context, string providerId, string conversationId)
    {
        if (!context.IsPersistable) return null;
        providerId = NormalizeProviderId(providerId);
        lock (_gate) return LoadLocked().Conversations.Where(item => item.ProjectKey == context.Key && item.ProviderId == providerId && item.Id == conversationId).Select(Clone).FirstOrDefault();
    }

    public void Activate(ChatProjectContext context, string providerId, string conversationId)
    {
        if (!context.IsPersistable) return;
        providerId = NormalizeProviderId(providerId);
        lock (_gate)
        {
            var state = LoadLocked();
            if (state.Conversations.Any(item => item.ProjectKey == context.Key && item.ProviderId == providerId && item.Id == conversationId))
            {
                state.ActiveConversationIds[ActiveKey(context, providerId)] = conversationId;
                SaveLocked(state);
            }
        }
    }

    public void Delete(ChatProjectContext context, string providerId, string conversationId)
    {
        if (!context.IsPersistable) return;
        providerId = NormalizeProviderId(providerId);
        lock (_gate)
        {
            var state = LoadLocked();
            state.Conversations.RemoveAll(item => item.ProjectKey == context.Key && item.ProviderId == providerId && item.Id == conversationId);
            var activeKey = ActiveKey(context, providerId);
            if (state.ActiveConversationIds.TryGetValue(activeKey, out var active) && active == conversationId) state.ActiveConversationIds.Remove(activeKey);
            SaveLocked(state);
        }
    }

    public void UpdateProviderConversation(ChatProjectContext context, string providerId, string conversationId, string providerConversationId, string? model)
    {
        if (!context.IsPersistable) return;
        providerId = NormalizeProviderId(providerId);
        lock (_gate)
        {
            var item = LoadLocked().Conversations.FirstOrDefault(entry => entry.ProjectKey == context.Key && entry.ProviderId == providerId && entry.Id == conversationId);
            if (item == null) return;
            item.ProviderConversationId = providerConversationId; item.ThreadId = null; item.Model = model; item.UpdatedUtc = DateTime.UtcNow;
            SaveLocked(_state!);
        }
    }

    public void AddMessage(ChatProjectContext context, string providerId, string conversationId, string role, string text)
    {
        if (!context.IsPersistable || string.IsNullOrWhiteSpace(text)) return;
        providerId = NormalizeProviderId(providerId);
        lock (_gate)
        {
            var item = LoadLocked().Conversations.FirstOrDefault(entry => entry.ProjectKey == context.Key && entry.ProviderId == providerId && entry.Id == conversationId);
            if (item == null) return;
            item.Messages.Add(new ChatTranscriptEntry { Role = role, Text = text, CreatedUtc = DateTime.UtcNow });
            item.UpdatedUtc = DateTime.UtcNow;
            if (item.DisplayName == "Cuộc trò chuyện mới" && role == "user") item.DisplayName = TrimTitle(text);
            SaveLocked(_state!);
        }
    }

    public IReadOnlyList<ChatMemoryEntry> GetMemories(ChatProjectContext context)
    {
        lock (_gate) return LoadLocked().Memories.Where(item => item.Scope == "global" || item.ProjectKey == context.Key).Select(Clone).ToArray();
    }

    public bool Remember(ChatProjectContext context, string text, bool global)
    {
        text = text.Trim();
        if (text.Length == 0 || text.Length > 800 || !IsSafeMemoryText(text)) return false;
        lock (_gate)
        {
            var state = LoadLocked();
            if (state.Memories.Any(item => item.Scope == (global ? "global" : "project") && item.ProjectKey == (global ? null : context.Key) && string.Equals(item.Text, text, StringComparison.OrdinalIgnoreCase))) return false;
            state.Memories.Add(new ChatMemoryEntry { Scope = global ? "global" : "project", ProjectKey = global ? null : context.Key, Text = text });
            SaveLocked(state);
            return true;
        }
    }

    public void Forget(string memoryId)
    {
        lock (_gate) { var state = LoadLocked(); state.Memories.RemoveAll(item => item.Id == memoryId); SaveLocked(state); }
    }

    private ChatPersistenceState LoadLocked()
    {
        if (_state != null) return _state;
        try
        {
            _state = File.Exists(_path) ? JsonConvert.DeserializeObject<ChatPersistenceState>(File.ReadAllText(_path)) : null;
        }
        catch { _state = null; }
        _state ??= new ChatPersistenceState();
        _state.ActiveConversationIds ??= new Dictionary<string, string>(StringComparer.Ordinal);
        _state.Conversations ??= new List<ChatConversation>();
        _state.Memories ??= new List<ChatMemoryEntry>();
        MigrateV1ToV2Locked(_state);
        return _state;
    }

    private void SaveLocked(ChatPersistenceState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonConvert.SerializeObject(state, Formatting.Indented), new UTF8Encoding(false));
        File.Copy(temp, _path, true);
        File.Delete(temp);
    }

    private static ChatConversation NewConversationCore(ChatProjectContext context, string providerId) => new() { ProjectKey = context.Key, ProviderId = providerId };
    private static ChatConversation NewTransient(ChatProjectContext context, string providerId) => new() { ProjectKey = context.Key, ProviderId = providerId, DisplayName = context.DisplayName };
    private static string ActiveKey(ChatProjectContext context, string providerId) => context.Key + "\n" + providerId;
    private static string NormalizeProviderId(string? providerId)
    {
        if (providerId is "codex" or "claude" or "antigravity") return providerId;
        throw new ArgumentException("Unknown embedded chat provider.", nameof(providerId));
    }
    private static void MigrateV1ToV2Locked(ChatPersistenceState state)
    {
        if (state.SchemaVersion >= 2)
        {
            foreach (var conversation in state.Conversations)
            {
                conversation.ProviderId = string.IsNullOrWhiteSpace(conversation.ProviderId) ? "codex" : NormalizeProviderId(conversation.ProviderId);
                if (string.IsNullOrWhiteSpace(conversation.ProviderConversationId) && !string.IsNullOrWhiteSpace(conversation.ThreadId)) conversation.ProviderConversationId = conversation.ThreadId;
                conversation.ThreadId = null;
            }
            state.SchemaVersion = 2;
            return;
        }
        foreach (var conversation in state.Conversations)
        {
            conversation.ProviderId = "codex";
            conversation.ProviderConversationId = conversation.ThreadId;
            conversation.ThreadId = null;
        }
        state.ActiveConversationIds = state.ActiveConversationIds.ToDictionary(pair => pair.Key.Contains("\n") ? pair.Key : pair.Key + "\n" + "codex", pair => pair.Value, StringComparer.Ordinal);
        state.SchemaVersion = 2;
    }
    // Memory is deliberately limited to durable learner preferences. Transient identifiers,
    // credentials and machine paths must never become part of a later model prompt.
    private static bool IsSafeMemoryText(string text)
    {
        var lower = text.ToLowerInvariant();
        var prohibited = new[] { "token", "secret", "password", "api key", "credential", "preview_id", "preview id", "element_id", "element id", "tool arguments", "tool_args", "raw arguments", "pid", "process id", "login path" };
        return !prohibited.Any(lower.Contains) && !text.Contains(@":\\") && !text.Contains(@"\\\\");
    }
    private static string TrimTitle(string value) => value.Trim().Replace('\r', ' ').Replace('\n', ' ').Substring(0, Math.Min(48, value.Trim().Length));
    private static ChatConversation Clone(ChatConversation source) => JsonConvert.DeserializeObject<ChatConversation>(JsonConvert.SerializeObject(source))!;
    private static ChatMemoryEntry Clone(ChatMemoryEntry source) => JsonConvert.DeserializeObject<ChatMemoryEntry>(JsonConvert.SerializeObject(source))!;
}

internal static class ChatDiagnosticLog
{
    private static readonly object Gate = new();
    public static void Write(string category, string detail)
    {
        try
        {
            lock (Gate)
            {
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSCons", "RevitMcp", "Chat");
                Directory.CreateDirectory(root);
                var path = Path.Combine(root, "diagnostic.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
                {
                    var rotated = path + ".1";
                    if (File.Exists(rotated)) File.Delete(rotated);
                    File.Move(path, rotated);
                }
                File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " " + category + " " + detail.Replace('\r', ' ').Replace('\n', ' ') + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch { }
    }
}
#endif
