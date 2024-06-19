namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment;

public partial class PluginDelegates {
    private readonly ILogger<PluginDelegates> _logger;
    private readonly List<Core.Models.PLUG_IN> _plugins;
    public PluginDelegates(ILogger<PluginDelegates> logger, IServiceProvider serviceProvider) {
        _logger = logger;
        _plugins = serviceProvider.GetService<List<Core.Models.PLUG_IN>>() ?? new List<Core.Models.PLUG_IN>();
    }
}