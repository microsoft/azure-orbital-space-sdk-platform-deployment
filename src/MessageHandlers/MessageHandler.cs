namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment;

public partial class MessageHandler<T> : Microsoft.Azure.SpaceFx.Core.IMessageHandler<T> where T : notnull {
    private readonly ILogger<MessageHandler<T>> _logger;
    public static EventHandler<T>? MessageReceivedEvent;
    private readonly Microsoft.Azure.SpaceFx.Core.Services.PluginLoader _pluginLoader;
    private readonly IServiceProvider _serviceProvider;
    private readonly Core.Client _client;
    private readonly Models.APP_CONFIG _appConfig;
    private readonly PluginDelegates _pluginDelegates;

    public MessageHandler(ILogger<MessageHandler<T>> logger, PluginDelegates pluginDelegates, Microsoft.Azure.SpaceFx.Core.Services.PluginLoader pluginLoader, IServiceProvider serviceProvider, Core.Client client) {
        _logger = logger;
        _pluginDelegates = pluginDelegates;
        _pluginLoader = pluginLoader;
        _serviceProvider = serviceProvider;
        _client = client;

        _appConfig = new Models.APP_CONFIG();
    }

    public void MessageReceived(T message, MessageFormats.Common.DirectToApp fullMessage) => Task.Run(() => {
        using (var scope = _serviceProvider.CreateScope()) {

            if (message == null || EqualityComparer<T>.Default.Equals(message, default)) {
                _logger.LogInformation("Received empty message '{messageType}' from '{appId}'.  Discarding message.", typeof(T).Name, fullMessage.SourceAppId);
                return;
            }

            // This function is just a catch all for any messages that come in.  They are not processed and no plugins are triggered for security reasons.
            // We're catching all messages here to reduce the log warnings for OOTB messages that are flowing
        }
    });
}