using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

// Top-level statements must come first
var builder = WebApplication.CreateBuilder(args);

// 1. Configure Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Trace); // As per blog for SK

// 2. Add standard ASP.NET Core services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer(); // For Swagger/OpenAPI
builder.Services.AddSwaggerGen(); // For Swagger UI

// 3. Configure OpenAI settings and Kernel
var openAIConfig = builder.Configuration.GetSection("OpenAI");
var apiKey = openAIConfig["ApiKey"];
var modelId = openAIConfig["ChatModelId"] ?? "gpt-4o";

var programLogger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();

if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "sk-YOUR_OPENAI_API_KEY_PLEASE_REPLACE")
{
    programLogger.LogWarning("OpenAI APIKey is not configured or is set to the placeholder value. Please set it in appsettings.json or user secrets. LLM functionalities will be disabled.");
}

var kernelBuilder = Kernel.CreateBuilder();
// Enhance Kernel specific logging for more details, especially for function calling
kernelBuilder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.AddConsole().SetMinimumLevel(LogLevel.Trace); // Ensure console logger is present and at Trace for Kernel
    loggingBuilder.AddFilter<Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>("Microsoft.SemanticKernel", LogLevel.Trace); // Be more explicit for SK logs
    loggingBuilder.AddFilter<Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>("Microsoft.SemanticKernel.Core", LogLevel.Trace); 
    loggingBuilder.AddFilter<Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>("Microsoft.SemanticKernel.Connectors.OpenAI", LogLevel.Trace); 
    // loggingBuilder.AddDebug(); // Already present in general logging, but can be re-added if specific debug output for kernel is desired
});

if (!string.IsNullOrWhiteSpace(apiKey) && apiKey != "sk-YOUR_OPENAI_API_KEY_PLEASE_REPLACE")
{
    kernelBuilder.Services.AddOpenAIChatCompletion(modelId: modelId, apiKey: apiKey);
    programLogger.LogInformation("OpenAI Chat Completion service added to kernel with model {ModelId}.", modelId);
}
else
{
     programLogger.LogWarning("OpenAI Chat Completion service NOT added to kernel due to missing/placeholder API key.");
}
Kernel kernel = kernelBuilder.Build();
builder.Services.AddSingleton<Kernel>(kernel);

// Check GitHub PAT configuration
var githubPat = builder.Configuration["GitHub:PAT"];
if (!string.IsNullOrWhiteSpace(githubPat) && githubPat != "YOUR_GITHUB_PAT_HERE_OR_SET_ENV_VAR")
{
    programLogger.LogWarning("GitHub PAT found in configuration. Note: For the MCP GitHub server (npx) to use this token, you usually need to set the GITHUB_TOKEN environment variable in your terminal *before* running 'dotnet run'. The application does not automatically pass this config value to the npx process.");
}

// 4. Register McpGitHubService
builder.Services.AddSingleton<McpGitHubService>();

var app = builder.Build();

// 5. Initialize McpGitHubService and add tools to Kernel (after app build, before run)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var mcpGitHubService = services.GetRequiredService<McpGitHubService>();
    var startupLogger = services.GetRequiredService<ILogger<Program>>(); 

    try
    {
        startupLogger.LogInformation("Initializing McpGitHubService asynchronously...");
        await mcpGitHubService.InitializeAsync();
        startupLogger.LogInformation("McpGitHubService initialized.");
        
        var kernelForMcp = services.GetRequiredService<Kernel>();
        mcpGitHubService.AddGitHubToolsToKernel(kernelForMcp);
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "CRITICAL: Failed to initialize McpGitHubService or add tools to kernel. Ensure Node.js and npx are installed and in PATH, and '@modelcontextprotocol/server-github' can be executed. MCP functionalities will be unavailable.");
    }
}

// 6. Configure the HTTP request pipeline (standard ASP.NET Core)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    programLogger.LogInformation("Swagger UI enabled for Development environment.");
}

app.UseHttpsRedirection();

// Add UseAuthorization if you implement authentication/authorization
// app.UseAuthorization(); 

app.MapControllers();

programLogger.LogInformation("Application starting...");
app.Run();

// ----- McpGitHubService Definition (moved to the end) -----
public class McpGitHubService : IAsyncDisposable, IDisposable
{
    public IMcpClient? McpClient { get; private set; }
    public IReadOnlyList<McpClientTool>? GitHubTools { get; private set; }
    private readonly ILogger<McpGitHubService> _logger;
    private readonly IConfiguration _configuration;

