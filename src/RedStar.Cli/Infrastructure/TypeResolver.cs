using Microsoft.Extensions.DependencyInjection;

using Spectre.Console.Cli;

namespace RedStar.Cli.Infrastructure;

public sealed class TypeResolver(IServiceProvider provider) : ITypeResolver, IDisposable
{
    public object? Resolve(Type? type) => type == null ? null : provider.GetService(type);

    public void Dispose() => (provider as IDisposable)?.Dispose();
}