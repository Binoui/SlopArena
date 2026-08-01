// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    // Polyfill: record init-only setters emit a call to this attribute's
    // constructor. It is defined in .NET 5+ runtimes but not in
    // netstandard2.1, so we declare it here so Shared can use `record`.
    // The runtime ignores the attribute body; only the symbol's existence matters.
    internal static class IsExternalInit { }
}
