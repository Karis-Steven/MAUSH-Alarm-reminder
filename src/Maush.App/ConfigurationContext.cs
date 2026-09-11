using Maush.Core;

namespace Maush.App;

public sealed class ConfigurationContext(AppConfiguration configuration)
{
    public AppConfiguration Current { get; set; } = configuration;
}
