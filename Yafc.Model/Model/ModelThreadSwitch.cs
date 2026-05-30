using System;
using System.Runtime.CompilerServices;

namespace Yafc.Model;

/// <summary>
/// Represents an awaitable switch between model execution contexts.
/// </summary>
public readonly struct ModelThreadSwitch {
    private readonly IModelThreadSwitchAwaitable? awaitable;

    /// <summary>
    /// Initializes a new switch that delegates to <paramref name="awaitable"/>.
    /// </summary>
    /// <param name="awaitable">The awaitable that performs the context switch, or <see langword="null" /> to complete synchronously.</param>
    public ModelThreadSwitch(IModelThreadSwitchAwaitable? awaitable) => this.awaitable = awaitable;

    /// <summary>
    /// Gets the awaiter used by the C# await pattern.
    /// </summary>
    /// <returns>The awaiter for this switch.</returns>
    public ModelThreadSwitchAwaiter GetAwaiter() => new(awaitable?.GetAwaiter());
}

/// <summary>
/// Provides completion notifications for a <see cref="ModelThreadSwitch"/>.
/// </summary>
public readonly struct ModelThreadSwitchAwaiter : INotifyCompletion {
    private readonly IModelThreadSwitchAwaiter? awaiter;

    internal ModelThreadSwitchAwaiter(IModelThreadSwitchAwaiter? awaiter) => this.awaiter = awaiter;

    /// <summary>
    /// Gets a value indicating whether the switch has already completed.
    /// </summary>
    public bool IsCompleted => awaiter?.IsCompleted ?? true;

    /// <summary>
    /// Completes the switch and propagates any exception from the underlying awaiter.
    /// </summary>
    public void GetResult() => awaiter?.GetResult();

    /// <inheritdoc />
    public void OnCompleted(Action continuation) {
        if (awaiter == null) {
            continuation();
            return;
        }

        awaiter.OnCompleted(continuation);
    }
}

/// <summary>
/// Supplies an awaiter for a model thread switch operation.
/// </summary>
public interface IModelThreadSwitchAwaitable {
    /// <summary>
    /// Gets the awaiter that performs the switch.
    /// </summary>
    /// <returns>The awaiter for the switch operation.</returns>
    IModelThreadSwitchAwaiter GetAwaiter();
}

/// <summary>
/// Defines the awaiter contract used by model thread switch implementations.
/// </summary>
public interface IModelThreadSwitchAwaiter : INotifyCompletion {
    /// <summary>
    /// Gets a value indicating whether the switch has already completed.
    /// </summary>
    bool IsCompleted { get; }

    /// <summary>
    /// Completes the switch and propagates any exception from the implementation.
    /// </summary>
    void GetResult();
}
