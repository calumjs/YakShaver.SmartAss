# YakShaver.SmartAss

A .NET application designed to [... TBD: Add a brief description of the project's purpose here ...]

## Prerequisites

Before you begin, ensure you have the following installed:

*   [.NET SDK](https://dotnet.microsoft.com/download) (Version specified in `YakShaver.SmartAss/YakShaver.SmartAss.csproj` or latest stable)
*   [PowerShell 7+](https://docs.microsoft.com/powershell/scripting/install/installing-powershell)

## Setup

1.  **Clone the repository:**
    ```bash
    git clone <repository-url>
    cd <repository-directory>
    ```

2.  **Configure GitHub Personal Access Token (PAT):**
    The application requires a GitHub PAT to interact with the MCP GitHub Server. The primary way to configure this is in the `YakShaver.SmartAss/appsettings.json` file.

    Open `YakShaver.SmartAss/appsettings.json` and update the `GitHub.PAT` value:
    ```json
    {
      "Logging": {
        "LogLevel": {
          "Default": "Information",
          "Microsoft.AspNetCore": "Warning"
        }
      },
      "AllowedHosts": "*",
      "GitHub": {
        "PAT": "YOUR_GITHUB_PAT_HERE_OR_SET_ENV_VAR"
      },
      "McpGitHubServer": {
        "Command": "docker", // Or "npx", etc.
        "Arguments": [ /* ... */ ]
      }
      // ... other settings
    }
    ```
    Replace `"YOUR_GITHUB_PAT_HERE_OR_SET_ENV_VAR"` with your actual GitHub PAT. Ensure the PAT has the necessary permissions (e.g., `repo`, `read:org`).

    The application uses this PAT from `appsettings.json` to authenticate the MCP GitHub Server process it starts.
    *   If `McpGitHubServer:Command` is set to `docker` (default), the application passes the PAT as the `GITHUB_PERSONAL_ACCESS_TOKEN` environment variable to the Docker container.
    *   If `McpGitHubServer:Command` is a direct command (e.g., `npx`), the application passes the PAT as the `GITHUB_TOKEN` environment variable to that command's process.

## Running the Application

Once the `GitHub.PAT` is configured in `YakShaver.SmartAss/appsettings.json`, you can run the application.

### Using the `run-yakshaver-with-auth.ps1` script (Recommended for local dev)

This PowerShell script provides a convenient way to launch the application:

1.  Navigate to the root directory of the project (where `run-yakshaver-with-auth.ps1` is located).
2.  Run the script:
    ```powershell
    ./run-yakshaver-with-auth.ps1
    ```
    This script will:
    *   Read the `GitHub.PAT` value from `YakShaver.SmartAss/appsettings.json`.
    *   Set this value as an environment variable named `GITHUB_TOKEN` for the `dotnet run` process it is about to launch.
    *   Launch the `YakShaver.SmartAss` .NET application.

    Note: While the script sets `GITHUB_TOKEN`, the application's internal logic (described above) primarily relies on the `GitHub.PAT` from `appsettings.json` to correctly set `GITHUB_PERSONAL_ACCESS_TOKEN` for Docker or `GITHUB_TOKEN` for direct commands like `npx` when launching the MCP server. The script's action provides an additional layer, ensuring `GITHUB_TOKEN` is available in the application's immediate environment.

### Running manually with `dotnet run`

You can also run the .NET project directly:

1.  Ensure `GitHub.PAT` in `YakShaver.SmartAss/appsettings.json` is correctly set with your PAT. This is the most important step for the application to authenticate the MCP server.
2.  Navigate to the application directory:
    ```powershell
    cd YakShaver.SmartAss
    ```
3.  Run the application:
    ```powershell
    dotnet run
    ```
    The application will then use the PAT from `appsettings.json` to configure authentication for the MCP GitHub Server as described under the PAT configuration section.
    Setting `GITHUB_TOKEN` or `GITHUB_PERSONAL_ACCESS_TOKEN` manually in your terminal before `dotnet run` is generally not required if `appsettings.json` is correctly configured, as the application handles the specific environment variable injection for the child MCP server process.

## Project Structure

*   `run-yakshaver-with-auth.ps1`: PowerShell script to configure GitHub authentication and run the application.
*   `YakShaver.SmartAss/`: Contains the .NET application.
    *   `Program.cs`: Main entry point for the application.
    *   `YakShaver.SmartAss.csproj`: Project file for the .NET application.
    *   `appsettings.json`: Configuration file for the application, including GitHub PAT.
    *   `Controllers/`: Likely contains API controllers if this is a web application.
    *   ... (other project files and folders)

## Contributing

[... TBD: Add guidelines for contributing to the project ...]

## License

[... TBD: Add license information ...] 