using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace DSCons.RevitMcp.Contracts;

/// <summary>
/// Stable loader/runtime boundary. The Revit loader owns the assembly lifetime;
/// the runtime owns the bridge, command registry and Revit API workflows.
/// Keep this contract free of Revit API types so it can be shared by every target.
/// </summary>
public interface IRevitMcpRuntime : IDisposable
{
    string RuntimeVersion { get; }
    string StatusText { get; }
    bool IsRunning { get; }
    void Initialize(object uiApplication);
    void SetReloadCallback(Action callback);
    void Start();
    void Stop();
}

public static class McpConstants
{
    public const string ServerName = "dscons-revit-mcp";
    public const string ProtocolVersion = "2024-11-05";
    public const int DefaultPort = 43827;
    public const int PreviewLifetimeSeconds = 90;
    // Revit preview simulation may commit then roll back a TransactionGroup.
    // On models with many registered updaters/warnings this legitimately takes
    // longer than a normal read request, so transport must not abandon it early.
    public const int RequestTimeoutSeconds = 120;

    public static string CreateSessionSecret()
    {
        var bytes = new byte[32];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }
}

public static class ErrorCodes
{
    public const string Unauthorized = "Unauthorized";
    public const string NoDocument = "NoDocument";
    public const string Unsupported = "Unsupported";
    public const string InvalidParam = "InvalidParam";
    public const string ReadOnly = "ReadOnly";
    public const string CentralBlocked = "CentralBlocked";
    public const string OwnershipBlocked = "OwnershipBlocked";
    public const string ProtectedElement = "ProtectedElement";
    public const string PreviewExpired = "PreviewExpired";
    public const string PreviewInvalid = "PreviewInvalid";
    public const string ContextInvalid = "ContextInvalid";
    public const string TransactionFailed = "TransactionFailed";
    public const string BridgeUnavailable = "BridgeUnavailable";
    public const string Timeout = "Timeout";
    public const string PathBlocked = "PathBlocked";
    public const string DocumentTypeInvalid = "DocumentTypeInvalid";
    public const string TemplateInvalid = "TemplateInvalid";
    public const string FileConflict = "FileConflict";
    public const string VerificationFailed = "VerificationFailed";
    public const string SourceUnreadable = "SourceUnreadable";
    public const string SourceEncrypted = "SourceEncrypted";
    public const string OcrUnavailable = "OcrUnavailable";
    public const string EvidenceInvalid = "EvidenceInvalid";
    public const string EvidenceConflict = "EvidenceConflict";
}

public static class ToolScopes
{
    public const string Session = "session";
    public const string Document = "document";
    public const string View = "view";
    public const string Selection = "selection";
    public const string Elements = "elements";
}

public sealed class BridgeRequest
{
    public string JsonRpc { get; set; } = "2.0";
    public string Id { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string ParamsJson { get; set; } = "{}";
    public string ClientName { get; set; } = "unknown";
}

public sealed class BridgeResponse
{
    public string JsonRpc { get; set; } = "2.0";
    public string Id { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string ResultJson { get; set; } = "{}";
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

public sealed class ToolCapability
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsWrite { get; set; }
    public bool IsDestructive { get; set; }
    public bool IsAvailable { get; set; } = true;
    /// <summary>What Revit state the result or operation is anchored to.</summary>
    public string Scope { get; set; } = ToolScopes.Document;
    /// <summary>Whether callers must obtain live context in this turn before use.</summary>
    public bool RequiresFreshContext { get; set; }
    /// <summary>Human/agent-readable preconditions that must be met before invoking the tool.</summary>
    public List<string> Prerequisites { get; set; } = new List<string>();
    /// <summary>Known operational boundaries; callers must surface these instead of guessing around them.</summary>
    public List<string> Limitations { get; set; } = new List<string>();
}

public sealed class SessionInfo
{
    public int Port { get; set; } = McpConstants.DefaultPort;
    public string Secret { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
}

public sealed class PreviewToken
{
    public string PreviewId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
    public string DocumentFingerprint { get; set; } = string.Empty;
    public string TargetFingerprint { get; set; } = string.Empty;
    /// <summary>Fingerprint for an external resource such as a Family template
    /// or .rfa file. It prevents a preview from being applied after its source
    /// file or approved output location changes.</summary>
    public string ResourceFingerprint { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

public sealed class ToolCache
{
    public DateTimeOffset CachedAtUtc { get; set; }
    public List<ToolCapability> Tools { get; set; } = new List<ToolCapability>();
}
