namespace PayloadApp.DebugClient;

public class MessageHandler<T> : Microsoft.Azure.SpaceFx.Core.IMessageHandler<T> where T : notnull {
    private readonly ILogger<MessageHandler<T>> _logger;
    private readonly Microsoft.Azure.SpaceFx.Core.Services.PluginLoader _pluginLoader;
    private readonly IServiceProvider _serviceProvider;
    public MessageHandler(ILogger<MessageHandler<T>> logger, Microsoft.Azure.SpaceFx.Core.Services.PluginLoader pluginLoader, IServiceProvider serviceProvider) {
        _logger = logger;
        _pluginLoader = pluginLoader;
        _serviceProvider = serviceProvider;
    }

    public void MessageReceived(T message, Microsoft.Azure.SpaceFx.MessageFormats.Common.DirectToApp fullMessage) {
        using (var scope = _serviceProvider.CreateScope()) {
            _logger.LogInformation($"Found {typeof(T).Name}");

            switch (typeof(T).Name) {
                case string messageType when messageType.Equals(typeof(LinkResponse).Name, StringComparison.CurrentCultureIgnoreCase):
                    LinkResponseHandler(response: message as LinkResponse);
                    break;
            }
        }
    }


    public void LinkResponseHandler(LinkResponse response) {
        _logger.LogInformation($"LinkResponse Heard from {response.ResponseHeader.AppId}");
        _logger.LogInformation($"LinkResponse: {response.ResponseHeader.Status}. TrackingID: {response.ResponseHeader.TrackingId}");
    }
}