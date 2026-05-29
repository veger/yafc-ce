using System;
using System.Runtime.CompilerServices;

namespace Yafc.Model;

public interface IModelThreadSwitcher {
    ModelThreadSwitch SwitchToBackground();
    ModelThreadSwitch SwitchToModelThread();
}

public readonly struct ModelThreadSwitch {
    private readonly IModelThreadSwitchAwaitable? awaitable;

    public ModelThreadSwitch(IModelThreadSwitchAwaitable awaitable) => this.awaitable = awaitable ?? throw new ArgumentNullException(nameof(awaitable));

    public ModelThreadSwitchAwaiter GetAwaiter() => new(awaitable?.GetAwaiter());
}

public readonly struct ModelThreadSwitchAwaiter : INotifyCompletion {
    private readonly IModelThreadSwitchAwaiter? awaiter;

    internal ModelThreadSwitchAwaiter(IModelThreadSwitchAwaiter? awaiter) => this.awaiter = awaiter;

    public bool IsCompleted => awaiter?.IsCompleted ?? true;
    public void GetResult() => awaiter?.GetResult();
    public void OnCompleted(Action continuation) {
        if (awaiter == null) {
            continuation();
            return;
        }

        awaiter.OnCompleted(continuation);
    }
}

public interface IModelThreadSwitchAwaitable {
    IModelThreadSwitchAwaiter GetAwaiter();
}

public interface IModelThreadSwitchAwaiter : INotifyCompletion {
    bool IsCompleted { get; }
    void GetResult();
}

public sealed class NoOpModelThreadSwitcher : IModelThreadSwitcher {
    public static NoOpModelThreadSwitcher Instance { get; } = new();

    private NoOpModelThreadSwitcher() { }

    public ModelThreadSwitch SwitchToBackground() => default;

    public ModelThreadSwitch SwitchToModelThread() => default;
}
