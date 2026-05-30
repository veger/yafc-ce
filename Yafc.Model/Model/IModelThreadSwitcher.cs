namespace Yafc.Model;

/// <summary>
/// Provides awaitable switches between the model's foreground and background execution contexts.
/// </summary>
public interface IModelThreadSwitcher {
    /// <summary>
    /// Returns an awaitable switch from the foreground context to the background context.
    /// </summary>
    ModelThreadSwitch SwitchToBackground();

    /// <summary>
    /// Returns an awaitable switch from the background context to the foreground context.
    /// </summary>
    ModelThreadSwitch SwitchToForeground();
}
