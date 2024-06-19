namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment;

public partial class PluginDelegates {
    internal StringValue? ProcessScheduleFile((StringValue? input_request, Plugins.PluginBase plugin) input) {
        const string methodName = nameof(input.plugin.ProcessScheduleFile);

        if (input.input_request is null || input.input_request is default(StringValue)) {
            _logger.LogDebug("Plugin {pluginName} / {pluginMethod}: Received empty input.  Returning empty results", input.plugin.ToString(), methodName);
            return input.input_request;
        }
        _logger.LogDebug("Plugin {pluginMethod}: START", methodName);

        try {
            Task<StringValue?> pluginTask = input.plugin.ProcessScheduleFile(input_request: input.input_request);
            pluginTask.Wait();
            input.input_request = pluginTask.Result;
        } catch (Exception ex) {
            _logger.LogError("Plugin {pluginName} / {pluginMethod}: Error: {errorMessage}", input.plugin.ToString(), methodName, ex.Message);
        }

        _logger.LogDebug("Plugin {pluginName} / {pluginMethod}: END", input.plugin.ToString(), methodName);
        return input.input_request;
    }
}