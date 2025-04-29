using System.Diagnostics.CodeAnalysis;

namespace Yafc.I18n;

public interface ILocalizable {
    bool Get([NotNullWhen(true)] out string? key, [NotNullWhen(true)] out object[]? parameters);
}
