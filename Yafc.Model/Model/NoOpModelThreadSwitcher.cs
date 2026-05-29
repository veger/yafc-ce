namespace Yafc.Model;

public sealed class NoOpModelThreadSwitcher : IModelThreadSwitcher {
    public static NoOpModelThreadSwitcher Instance { get; } = new();

    private NoOpModelThreadSwitcher() { }

    public ModelThreadSwitch SwitchToBackground() => default;

    public ModelThreadSwitch SwitchToModelThread() => default;
}