    public McpGitHubService(ILogger<McpGitHubService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Creating MCPClient for the GitHub server...");
        string serverCommand = _configuration["McpGitHubServer:Command"] ?? "npx";
        List<string> serverArgumentsList = _configuration.GetSection("McpGitHubServer:Arguments").Get<string[]>()?.ToList() ?? new List<string>();

        var pat = _configuration["GitHub:PAT"];
        Dictionary<string, string>? envVarsForNonDocker = null;
        string tokenEnvVarName = "GITHUB_TOKEN"; // Default

        if (serverCommand.Contains("docker", StringComparison.OrdinalIgnoreCase))
        {
            // Use the specific token name requested for Docker
            tokenEnvVarName = "GITHUB_PERSONAL_ACCESS_TOKEN"; 
            _logger.LogInformation("Attempting to use Docker. Target environment variable for token: {TokenEnvVarName}", tokenEnvVarName);

            if (!string.IsNullOrWhiteSpace(pat) && pat != "YOUR_GITHUB_PAT_HERE_OR_SET_ENV_VAR")
            {
                _logger.LogInformation("{TokenEnvVarName} will be passed to Docker container via -e argument.", tokenEnvVarName);
                int imageArgIndex = serverArgumentsList.FindLastIndex(arg => !arg.StartsWith("-"));
                if (imageArgIndex != -1 && imageArgIndex == serverArgumentsList.Count - 1) // Basic check if last arg is likely image
                {
                    // Insert right before the presumed image name
                    serverArgumentsList.Insert(imageArgIndex, $"{tokenEnvVarName}={pat}");
                    serverArgumentsList.Insert(imageArgIndex, "-e"); 
                    _logger.LogInformation("Inserted -e {TokenEnvVarName}=*** before arg index {Index}", tokenEnvVarName, imageArgIndex);
                }
                else
                {
                    // Fallback if image name isn't obvious last argument
                    _logger.LogWarning("Could not reliably determine Docker image name position in arguments {Args}. Appending -e {TokenEnvVarName}=***. Verify McpGitHubServer:Arguments in appsettings.json.", string.Join(' ', serverArgumentsList), tokenEnvVarName);
                    serverArgumentsList.Add("-e");
                    serverArgumentsList.Add($"{tokenEnvVarName}={pat}");
                }
            }
            else
            {
                _logger.LogWarning("GitHub PAT is not configured in appsettings.json. {TokenEnvVarName} will not be passed to Docker. Server might fail to authenticate.", tokenEnvVarName);
            }
        }
        else // For non-Docker commands (like npx)
        {
             tokenEnvVarName = "GITHUB_TOKEN"; // Ensure default is used for non-docker
            _logger.LogInformation("Using non-Docker command. Target environment variable for token: {TokenEnvVarName}", tokenEnvVarName);
            if (!string.IsNullOrWhiteSpace(pat) && pat != "YOUR_GITHUB_PAT_HERE_OR_SET_ENV_VAR")
            {
                _logger.LogInformation("{TokenEnvVarName} will be set as environment variable for the direct MCP server process.", tokenEnvVarName);
                envVarsForNonDocker = new Dictionary<string, string> { { tokenEnvVarName, pat } };
            }
            else
            {
                _logger.LogWarning("GitHub PAT is not configured. {TokenEnvVarName} will not be set for the process. Server might fail to authenticate.", tokenEnvVarName);
            }
        }

        var finalServerArguments = serverArgumentsList.ToArray();

        var transportOptions = new StdioClientTransportOptions
        {
            Name = "GitHubMCPvia" + serverCommand,
            Command = serverCommand,
            Arguments = finalServerArguments,
            // Only set EnvironmentVariables directly for non-Docker commands
            EnvironmentVariables = serverCommand.Contains("docker", StringComparison.OrdinalIgnoreCase) ? null : envVarsForNonDocker 
        };

        McpClient = await McpClientFactory.CreateAsync(new StdioClientTransport(transportOptions));
        _logger.LogInformation("MCPClient created for command: {Command} {Arguments}", serverCommand, string.Join(" ", finalServerArguments));

        _logger.LogInformation("Retrieving MCP tools from GitHub server...");
        if (McpClient != null)
        {
            var toolsList = await McpClient.ListToolsAsync();
            GitHubTools = toolsList?.ToList();
            _logger.LogInformation("Retrieved {Count} tools.", GitHubTools?.Count ?? 0);
            if (GitHubTools != null)
            {
                foreach (var tool in GitHubTools)
                {
                    _logger.LogInformation("Tool: {Name} - {Description}", tool.Name, tool.Description);
                }
            }
        }
        else
        {
            _logger.LogError("McpClient is null after creation. Cannot retrieve tools.");
        }
    }

    public void AddGitHubToolsToKernel(Kernel kernelInstance)
    {
        if (kernelInstance == null)
        {
            _logger.LogError("Kernel instance is null. Cannot add GitHub tools.");
            return;
        }

        if (GitHubTools != null && GitHubTools.Any())
        {
            _logger.LogInformation("Adding GitHub MCP tools to Kernel plugins under 'GitHubMcp'...");
            try
            {
                // McpClientTool should be an AIFunction, so AsKernelFunction() should work.
#pragma warning disable SKEXP0001 // Suppress experimental warning for AsKernelFunction
                kernelInstance.Plugins.AddFromFunctions("GitHubMcp", GitHubTools.Select(tool => tool.AsKernelFunction()));
#pragma warning restore SKEXP0001
                _logger.LogInformation("GitHub MCP tools added to Kernel.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert or add MCP tools to kernel.");
            }
        }
        else
        {
            _logger.LogWarning("No GitHub tools found or McpClient not initialized properly; nothing to add to kernel.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Asynchronously disposing McpGitHubService...");
        if (McpClient is IAsyncDisposable asyncDisposableClient)
        {
            await asyncDisposableClient.DisposeAsync();
        }
        McpClient = null;
        _logger.LogInformation("McpGitHubService disposed.");
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing McpGitHubService (sync)...");
        if (McpClient is IDisposable disposableClient)
        {
             disposableClient.Dispose();
        }
        McpClient = null;
        _logger.LogInformation("McpGitHubService disposed (sync).");
        GC.SuppressFinalize(this);
    }
}
// ----- End McpGitHubService Definition -----
