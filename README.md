# Azure Orbital Space SDK - Platform-Deployment

[![platform-deployment-build](https://github.com/microsoft/azure-orbital-space-sdk-platform-deployment/actions/workflows/platform-deployment-build-publish.yaml/badge.svg)](https://github.com/microsoft/azure-orbital-space-sdk-platform-deployment/actions/workflows/platform-deployment-build-publish.yaml)

This repository hosts the Platform-Deployment.

Outputs:

| Item                                                                       | Description                                                             |
| -------------------------------------------------------------------------- | ----------------------------------------------------------------------- |
| `Microsoft.Azure.SpaceSDK.PlatformServices.Deployment.Plugins.1.0.0.nupkg` | DotNet Nuget Package for building Platform-Deployment Plugins           |
| `platform-deployment:0.11.0`                                               | Container image for app                                                 |
| `platform-deployment:0.11.0_base`                                          | Base container image for app.  Requires SpaceSDK_Base and build service |

## Building

1. Provision /var/spacedev

    ```bash
    # clone the azure-orbital-space-sdk-setup repo and provision /var/spacedev
    git clone https://github.com/microsoft/azure-orbital-space-sdk-setup
    cd azure-orbital-space-sdk-setup
    bash ./.vscode/copy_to_spacedev.sh
    cd -
    ```

1. Build the nuget packages and the container images.  (Note: container images will automatically push)

    ```bash
    # clone this repo
    git clone https://github.com/microsoft/azure-orbital-space-sdk-platform-deployment

    cd azure-orbital-space-sdk-platform-deployment

    # Trigger the build_app.sh from azure-orbital-space-sdk-setup
    /var/spacedev/build/dotnet/build_app.sh \
        --repo-dir ${PWD} \
        --app-project src/platform-deployment.csproj \
        --nuget-project src_pluginBase/pluginBase.csproj \
        --architecture amd64 \
        --output-dir /var/spacedev/tmp/platform-deployment \
        --app-version 0.11.0 \
        --annotation-config azure-orbital-space-sdk-platform-deployment.yaml
    ```

1. Copy the build artifacts to their locations in /var/spacedev

    ```bash
    sudo mkdir -p /var/spacedev/nuget/deployment

    sudo cp /var/spacedev/tmp/platform-deployment/amd64/nuget/Microsoft.Azure.SpaceSDK.PlatformServices.Deployment.Plugins.0.11.0.nupkg /var/spacedev/nuget/deployment
    ```

1. Push the artifacts to the container registry

    ```bash
    # Push the nuget package to the container registry
    /var/spacedev/build/push_build_artifact.sh \
            --artifact /var/spacedev/nuget/deployment/Microsoft.Azure.SpaceSDK.PlatformServices.Deployment.Plugins.0.11.0.nupkg \
            --annotation-config azure-orbital-space-sdk-platform-deployment.yaml \
            --architecture amd64 \
            --artifact-version 0.11.0
    ```

## Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Trademarks

This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft
trademarks or logos is subject to and must follow
[Microsoft's Trademark & Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks/usage/general).
Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship.
Any use of third-party trademarks or logos are subject to those third-party's policies.
