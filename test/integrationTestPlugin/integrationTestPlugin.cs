using Google.Protobuf.WellKnownTypes;
using Microsoft.Azure.SpaceFx.MessageFormats.PlatformServices.Deployment;

namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment.Plugins;
public class IntegrationTestPlugin : Microsoft.Azure.SpaceFx.PlatformServices.Deployment.Plugins.PluginBase {
    public override ILogger Logger { get; set; }

    public IntegrationTestPlugin() {
        LoggerFactory loggerFactory = new();
        Logger = loggerFactory.CreateLogger<IntegrationTestPlugin>();
    }

    public override Task BackgroundTask() => Task.CompletedTask;

    public override void ConfigureLogging(ILoggerFactory loggerFactory) => Logger = loggerFactory.CreateLogger<IntegrationTestPlugin>();

    public override Task<PluginHealthCheckResponse> PluginHealthCheckResponse() => Task.FromResult(new MessageFormats.Common.PluginHealthCheckResponse());

    public override Task<StringValue?> ProcessScheduleFile(StringValue? input_request) => Task.Run(() => {
        if (input_request == null) return input_request;
        return input_request;
    });

    public override Task<(DeployRequest?, DeployResponse?)> PreKubernetesDeployment(DeployRequest? input_request, DeployResponse? input_response) => Task.Run(() => {
        if (input_request == null || input_response == null) return (input_request, input_response);
        input_response.ResponseHeader.Status = MessageFormats.Common.StatusCodes.Successful;
        return (input_request, input_response);
    });

    public override Task<(DeployRequest?, DeployResponse?)> PostKubernetesDeployment(DeployRequest? input_request, DeployResponse? input_response) => Task.Run(() => {
        if (input_request == null || input_response == null) return (input_request, input_response);
        input_response.ResponseHeader.Status = MessageFormats.Common.StatusCodes.Successful;
        return (input_request, input_response);
    });

}
