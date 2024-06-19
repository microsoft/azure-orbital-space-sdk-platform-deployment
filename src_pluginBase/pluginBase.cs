namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment.Plugins;
public abstract class PluginBase : Microsoft.Azure.SpaceFx.Core.IPluginBase, IPluginBase {
    public abstract ILogger Logger { get; set; }
    public abstract Task BackgroundTask();
    public abstract void ConfigureLogging(ILoggerFactory loggerFactory);
    public abstract Task<MessageFormats.Common.PluginHealthCheckResponse> PluginHealthCheckResponse();
    public abstract Task<(MessageFormats.PlatformServices.Deployment.DeployRequest?, MessageFormats.PlatformServices.Deployment.DeployResponse?)> PreKubernetesDeployment(MessageFormats.PlatformServices.Deployment.DeployRequest? input_request, MessageFormats.PlatformServices.Deployment.DeployResponse? input_response);
    public abstract Task<(MessageFormats.PlatformServices.Deployment.DeployRequest?, MessageFormats.PlatformServices.Deployment.DeployResponse?)> PostKubernetesDeployment(MessageFormats.PlatformServices.Deployment.DeployRequest? input_request, MessageFormats.PlatformServices.Deployment.DeployResponse? input_response);
    public abstract Task<Google.Protobuf.WellKnownTypes.StringValue?> ProcessScheduleFile(Google.Protobuf.WellKnownTypes.StringValue? input_request);
}

public interface IPluginBase {
    ILogger Logger { get; set; }

    Task<MessageFormats.Common.PluginHealthCheckResponse> PluginHealthCheckResponse();
    Task<(MessageFormats.PlatformServices.Deployment.DeployRequest?, MessageFormats.PlatformServices.Deployment.DeployResponse?)> PreKubernetesDeployment(MessageFormats.PlatformServices.Deployment.DeployRequest? input_request, MessageFormats.PlatformServices.Deployment.DeployResponse? input_response);
    Task<(MessageFormats.PlatformServices.Deployment.DeployRequest?, MessageFormats.PlatformServices.Deployment.DeployResponse?)> PostKubernetesDeployment(MessageFormats.PlatformServices.Deployment.DeployRequest? input_request, MessageFormats.PlatformServices.Deployment.DeployResponse? input_response);
    Task<Google.Protobuf.WellKnownTypes.StringValue?> ProcessScheduleFile(Google.Protobuf.WellKnownTypes.StringValue? input_request);
}
