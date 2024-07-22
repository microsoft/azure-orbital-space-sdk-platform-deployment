using k8s.Models;

namespace Microsoft.Azure.SpaceFx.PlatformServices.Deployment.IntegrationTests.Tests;

[Collection(nameof(TestSharedContext))]
public class DeploymentTests : IClassFixture<TestSharedContext> {
    readonly TestSharedContext _context;

    public DeploymentTests(TestSharedContext context) {
        _context = context;
    }

    [Fact]
    public async Task DeployAnApp() {
        const string testName = nameof(DeployAnApp);
        k8s.Models.V1Pod? integrationPod = null;
        DateTime maxTimeToWait = DateTime.Now.Add(TestSharedContext.MAX_TIMESPAN_TO_WAIT_FOR_MSG);
        MessageFormats.HostServices.Link.LinkResponse? jsonFileResponse = null;
        MessageFormats.HostServices.Link.LinkResponse? yamlFileResponse = null;
        MessageFormats.HostServices.Link.LinkResponse? tarFileResponse = null;
        MessageFormats.HostServices.Link.LinkResponse? jpgFileResponse = null;
        k8s.Models.V1PodList podList;

        string trackingId = Guid.NewGuid().ToString();

        Console.WriteLine("Checking that integration test hasn't been deployed yet...");

        var result = await TestSharedContext.K8S_CLIENT.CoreV1.ListPodForAllNamespacesWithHttpMessagesAsync(allowWatchBookmarks: false, watch: false, pretty: true);
        podList = result.Body as k8s.Models.V1PodList ?? throw new Exception("Failed to get Pod List");
        integrationPod = podList.Items.FirstOrDefault(pod => pod.Name().Contains("integration-test", StringComparison.CurrentCultureIgnoreCase));
        Assert.Null(integrationPod);

        Console.WriteLine("Integration Test Pod not found.  Continuing with test");

        MessageFormats.HostServices.Link.LinkRequest jpgFile = new() {
            RequestHeader = new MessageFormats.Common.RequestHeader() {
                TrackingId = Guid.NewGuid().ToString(),
                CorrelationId = Guid.NewGuid().ToString()
            },
            FileName = $"astronaut.jpg",
            Subdirectory = "schedule",
            Overwrite = true,
            DestinationAppId = TestSharedContext.TARGET_SVC_APP_ID
        };


        MessageFormats.HostServices.Link.LinkRequest tarFile = new() {
            RequestHeader = new MessageFormats.Common.RequestHeader() {
                TrackingId = Guid.NewGuid().ToString(),
                CorrelationId = Guid.NewGuid().ToString()
            },
            FileName = $"pubsub-csharp-subscriber.tar",
            Subdirectory = "schedule",
            Overwrite = true,
            DestinationAppId = TestSharedContext.TARGET_SVC_APP_ID
        };


        MessageFormats.HostServices.Link.LinkRequest jsonFile = new() {
            RequestHeader = new MessageFormats.Common.RequestHeader() {
                TrackingId = Guid.NewGuid().ToString(),
                CorrelationId = Guid.NewGuid().ToString()
            },
            FileName = $"integration-test-deployment.json",
            Subdirectory = "schedule",
            Overwrite = true,
            DestinationAppId = TestSharedContext.TARGET_SVC_APP_ID
        };

        MessageFormats.HostServices.Link.LinkRequest yamlFile = new() {
            RequestHeader = new MessageFormats.Common.RequestHeader() {
                TrackingId = Guid.NewGuid().ToString(),
                CorrelationId = Guid.NewGuid().ToString()
            },
            FileName = $"integration-test-deployment.yaml",
            Subdirectory = "schedule",
            Overwrite = true,
            DestinationAppId = TestSharedContext.TARGET_SVC_APP_ID
        };

        // Register a callback event to catch the response
        void LinkResponseEventHandler(object? _, MessageFormats.HostServices.Link.LinkResponse _response) {
            if (_response.ResponseHeader.Status == MessageFormats.Common.StatusCodes.Successful && _response.ResponseHeader.CorrelationId == jsonFile.RequestHeader.CorrelationId) jsonFileResponse = _response;
            if (_response.ResponseHeader.Status == MessageFormats.Common.StatusCodes.Successful && _response.ResponseHeader.CorrelationId == yamlFile.RequestHeader.CorrelationId) yamlFileResponse = _response;
            if (_response.ResponseHeader.Status == MessageFormats.Common.StatusCodes.Successful && _response.ResponseHeader.CorrelationId == tarFile.RequestHeader.CorrelationId) tarFileResponse = _response;
            if (_response.ResponseHeader.Status == MessageFormats.Common.StatusCodes.Successful && _response.ResponseHeader.CorrelationId == jpgFile.RequestHeader.CorrelationId) jpgFileResponse = _response;
        }

        MessageHandler<MessageFormats.HostServices.Link.LinkResponse>.MessageReceivedEvent += LinkResponseEventHandler;


        Console.WriteLine("Sending integration-test-schedule.json and integration-test-deployment.yaml to Platform-Deployment via Link Service");

        string scheduleDir = Path.Combine((await TestSharedContext.SPACEFX_CLIENT.GetXFerDirectories()).outbox_directory, "schedule");
        Directory.CreateDirectory(scheduleDir);

        File.Copy("/workspace/platform-deployment/test/sampleSchedules/integration-test-schedule.json", Path.Combine(scheduleDir, "integration-test-deployment.json"), overwrite: true);
        File.Copy("/workspace/platform-deployment/test/sampleSchedules/integration-test-deployment.yaml", Path.Combine(scheduleDir, "integration-test-deployment.yaml"), overwrite: true);
        File.Copy("/workspace/platform-deployment/test/sampleSchedules/pubsub-csharp-subscriber.tar", Path.Combine(scheduleDir, "pubsub-csharp-subscriber.tar"), overwrite: true);
        File.Copy("/workspace/platform-deployment/test/sampleSchedules/astronaut.jpg", Path.Combine(scheduleDir, "astronaut.jpg"), overwrite: true);

        Console.WriteLine($"Sending '{tarFile.GetType().Name}' (TrackingId: '{tarFile.RequestHeader.TrackingId}')");
        await TestSharedContext.SPACEFX_CLIENT.DirectToApp("hostsvc-link", tarFile);

        Console.WriteLine($"Sending '{jsonFile.GetType().Name}' (TrackingId: '{jsonFile.RequestHeader.TrackingId}')");
        await TestSharedContext.SPACEFX_CLIENT.DirectToApp("hostsvc-link", jsonFile);

        Console.WriteLine($"Sending '{yamlFile.GetType().Name}' (TrackingId: '{yamlFile.RequestHeader.TrackingId}')");
        await TestSharedContext.SPACEFX_CLIENT.DirectToApp("hostsvc-link", yamlFile);

        Console.WriteLine($"Sending '{jpgFile.GetType().Name}' (TrackingId: '{jpgFile.RequestHeader.TrackingId}')");
        await TestSharedContext.SPACEFX_CLIENT.DirectToApp("hostsvc-link", jpgFile);

        Console.WriteLine($"Files sent - waiting for responses");

        while ((jsonFileResponse == null || yamlFileResponse == null || tarFileResponse == null || jpgFileResponse == null) && DateTime.Now <= maxTimeToWait) {
            await Task.Delay(250);
        }

        if (tarFileResponse == null) throw new TimeoutException($"Failed to hear {nameof(tarFileResponse)} after {TestSharedContext.MAX_TIMESPAN_TO_WAIT_FOR_MSG}.  Please check that {TestSharedContext.TARGET_SVC_APP_ID} is deployed");
        if (jsonFileResponse == null) throw new TimeoutException($"Failed to hear {nameof(jsonFileResponse)} after {TestSharedContext.MAX_TIMESPAN_TO_WAIT_FOR_MSG}.  Please check that {TestSharedContext.TARGET_SVC_APP_ID} is deployed");
        if (yamlFileResponse == null) throw new TimeoutException($"Failed to hear {nameof(yamlFileResponse)} after {TestSharedContext.MAX_TIMESPAN_TO_WAIT_FOR_MSG}.  Please check that {TestSharedContext.TARGET_SVC_APP_ID} is deployed");
        if (jpgFileResponse == null) throw new TimeoutException($"Failed to hear {nameof(jpgFileResponse)} after {TestSharedContext.MAX_TIMESPAN_TO_WAIT_FOR_MSG}.  Please check that {TestSharedContext.TARGET_SVC_APP_ID} is deployed");

        Assert.NotNull(jsonFileResponse);
        Assert.NotNull(yamlFileResponse);
        Assert.NotNull(tarFileResponse);
        Assert.NotNull(jpgFileResponse);


        DateTime maxTimeToWaitForDeployment = DateTime.Now.Add(TestSharedContext.MAX_TIMESPAN_TO_WAIT_FOR_MSG);
        Console.WriteLine("Starting polling to wait for deployment...");

        while (integrationPod == null && DateTime.Now <= maxTimeToWaitForDeployment) {
            result = await TestSharedContext.K8S_CLIENT.CoreV1.ListPodForAllNamespacesWithHttpMessagesAsync(allowWatchBookmarks: false, watch: false, pretty: true);
            podList = result.Body as k8s.Models.V1PodList ?? throw new Exception("Failed to get Pod List");
            integrationPod = podList.Items.FirstOrDefault(pod => pod.Name().Contains("integration-test", StringComparison.CurrentCultureIgnoreCase) && pod.Status.Phase == "Running");
            Console.WriteLine($"...pod found: {integrationPod == null}");
            if (integrationPod == null)
                await Task.Delay(5000);
        }

        if (integrationPod == null || integrationPod?.Status.Phase == "Pending") throw new TimeoutException($"Failed to find deployment after {TestSharedContext.MAX_TIMESPAN_TO_WAIT_FOR_MSG}.  Please check that {TestSharedContext.TARGET_SVC_APP_ID} is deployed");
        Assert.NotNull(integrationPod);
        Assert.Equal("Running", integrationPod.Status.Phase);

        string recdFile = Path.Combine((await TestSharedContext.SPACEFX_CLIENT.GetXFerDirectories()).inbox_directory, "astronaut.jpg");

        Console.WriteLine($"Waiting for jpg file '{recdFile}' to be received...");
        maxTimeToWaitForDeployment = DateTime.Now.Add(TestSharedContext.MAX_TIMESPAN_TO_WAIT_FOR_MSG);
        while (!File.Exists(recdFile) && DateTime.Now <= maxTimeToWaitForDeployment) {
            if (!File.Exists(recdFile)) await Task.Delay(500);
        }

        if (!File.Exists(recdFile)) throw new TimeoutException($"Failed to find file '{recdFile}' after {TestSharedContext.MAX_TIMESPAN_TO_WAIT_FOR_MSG}.");

        Assert.True(File.Exists(recdFile));


        Console.WriteLine("Integration Test Pod Found.  Waiting for deletion (max 3 mins)");
        maxTimeToWaitForDeployment = DateTime.Now.Add(TimeSpan.FromMinutes(3));

        while (integrationPod != null && DateTime.Now <= maxTimeToWaitForDeployment) {
            result = await TestSharedContext.K8S_CLIENT.CoreV1.ListPodForAllNamespacesWithHttpMessagesAsync(allowWatchBookmarks: false, watch: false, pretty: true);
            podList = result.Body as k8s.Models.V1PodList ?? throw new Exception("Failed to get Pod List");
            integrationPod = podList.Items.FirstOrDefault(pod => pod.Name().Contains("integration-test", StringComparison.CurrentCultureIgnoreCase));
            Console.WriteLine($"...pod found: {integrationPod != null}");
            if (integrationPod != null) await Task.Delay(5000);
        }

        if (integrationPod != null) throw new TimeoutException($"Failed to remove deployment after {TimeSpan.FromMinutes(3)}.  Please check that {TestSharedContext.TARGET_SVC_APP_ID} is deployed");




        Console.WriteLine("Integration Test Pod successfully removed.");
        Assert.Null(integrationPod);
    }
}
