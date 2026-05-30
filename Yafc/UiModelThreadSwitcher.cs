using System;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

/// <summary>
/// Switches model execution between the UI foreground thread and the background thread pool.
/// </summary>
public sealed class UiModelThreadSwitcher : IModelThreadSwitcher {
    /// <summary>
    /// Gets the shared UI switcher instance.
    /// </summary>
    public static UiModelThreadSwitcher Instance { get; } = new();

    private UiModelThreadSwitcher() { }

    /// <inheritdoc />
    public ModelThreadSwitch SwitchToBackground() => new(ExitMainThreadSwitch.Instance);

    /// <inheritdoc />
    public ModelThreadSwitch SwitchToForeground() => new(EnterMainThreadSwitch.Instance);

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
