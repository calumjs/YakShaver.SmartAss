using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI; // For OpenAIPromptExecutionSettings, ToolCallBehavior
using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace YakShaver.SmartAss.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class GithubAssistantController : ControllerBase
    {
        private readonly Kernel _kernel;
        private readonly ILogger<GithubAssistantController> _logger;
        private readonly IConfiguration _configuration; 

        public GithubAssistantController(Kernel kernel, ILogger<GithubAssistantController> logger, IConfiguration configuration)
        {
            _kernel = kernel;
            _logger = logger;
            _configuration = configuration; 
        }

        public class IssueRequest
        {
            [JsonPropertyName("issue_context")]
            public string? IssueContext { get; set; }

            [JsonPropertyName("repo_name")]
            public string? RepoName { get; set; } // Expected format: "owner/repo"
        }

        [HttpPost("respondToIssue")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> RespondToIssue([FromBody] IssueRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.IssueContext))
            {
                return BadRequest("IssueContext must be provided.");
            }
            if (string.IsNullOrWhiteSpace(request.RepoName) || !request.RepoName.Contains("/"))
            {
                return BadRequest("RepoName must be provided in 'owner/repo' format.");
            }

            _logger.LogInformation("Received request to respond to issue: '{IssueContext}' in repo: '{RepoName}'", request.IssueContext, request.RepoName);

            try
            {
                var executionSettings = new OpenAIPromptExecutionSettings
                {
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                    Temperature = 0.2,
                    MaxTokens = 1500 // Max tokens for intermediate research steps
                };

                // --- Step 1: Search similar issues already answered ---
                _logger.LogInformation("Step 1: Searching similar answered issues for '{IssueContext}' in repo '{RepoName}'", request.IssueContext, request.RepoName);
                var answeredIssuesPrompt = $"""
                Background: You are an AI assistant helping to research a GitHub issue.
                Task: Search for GitHub issues in the repository '{request.RepoName}' that are similar to the following issue context: '{request.IssueContext}'. 
                Focus on issues that are already CLOSED or RESOLVED.
                Instructions: Provide a summary of up to 3 most relevant issues found. For each, include its title, number, and a brief of its resolution.
                When using tools that accept a 'per_page' parameter, ensure it is an integer.
                If no relevant closed/resolved issues are found, clearly state that.
                Your response will be used as context for another AI to draft a final response to the original issue.
                """;
                var answeredIssuesResult = await _kernel.InvokePromptAsync(answeredIssuesPrompt, new KernelArguments(executionSettings));
                var answeredIssuesContext = answeredIssuesResult.GetValue<string>() ?? "No information on answered issues found by the LLM.";
                _logger.LogInformation("Step 1 - Answered issues search result: {Context}", answeredIssuesContext);

                // --- Step 2: Search other outstanding issues ---
                _logger.LogInformation("Step 2: Searching other outstanding issues for '{IssueContext}' in repo '{RepoName}'", request.IssueContext, request.RepoName);
                var outstandingIssuesPrompt = $"""
                Background: You are an AI assistant helping to research a GitHub issue.
                Task: Search for OPEN GitHub issues in the repository '{request.RepoName}' that might be related to the following issue context: '{request.IssueContext}'.
                Instructions: Provide a summary of up to 3 most relevant open issues. For each, include its title and number.
                When using tools that accept a 'per_page' parameter, ensure it is an integer.
                If no relevant open issues are found, clearly state that.
                Your response will be used as context for another AI to draft a final response to the original issue.
                """;
                var outstandingIssuesResult = await _kernel.InvokePromptAsync(outstandingIssuesPrompt, new KernelArguments(executionSettings));
                var outstandingIssuesContext = outstandingIssuesResult.GetValue<string>() ?? "No information on outstanding issues found by the LLM.";
                _logger.LogInformation("Step 2 - Outstanding issues search result: {Context}", outstandingIssuesContext);

                // --- Step 3: Search the code base ---
                _logger.LogInformation("Step 3: Searching codebase in '{RepoName}' related to '{IssueContext}'", request.IssueContext, request.RepoName);
                var codeSearchPrompt = $"""
                Background: You are an AI assistant helping to research a GitHub issue.
                Task: Search the codebase of the GitHub repository '{request.RepoName}' for code snippets, comments, or documentation relevant to the following issue context: '{request.IssueContext}'.
                Instructions: Summarize any key findings. If specific file paths or code blocks are identified as highly relevant, mention them. Limit to 3 most relevant findings.
                When using tools that accept a 'per_page' parameter, ensure it is an integer.
                If no relevant code is found, clearly state that.
                Your response will be used as context for another AI to draft a final response to the original issue.
                """;
                // The GitHubMcp.search_code tool might require specific query formats. The LLM will try to use it.
                var codeSearchResult = await _kernel.InvokePromptAsync(codeSearchPrompt, new KernelArguments(executionSettings));
                var codeSearchContext = codeSearchResult.GetValue<string>() ?? "No relevant code snippets found by the LLM.";
                _logger.LogInformation("Step 3 - Code search result: {Context}", codeSearchContext);

                // --- Step 4: Give a useful response using an LLM ---
                _logger.LogInformation("Step 4: Synthesizing a response for issue '{IssueContext}'", request.IssueContext);
                var finalResponsePrompt = $"""
                You are an AI assistant tasked with drafting a helpful and context-aware response to a new GitHub issue.

                Original Issue Context Provided:
                '''{request.IssueContext}'''

                Repository: {request.RepoName}

                Here is the background research conducted to help you formulate the response:

                1. Similar Answered/Closed Issues Found:
                '''
                {answeredIssuesContext}
                '''

                2. Related Outstanding/Open Issues Found:
                '''
                {outstandingIssuesContext}
                '''

                3. Relevant Code Search Results from the Repository:
                '''
                {codeSearchContext}
                '''

                Task:
                Based *only* on the Original Issue Context and the Background Research provided above, please draft a comprehensive and helpful response. 
                Your response should be suitable for posting as a comment on the GitHub issue.
                Address the user who might have reported the issue. Be empathetic and constructive.
                If the research yielded no specific results for some steps, acknowledge that tactfully if relevant, and formulate the best possible response with the available information.
                Do not invent information not present in the context provided.
                Structure your response clearly. You can use markdown for formatting.
                """;
                
                var synthesisExecutionSettings = new OpenAIPromptExecutionSettings
                {
                    // ToolCallBehavior = ToolCallBehavior.None, // Removed due to CS0117 build error. Default might be no auto-invocation.
                    Temperature = 0.5, 
                    MaxTokens = 1000 
                };

                var finalResponseResult = await _kernel.InvokePromptAsync(finalResponsePrompt, new KernelArguments(synthesisExecutionSettings));
                var finalResponse = finalResponseResult.GetValue<string>() ?? "Could not generate a final response at this time.";

                _logger.LogInformation("Generated final response: {Response}", finalResponse);
                return Ok(new { response = finalResponse });
            }
            catch (Exception ex)
            { // Catching general Exception, specific SK or API exceptions could be handled too.
                _logger.LogError(ex, "Error processing request for issue '{IssueContext}' in repo '{RepoName}'", request.IssueContext, request.RepoName);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while processing your request. Check service logs for details.");
            }
        }
    }
} 