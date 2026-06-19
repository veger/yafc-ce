namespace Yafc.Core;

/// <summary>
/// An <see cref="IProgress{T}"/> that initially discards the received progress reports, but can be updated to forward them to another implementation.
/// </summary>
/// <typeparam name="T"></typeparam>
public class ProgressForwarder<T> : IProgress<T> {
    /// <summary>
    /// Gets or sets the <see cref="IProgress{T}"/> that will receive updates sent to this <see cref="ProgressForwarder{T}"/>. If
    /// <see langword="null"/>, reports will not be forwarded.
    /// </summary>
    public IProgress<T>? Destination { get; set; }
    void IProgress<T>.Report(T value) => Destination?.Report(value);
}
