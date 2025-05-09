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
    The application requires a GitHub PAT for certain operations. You need to configure this in the `YakShaver.SmartAss/appsettings.json` file.

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
      }
      // ... other settings
    }
    ```
    Replace `"YOUR_GITHUB_PAT_HERE_OR_SET_ENV_VAR"` with your actual GitHub PAT. Ensure the PAT has the necessary permissions for the application's functionality.

    Alternatively, the `run-yakshaver-with-auth.ps1` script can read the PAT from an environment variable if it's not set or is a placeholder in `appsettings.json`. However, it's recommended to set it directly in `appsettings.json` for clarity or ensure the environment variable `GITHUB_TOKEN` is set before running the script manually if you prefer that method.

## Running the Application

The easiest way to run the application with the necessary GitHub authentication is by using the provided PowerShell script:

1.  Navigate to the root directory of the project (where `run-yakshaver-with-auth.ps1` is located).
2.  Run the script:
    ```powershell
    ./run-yakshaver-with-auth.ps1
    ```
    This script will:
    *   Read the `GitHub.PAT` from `YakShaver.SmartAss/appsettings.json`.
    *   Set it as an environment variable (`GITHUB_TOKEN`) for the current session.
    *   Launch the `YakShaver.SmartAss` .NET application.

    You can also run the .NET project directly using `dotnet run` from within the `YakShaver.SmartAss` directory, but you will need to ensure the `GITHUB_TOKEN` environment variable is set manually if the application requires it at startup.
    ```powershell
    cd YakShaver.SmartAss
    # Set GITHUB_TOKEN environment variable here if needed
    dotnet run
    ```

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