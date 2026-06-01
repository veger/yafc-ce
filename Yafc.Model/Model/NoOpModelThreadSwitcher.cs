namespace Yafc.Model;

/// <summary>
/// Completes all model thread switches synchronously for hosts that do not need thread switching.
/// </summary>
public sealed class NoOpModelThreadSwitcher : IModelThreadSwitcher {
    /// <summary>
    /// Gets the shared no-op switcher instance.
    /// </summary>
    public static NoOpModelThreadSwitcher Instance { get; } = new();

    private NoOpModelThreadSwitcher() { }

    /// <inheritdoc />
    public ModelThreadSwitch SwitchToBackground() => default;

    /// <inheritdoc />
    public ModelThreadSwitch SwitchToForeground() => default;
}
