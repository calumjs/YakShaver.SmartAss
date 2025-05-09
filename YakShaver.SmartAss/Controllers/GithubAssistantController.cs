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

        [HttpPost("respondToIssue")]
        [Consumes("application/x-www-form-urlencoded")] // Specify content type
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> RespondToIssue([FromForm] string issue) // Changed FromBody to FromForm
        {
            if (string.IsNullOrWhiteSpace(issue))
            {
                return BadRequest("Issue context must be provided in the form data.");
            }

            // Extract repoName from issueContext
            string? repoName = null;
            using (var reader = new StringReader(issue))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("Repo: ", StringComparison.OrdinalIgnoreCase))
                    {
                        repoName = line.Substring("Repo: ".Length).Trim();
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(repoName) || !repoName.Contains("/"))
            {
                return BadRequest("repoName could not be extracted from issueContext or is not in 'owner/repo' format. Ensure the issue context contains a line like 'Repo: owner/repo'.");
            }

            _logger.LogInformation("Received request to respond to issue in repo: '{RepoName}'. Context: '{IssueContext}'", repoName, issue);

            try
            {
                var executionSettings = new OpenAIPromptExecutionSettings
                {
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                    Temperature = 0.2,
                    MaxTokens = 1500 // Max tokens for intermediate research steps
                };

                // --- Step 1: Search similar issues already answered ---
                _logger.LogInformation("Step 1: Searching similar answered issues for '{IssueContext}' in repo '", issue);
                var answeredIssuesPrompt = $"""
                Background: You are an AI assistant helping to research a GitHub issue.
                Task: Search for GitHub issues in the repository '{repoName}' that are similar to the following issue context: '{issue}'. 
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
                _logger.LogInformation("Step 2: Searching other outstanding issues for '{IssueContext}' in repo '{RepoName}'", issue, repoName);
                var outstandingIssuesPrompt = $"""
                Background: You are an AI assistant helping to research a GitHub issue.
                Task: Search for OPEN GitHub issues in the repository '{repoName}' that might be related to the following issue context: '{issue}'.
                Instructions: Provide a summary of up to 3 most relevant open issues. For each, include its title and number.
                When using tools that accept a 'per_page' parameter, ensure it is an integer.
                If no relevant open issues are found, clearly state that.
                Your response will be used as context for another AI to draft a final response to the original issue.
                """;
                var outstandingIssuesResult = await _kernel.InvokePromptAsync(outstandingIssuesPrompt, new KernelArguments(executionSettings));
                var outstandingIssuesContext = outstandingIssuesResult.GetValue<string>() ?? "No information on outstanding issues found by the LLM.";
                _logger.LogInformation("Step 2 - Outstanding issues search result: {Context}", outstandingIssuesContext);

                // --- Step 3: Search the code base ---
                _logger.LogInformation("Step 3: Searching codebase in '{RepoName}' related to '{IssueContext}'", issue, repoName);
                var codeSearchPrompt = $"""
                Background: You are an AI assistant helping to research a GitHub issue.
                Task: Search the codebase of the GitHub repository '{repoName}' for code snippets, comments, or documentation relevant to the following issue context: '{issue}'.
                Instructions: Summarize any key findings. If specific file paths or code blocks are identified as highly relevant, mention them. Limit to 3 most relevant findings.
                When using tools that accept a 'per_page' parameter, ensure it is an integer.
                If no relevant code is found, clearly state that.
                Your response will be used as context for another AI to draft a final response to the original issue.
                """;
                var codeSearchResult = await _kernel.InvokePromptAsync(codeSearchPrompt, new KernelArguments(executionSettings));
                var codeSearchContext = codeSearchResult.GetValue<string>() ?? "No relevant code snippets found by the LLM.";
                _logger.LogInformation("Step 3 - Code search result: {Context}", codeSearchContext);

                // --- Step 4: Create a new GitHub issue with the research context ---
                _logger.LogInformation("Step 4: Creating a new GitHub issue with research context for '{IssueContext}' in repo '{RepoName}'", issue, repoName);
                var createIssuePrompt = $"""
                Background: You are an AI assistant helping to manage GitHub issues. Based on the research conducted for an incoming issue, you need to create a new, well-summarized issue in the repository '{repoName}'.

                Original Issue Context Provided:
                '''{issue}'''

                Research Summary:
                1. Similar Answered/Closed Issues: {answeredIssuesContext}
                2. Related Outstanding/Open Issues: {outstandingIssuesContext}
                3. Relevant Code Search Results: {codeSearchContext}

                Task:
                1. Synthesize the information above to create a new GitHub issue.
                2. The issue title should be concise and reflect the core problem derived from the original context and research.
                3. The issue body should provide a clear summary of the problem, referencing the key findings from the research (answered issues, open issues, code findings).
                4. Structure the body for clarity. Use markdown.
                5. Your primary goal is to create an issue that a developer can understand and act upon.
                Instructions:
                - Use the available tools to create this issue in the repository '{repoName}'.
                - After creating the issue, output the URL or identifier of the newly created issue. If creation fails or is not possible, state that clearly.
                """;
                var createIssueResult = await _kernel.InvokePromptAsync(createIssuePrompt, new KernelArguments(executionSettings));
                var newIssueCreationContext = createIssueResult.GetValue<string>() ?? "Could not create a new issue or no confirmation received.";
                _logger.LogInformation("Step 4 - New issue creation result: {Context}", newIssueCreationContext);

                // --- Step 5: Give a useful response using an LLM ---
                _logger.LogInformation("Step 5: Synthesizing a response for issue '{IssueContext}'", issue);
                var finalResponsePrompt = $"""
                You are an AI assistant tasked with drafting a helpful and context-aware response to a new GitHub issue.

                Original Issue Context Provided:
                '''{issue}'''

                Repository: {repoName}

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

                4. Action Taken: A new GitHub issue has been created based on this research.
                   Details: {newIssueCreationContext}

                Task:
                Based *only* on the Original Issue Context and the Background Research provided above (including the result of the new issue creation), please draft a comprehensive and helpful response.
                Your response should be suitable for posting as a comment on the GitHub issue that *triggered this process*.
                Address the user who might have reported the issue. Be empathetic and constructive.
                Inform the user that a new issue has been created to track this (if successful, refer to '{newIssueCreationContext}').
                If the research yielded no specific results for some steps, acknowledge that tactfully if relevant, and formulate the best possible response with the available information.
                Do not invent information not present in the context provided.
                Structure your response clearly. You can use markdown for formatting.
                """;
                
                var synthesisExecutionSettings = new OpenAIPromptExecutionSettings
                {
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
                _logger.LogError(ex, "Error processing request for issue '{IssueContext}' in repo '{RepoName}'", issue, repoName);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while processing your request. Check service logs for details.");
            }
        }
    }
}