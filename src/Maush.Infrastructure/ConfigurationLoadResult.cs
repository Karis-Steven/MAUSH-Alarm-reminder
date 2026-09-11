using Maush.Core;

namespace Maush.Infrastructure;

public sealed record ConfigurationLoadResult(AppConfiguration Configuration, string? Error)
{
    public bool IsSuccess => Error is null;
}
