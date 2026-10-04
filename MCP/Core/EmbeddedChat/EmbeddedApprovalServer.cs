#if REVIT2023 || REVIT2025
using Newtonsoft.Json;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace DSCons.RevitMcp.Core.EmbeddedChat;

internal sealed class ApprovalPrompt
{
    public string RequestId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public int ExpectedPid { get; set; }
    public string Secret { get; set; } = string.Empty;
    public TaskCompletionSource<bool> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class EmbeddedApprovalServer : IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<string> _usedRequestIds = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private NamedPipeServerStream? _activePipe;
    private ApprovalPrompt? _pendingPrompt;
    private Task? _listenTask;

    public string PipeName { get; } = "dscons-chat-" + Guid.NewGuid().ToString("N");
    public string Secret { get; } = Convert.ToBase64String(RandomBytes(32));
    public ApprovalPrompt? PendingPrompt { get { lock (_gate) return _pendingPrompt; } }
    public event Action<ApprovalPrompt>? ApprovalRequested;

    public void Start() => _listenTask ??= Task.Run(ListenLoopAsync);

    private async Task ListenLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            ApprovalPrompt? currentPrompt = null;
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                lock (_gate) _activePipe = pipe;
                await pipe.WaitForConnectionAsync().ConfigureAwait(false);
                if (_shutdown.IsCancellationRequested) break;
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                var prompt = string.IsNullOrWhiteSpace(line) ? null : JsonConvert.DeserializeObject<ApprovalPrompt>(line);
                var approved = false;
                var reason = "Yêu cầu xác nhận không hợp lệ.";
                if (prompt != null && Validate(prompt, out reason))
                {
                    currentPrompt = prompt;
                    lock (_gate) _pendingPrompt = prompt;
                    ApprovalRequested?.Invoke(prompt);
                    var completed = await Task.WhenAny(prompt.Decision.Task, Task.Delay(TimeSpan.FromMinutes(10), _shutdown.Token)).ConfigureAwait(false);
                    if (completed == prompt.Decision.Task)
                    {
                        approved = prompt.Decision.Task.Result;
                        reason = approved ? "Học viên đã xác nhận." : "Học viên đã hủy thao tác ghi.";
                    }
                    else reason = "Đã hết thời gian chờ học viên xác nhận.";
                }
                await writer.WriteLineAsync(JsonConvert.SerializeObject(new { requestId = prompt?.RequestId, hash = prompt?.Hash, approved, reason })).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested) { break; }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { break; }
            catch { if (!_shutdown.IsCancellationRequested) await Task.Delay(250).ConfigureAwait(false); }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_pendingPrompt, currentPrompt)) _pendingPrompt = null;
                    _activePipe = null;
                }
            }
        }
    }

    private bool Validate(ApprovalPrompt prompt, out string reason)
    {
        if (!FixedEquals(Secret, prompt.Secret)) { reason = "Kênh xác nhận không hợp lệ."; return false; }
        if (prompt.ExpectedPid != System.Diagnostics.Process.GetCurrentProcess().Id) { reason = "Yêu cầu thuộc phiên Revit khác."; return false; }
        var expected = Sha256(prompt.ToolName + "\n" + prompt.ArgumentsJson);
        if (!FixedEquals(expected, prompt.Hash)) { reason = "Nội dung yêu cầu đã thay đổi."; return false; }
        lock (_gate)
        {
            if (!_usedRequestIds.Add(prompt.RequestId)) { reason = "Yêu cầu xác nhận đã được dùng."; return false; }
        }
        reason = string.Empty;
        return true;
    }

    private static bool FixedEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left ?? string.Empty);
        var b = Encoding.UTF8.GetBytes(right ?? string.Empty);
        if (a.Length != b.Length) return false;
        var difference = 0;
        for (var index = 0; index < a.Length; index++) difference |= a[index] ^ b[index];
        return difference == 0;
    }

    private static string Sha256(string value)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(b => b.ToString("x2")));
    }

    private static byte[] RandomBytes(int count) { var bytes = new byte[count]; using var rng = RandomNumberGenerator.Create(); rng.GetBytes(bytes); return bytes; }

    public void Dispose()
    {
        _shutdown.Cancel();
        lock (_gate) _activePipe?.Dispose();
        try { _listenTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _shutdown.Dispose();
    }
}
#endif
