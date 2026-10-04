using System.Collections.Concurrent;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Contracts;

namespace DSCons.RevitMcp.Core;

internal sealed class ExternalEventBridge : IExternalEventHandler, IDisposable
{
    private readonly ConcurrentQueue<PendingInvocation> _queue = new();
    private readonly ExternalEvent _event;
    private int _disposed;
    public ExternalEventBridge() { _event = ExternalEvent.Create(this); }
    public string GetName() => "DSCons Revit MCP ExternalEvent bridge";
    public void Execute(UIApplication application)
    {
        while (_queue.TryDequeue(out var item))
        {
            if (item.Completion.Task.IsCompleted) continue;
            try { item.Completion.TrySetResult(JsonRpcDispatcher.Dispatch(application, item.Request)); }
            catch (Exception ex) { item.Completion.TrySetResult(BridgeServer.Failure(item.Request.Id, ErrorCodes.TransactionFailed, ex.Message)); }
        }
    }
    public Task<BridgeResponse> InvokeAsync(BridgeRequest request)
    {
        var item = new PendingInvocation(request);
        if (Volatile.Read(ref _disposed) != 0)
        {
            item.Completion.TrySetResult(BridgeServer.Failure(request.Id, ErrorCodes.BridgeUnavailable, "Revit MCP runtime is reloading or stopped."));
            return item.Completion.Task;
        }
        _queue.Enqueue(item);
        try { _event.Raise(); }
        catch (Exception ex)
        {
            _queue.TryDequeue(out _);
            item.Completion.TrySetResult(BridgeServer.Failure(request.Id, ErrorCodes.BridgeUnavailable, "Revit cannot accept an ExternalEvent request: " + ex.Message));
        }
        return item.Completion.Task;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        while (_queue.TryDequeue(out var item))
            item.Completion.TrySetResult(BridgeServer.Failure(item.Request.Id, ErrorCodes.BridgeUnavailable, "Revit MCP runtime was reloaded before the request completed."));
        _event.Dispose();
    }
    private sealed class PendingInvocation
    {
        public PendingInvocation(BridgeRequest request) { Request = request; }
        public BridgeRequest Request { get; }
        public TaskCompletionSource<BridgeResponse> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
