using System;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

public sealed class UiModelThreadSwitcher : IModelThreadSwitcher {
    public static UiModelThreadSwitcher Instance { get; } = new();

    private UiModelThreadSwitcher() { }

    public ModelThreadSwitch SwitchToBackground() => new(ExitMainThreadSwitch.Instance);

    public ModelThreadSwitch SwitchToModelThread() => new(EnterMainThreadSwitch.Instance);

    private sealed class ExitMainThreadSwitch : IModelThreadSwitchAwaitable, IModelThreadSwitchAwaiter {
        public static ExitMainThreadSwitch Instance { get; } = new();

        public IModelThreadSwitchAwaiter GetAwaiter() => this;
        public bool IsCompleted => Ui.ExitMainThread().GetAwaiter().IsCompleted;
        public void GetResult() => Ui.ExitMainThread().GetAwaiter().GetResult();
        public void OnCompleted(Action continuation) => Ui.ExitMainThread().GetAwaiter().OnCompleted(continuation);
    }

    private sealed class EnterMainThreadSwitch : IModelThreadSwitchAwaitable, IModelThreadSwitchAwaiter {
        public static EnterMainThreadSwitch Instance { get; } = new();

        public IModelThreadSwitchAwaiter GetAwaiter() => this;
        public bool IsCompleted => Ui.EnterMainThread().GetAwaiter().IsCompleted;
        public void GetResult() => Ui.EnterMainThread().GetAwaiter().GetResult();
        public void OnCompleted(Action continuation) => Ui.EnterMainThread().GetAwaiter().OnCompleted(continuation);
    }
}
