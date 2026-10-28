namespace Middleware.Worker.Services;

/// <summary>
/// Runtime collection gate. In middleware-command mode it is controlled from the
/// Worker console; in PLC-symbol mode it follows the configured PLC Boolean.
/// </summary>
public sealed class TwinCatTelemetryControl(bool initialEnabled)
{
    private readonly object sync = new();
    private bool enabled = initialEnabled;
    private TaskCompletionSource changed = NewSignal();

    public bool IsEnabled
    {
        get
        {
            lock (sync)
            {
                return enabled;
            }
        }
    }

    public bool SetEnabled(bool value)
    {
        TaskCompletionSource signal;
        lock (sync)
        {
            if (enabled == value)
            {
                return false;
            }

            enabled = value;
            signal = changed;
            changed = NewSignal();
        }

        signal.TrySetResult();
        return true;
    }

    public Task WaitForChangeAsync(CancellationToken cancellationToken)
    {
        lock (sync)
        {
            return changed.Task.WaitAsync(cancellationToken);
        }
    }

    public Task WaitUntilEnabledAsync(CancellationToken cancellationToken)
    {
        lock (sync)
        {
            return enabled ? Task.CompletedTask : changed.Task.WaitAsync(cancellationToken);
        }
    }

    public async Task WaitForChangeOrTimeoutAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var changeTask = WaitForChangeAsync(waitCancellation.Token);
        var timeoutTask = Task.Delay(timeout, cancellationToken);
        await Task.WhenAny(changeTask, timeoutTask);
        cancellationToken.ThrowIfCancellationRequested();
        waitCancellation.Cancel();
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
