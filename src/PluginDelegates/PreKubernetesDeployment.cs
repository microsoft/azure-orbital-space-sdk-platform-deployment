namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment;

public partial class PluginDelegates {
    internal (MessageFormats.PlatformServices.Deployment.DeployRequest? output_request, MessageFormats.PlatformServices.Deployment.DeployResponse? output_response) PreKubernetesDeployment((MessageFormats.PlatformServices.Deployment.DeployRequest? input_request, MessageFormats.PlatformServices.Deployment.DeployResponse? input_response, Plugins.PluginBase plugin) input) {
        const string methodName = nameof(input.plugin.PreKubernetesDeployment);
        if (input.input_request is null || input.input_request is default(MessageFormats.PlatformServices.Deployment.DeployRequest)) {
            _logger.LogDebug("Plugin {Plugin_Name} / {methodName}: Received empty input.  Returning empty results", input.plugin.ToString(), methodName);
            return (input.input_request, input.input_response);
        }
        _logger.LogDebug("Plugin {Plugin_Name} / {methodName}: START", input.plugin.ToString(), methodName);

        try {
            Task<(MessageFormats.PlatformServices.Deployment.DeployRequest? output_request,
                    MessageFormats.PlatformServices.Deployment.DeployResponse? output_response)> pluginTask = input.plugin.PreKubernetesDeployment(input_request: input.input_request, input_response: input.input_response);
            pluginTask.Wait();

            input.input_request = pluginTask.Result.output_request;
            input.input_response = pluginTask.Result.output_response;
        } catch (Exception ex) {
            _logger.LogError("Error in plugin '{Plugin_Name}:{methodName}'.  Error: {errMsg}", input.plugin.ToString(), methodName, ex.Message);
        }

        _logger.LogDebug("Plugin {Plugin_Name} / {methodName}: END", input.plugin.ToString(), methodName);
        return (input.input_request, input.input_response);
    }
}
