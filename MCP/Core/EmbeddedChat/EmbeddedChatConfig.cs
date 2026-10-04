#if REVIT2023 || REVIT2025
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DSCons.RevitMcp.Core.EmbeddedChat;

internal sealed class EmbeddedChatConfig
{
    public const string FileName = "DSCons.RevitMcp.chat.json";
    // This exact version has passed the DSCons read-only app-server probe
    // (initialize, account/read, model/list and isolated MCP inventory).
    // Keep an exact pin: an untested future CLI must still fail closed.
    public const string SupportedCliVersion = "0.154.0-alpha.6.2";

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("verifiedCodexCliVersion")]
    public string VerifiedCodexCliVersion { get; set; } = SupportedCliVersion;

    [JsonProperty("mcpServerEntrypoint")]
    public string McpServerEntrypoint { get; set; } = string.Empty;

    [JsonProperty("codexCliPath")]
    public string? CodexCliPath { get; set; }

    [JsonProperty("nodeExecutablePath")]
    public string? NodeExecutablePath { get; set; }

    [JsonProperty("claudeCliPath")]
    public string? ClaudeCliPath { get; set; }

    [JsonProperty("antigravityCliPath")]
    public string? AntigravityCliPath { get; set; }

    [JsonProperty("capabilityManifestPath")]
    public string? CapabilityManifestPath { get; set; }

    [JsonIgnore]
    public EmbeddedCapabilityManifest CapabilityManifest { get; private set; } = null!;

    public static EmbeddedChatConfig Load()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Không xác định được thư mục add-in.");
        var path = Path.Combine(assemblyDirectory, FileName);
        if (!File.Exists(path))
            throw new FileNotFoundException("Chat AI chưa được cấu hình. Hãy chạy lại bộ cài DSCons MCP cho Revit 2023 hoặc 2025.", path);
        var config = JsonConvert.DeserializeObject<EmbeddedChatConfig>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("Cấu hình Chat AI không hợp lệ.");
        if (!config.Enabled) throw new InvalidOperationException("Chat AI đang bị tắt trong cấu hình bản thử.");
        if (string.IsNullOrWhiteSpace(config.McpServerEntrypoint) || !File.Exists(config.McpServerEntrypoint))
            throw new FileNotFoundException("Không tìm thấy DSCons MCP Server đã build.", config.McpServerEntrypoint);
        config.CapabilityManifestPath ??= Path.Combine(Path.GetDirectoryName(config.McpServerEntrypoint)!, "embedded-capabilities.json");
        config.CapabilityManifest = EmbeddedCapabilityManifest.Load(config.CapabilityManifestPath);
        return config;
    }
}

/// <summary>
/// Immutable build output from MCP-Server. Tool count is presentation-only;
/// the exact name set plus the SHA-256-bound canonical schema are the policy.
/// </summary>
internal sealed class EmbeddedCapabilityManifest
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; }
    [JsonProperty("toolCount")]
    public int ToolCount { get; set; }
    [JsonProperty("tools")]
    public List<EmbeddedToolCapability> Tools { get; set; } = new();
    [JsonProperty("canonicalPayload")]
    public string CanonicalPayload { get; set; } = string.Empty;
    [JsonProperty("schemaFingerprintSha256")]
    public string SchemaFingerprintSha256 { get; set; } = string.Empty;
    [JsonIgnore]
    public IReadOnlyCollection<string> ToolNames => Tools.Select(tool => tool.Name).ToArray();

    public static EmbeddedCapabilityManifest Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new FileNotFoundException("Không tìm thấy capability manifest của DSCons MCP.", path);
        var manifest = JsonConvert.DeserializeObject<EmbeddedCapabilityManifest>(File.ReadAllText(path, Encoding.UTF8))
            ?? throw new InvalidOperationException("Capability manifest của DSCons MCP không hợp lệ.");
        if (manifest.SchemaVersion != 1 || manifest.ToolCount <= 0 || manifest.ToolCount != manifest.Tools.Count || string.IsNullOrWhiteSpace(manifest.CanonicalPayload)
            || !System.Text.RegularExpressions.Regex.IsMatch(manifest.SchemaFingerprintSha256, "^[a-f0-9]{64}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Capability manifest DSCons thiếu schema hoặc fingerprint hợp lệ.");
        using var sha = SHA256.Create();
        var actual = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(manifest.CanonicalPayload)).Select(value => value.ToString("x2")));
        if (!string.Equals(actual, manifest.SchemaFingerprintSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("Capability manifest DSCons có fingerprint không khớp.");
        var payload = JObject.Parse(manifest.CanonicalPayload);
        var payloadTools = payload["tools"] as JArray ?? throw new InvalidOperationException("Capability manifest DSCons có canonical payload không hợp lệ.");
        var declared = manifest.Tools.OrderBy(tool => tool.Name, StringComparer.Ordinal).Select(tool => tool.Name + "\n" + tool.IsWrite).ToArray();
        var canonical = payloadTools.OfType<JObject>().OrderBy(tool => tool.Value<string>("name"), StringComparer.Ordinal)
            .Select(tool => (tool.Value<string>("name") ?? string.Empty) + "\n" + (tool.Value<bool?>("isWrite") == true)).ToArray();
        if (declared.Length != canonical.Length || declared.Length == 0 || declared.Any(string.IsNullOrWhiteSpace) || declared.Distinct(StringComparer.Ordinal).Count() != declared.Length || !declared.SequenceEqual(canonical, StringComparer.Ordinal))
            throw new InvalidOperationException("Capability manifest DSCons có inventory công cụ không nhất quán.");
        return manifest;
    }
}

internal sealed class EmbeddedToolCapability
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
    [JsonProperty("isWrite")]
    public bool IsWrite { get; set; }
}
#endif
