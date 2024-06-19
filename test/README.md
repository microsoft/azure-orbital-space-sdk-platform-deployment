# test

This directory contains tests, clients, and plug-ins to validate the Deployment Service.

```plaintext
.
├── debugClient              # a .NET application that allows you to interact with Deployment Service
├── integrationTestPlugin    # a .NET application that modifies responses for tests
├── integrationTests         # a xUnit test suite that validates the end-to-end business-logic of Deployment Service
└── unitTests                # a xUnit test suite that validates Deployment Service components
└── sampleSchedules          # sample schedules used for xUnit test suite that validates Deployment Service components
└── sampleYaml               # sample YAML files used for xUnit test suite that validates Deployment Service components

```

## Run Integration Tests

One way to get started with Deployment Service is to run the [Debug Client](./debugClient/Program.cs) and [Integration Tests](./integrationTests/LogMsg.cs).

1. Open this respository in its Dev Container

1. Open the "Run and Debug" menu (CTRL + SHIFT + D)

2. From the launch configuration dropdown, select "Integration Tests - Run"

    ![Launch Configuration Options](../docs/img/integration-test-select.png)

3. Select the Start Icon ▶ (or press F5) to begin debugging the service and running the tests

4. Your terminal will show the tasks executed as described in [tasks.json](../.vscode/tasks.json)

5. The Debug Console (CTRL + SHIFT + Y) will become active, select the "Integration Tests - Client Run" session from the dropdown

    ![Debug Console Output](../docs/img/integration-test-debugger-output.png)

6. A successful test run should emit something like this to the debug console

    ```plaintext
        Passed integrationTests.DeploymentTests.ImmediateDeploymentAndDeletionTest [2 m]
        ......Sending ListItemRequest (Tracking ID: '44692d2a-db5d-4963-9a73-c9c43969e85c')...
        ......Waiting for ListItemResponse (Tracking ID: '44692d2a-db5d-4963-9a73-c9c43969e85c')...
        ......Heard ListItemResponse (Tracking ID: '44692d2a-db5d-4963-9a73-c9c43969e85c')...
        ......Sending LogRequest (Tracking ID: '11bd4565-ef43-4ec2-b450-9db574038040')...
        ......Waiting for LogResponse (Tracking ID: '11bd4565-ef43-4ec2-b450-9db574038040')...
        ......Heard LogResponse (Tracking ID: '11bd4565-ef43-4ec2-b450-9db574038040')...
        ......Waiting for file '11bd4565-ef43-4ec2-b450-9db574038040.log' in '/var/spacedev/xfer/platform-deployment-test-client/inbox' (Tracking ID: '11bd4565-ef43-4ec2-b450-9db574038040')...
        [xUnit.net 00:05:28.63]   Finished:    integrationTests
        Passed integrationTests.ListItemRequestTest.ListItemRequestQueryAndResponse [601 ms]
        Passed integrationTests.LogRequestTest.LogQuery [405 ms]

        Test Run Successful.
        Total tests: 4
            Passed: 4
        Total time: 5.4850 Minutes
    ```

1. Stop the debugger by selecting the Stop Icon 🟥 (Shift + F5)

    This is a **required step**, failure to stop the services will result in unknown pod states

## Debugging Integration Tests

1. Follow the steps to successfully [run the integration tests](#run-integration-tests)

1. Be sure to stop the services and detach the debugger
2. Open the "Run and Debug" menu (CTRL + SHIFT + D)
3. From the launch configuration dropdown, select debugService of choice such as "Integration Tests - Debug"
    ![Launch Configuration Options](../docs/img/integration-test-select.png)

4. Set a breakpoint on [Foreman.MessageReceivedHandler()](../src/Services/Foreman.cs)

    1. Click in the gutter area to the left of a line number, a circle 🔴 will appear for lines with breakpoints set

    ![Breakpoint set](../docs/img/integration-test-breakpoint.png)

5. Select the Continue Icon ⏯️ (or press F5) to run the integration tests debug again

6. The breakpoint will be hit and execution of the application will pause

    ![Breakpoint hit](../docs/img/integration-test-breakpoint-hit.png)

7. You can inspect the state of the application, view the call stack, and interact with the application

9. Remove your breakpoint, or move it around, explore how the application works

10. Select the Continue Icon ⏯️ (or press F5) to continue execution

    ![Continue Icon](../docs/img/integration-test-continue.png)

11. Remember, once finished, stop the debugger by selecting the Stop Icon 🟥 (Shift + F5)

    This is a **required step**, failure to stop the services will result in unknown pod states
    ![Stop Icon](../docs/img/stop-debug.png)

## Run Unit Tests

The unit tests assert that Deployment Service components are in the expected shape and configuration.

1. Open this respository in its Dev Container

1. Open the "Run and Debug" menu (CTRL + SHIFT + D)

1. From the launch configuration dropdown, select "Unit Tests - Run"
    ![Launch Configuration Options](../docs/img/integration-test-select.png)
1. Select the Start Icon ▶ (or press F5) to begin running the tests

1. Your terminal will show the tasks executed as described in [tasks.json](../.vscode/tasks.json)

1. The Debug Console (CTRL + SHIFT + Y) will become active, select the "Unit Tests" session from the dropdown

1. A successful test run should emit something like this to the debug console

1. A successful test run should emit something like this to the debug console

    ```plaintext
        [xUnit.net 00:00:00.89]   Finished:    unitTests
        Passed unitTests.ProtoTests.ListItemRequest [45 ms]
        Passed unitTests.ProtoTests.DeployRequest [< 1 ms]
        Passed unitTests.ProtoTests.LogRequest [< 1 ms]
        Passed unitTests.ProtoTests.ListItemResponse [< 1 ms]
        Passed unitTests.ProtoTests.LogResponse [< 1 ms]
        Passed unitTests.ProtoTests.DeployResponse [< 1 ms]

        Test Run Successful.
        Total tests: 6
            Passed: 6
        Total time: 1.4037 Seconds
    ```

## Integration Test Plugin

The [Integration Test Plugin](./integrationTestPlugin/integrationTestPlugin.cs) is used to validate that message requests and responses modified by plugins are honored by the Deployment Service.

This plugin is injected into the running Deployment Service during testing by providing the [appsettings.IntegrationTest.json](../src/appsettings.IntegrationTest.json) configuration to the Deployment Service at startup time in the `Integration Tests - Debug` launch configuration in [launch.json](../.vscode/launch.json).
