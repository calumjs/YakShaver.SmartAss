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
        public async Task<IActionResult> RespondToIssue([FromForm] string payload) // Changed FromBody to FromForm
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return BadRequest("Webhook payload must be provided in the form data.");
            }

            _logger.LogInformation("Received request to respond to issue. Raw payload: '{Payload}'", payload);

            try
            {
                var executionSettings = new OpenAIPromptExecutionSettings
                {
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                    Temperature = 0,
                    MaxTokens = 1500 // Max tokens for intermediate research steps
                };

                // --- Step 1: Search similar issues already answered ---
                _logger.LogInformation("Step 1: Searching similar answered issues based on payload: '{Payload}'", payload);
                var answeredIssuesPrompt = $"""
                Background: You are an AI assistant helping to research a GitHub issue.
                The GitHub issue context below (referred to as 'payload') contains the full webhook data from GitHub.
                Task: First, infer the repository details (owner and repository name) from the provided payload: '{payload}'. 
                Then, using these inferred repository details, search for GitHub issues in that repository that are similar to the issue described in the payload.
                Focus on issues that are already CLOSED or RESOLVED.
                Instructions: Provide a summary of up to 3 most relevant issues found. For each, include its title, number, and a brief of its resolution.
                IMPORTANT: If using a tool function that accepts a 'per_page' parameter (like GitHubMcp-search_code), you MUST provide the value as a JSON number (e.g., 3), NOT as a JSON string (e.g., "3").
                If no relevant closed/resolved issues are found, clearly state that.
                Your response will be used as context for another AI.
                """;
                var answeredIssuesResult = await _kernel.InvokePromptAsync(answeredIssuesPrompt, new KernelArguments(executionSettings));
                var answeredIssuesContext = answeredIssuesResult.GetValue<string>() ?? "No information on answered issues found by the LLM.";
                _logger.LogInformation("Step 1 - Answered issues search result: {Context}", answeredIssuesContext);

                // --- Step 2: Search other outstanding issues ---
                _logger.LogInformation("Step 2: Searching other outstanding issues based on payload: '{Payload}'", payload);
                var outstandingIssuesPrompt = $"""
                Background: You are an AI assistant helping to research a GitHub issue.
                The GitHub issue context below (referred to as 'payload') contains the full webhook data from GitHub.
                Task: First, infer the repository details (owner and repository name) from the provided payload: '{payload}'.
                Then, using these inferred repository details, search for OPEN GitHub issues in that repository that might be related to the issue described in the payload.
                Instructions: Provide a summary of up to 3 most relevant open issues. For each, include its title and number.
                IMPORTANT: If using a tool function that accepts a 'per_page' parameter (like GitHubMcp-search_code), you MUST provide the value as a JSON number (e.g., 3), NOT as a JSON string (e.g., "3").
                If no relevant open issues are found, clearly state that.
                Your response will be used as context for another AI.
                """;
                var outstandingIssuesResult = await _kernel.InvokePromptAsync(outstandingIssuesPrompt, new KernelArguments(executionSettings));
                var outstandingIssuesContext = outstandingIssuesResult.GetValue<string>() ?? "No information on outstanding issues found by the LLM.";
                _logger.LogInformation("Step 2 - Outstanding issues search result: {Context}", outstandingIssuesContext);

                // --- Step 3: Search the code base ---
                _logger.LogInformation("Step 3: Searching codebase based on payload: '{Payload}'", payload);
                var codeSearchPrompt = $"""
                Background: You are an AI assistant helping to research a GitHub issue.
                The GitHub issue context below (referred to as 'payload') contains the full webhook data from GitHub.
                Task: First, infer the repository details (owner and repository name) from the provided payload: '{payload}'.
                Then, using these inferred repository details, search the codebase of that GitHub repository for code snippets, comments, or documentation relevant to the issue described in the payload.
                Instructions: Summarize any key findings. If specific file paths or code blocks are identified as highly relevant, mention them. Limit to 3 most relevant findings.
                IMPORTANT: If using a tool function that accepts a 'per_page' parameter (like GitHubMcp-search_code), you MUST provide the value as a JSON number (e.g., 3), NOT as a JSON string (e.g., "3").
                If no relevant code is found, clearly state that.
                Your response will be used as context for another AI.
                """;
                var codeSearchResult = await _kernel.InvokePromptAsync(codeSearchPrompt, new KernelArguments(executionSettings));
                var codeSearchContext = codeSearchResult.GetValue<string>() ?? "No relevant code snippets found by the LLM.";
                _logger.LogInformation("Step 3 - Code search result: {Context}", codeSearchContext);

                // --- Step 4: Give a useful response using an LLM ---
                _logger.LogInformation("Step 4: Synthesizing a comment for original issue based on payload: '{Payload}'", payload);
                var finalResponsePrompt = $"""
                You are an AI assistant tasked with drafting and posting a single, helpful, and context-aware comment to the original GitHub issue that triggered this process.
                The original GitHub webhook payload is provided below. You MUST infer the repository details (owner/name) AND the original issue number from this payload to post your comment correctly.

                Original GitHub Webhook Payload:
                '''{payload}'''

                Here is the background research conducted from previous steps. Use this information to formulate your comment:

                1. Similar Answered/Closed Issues Found:
                '''
                {answeredIssuesContext}
                '''

                2. Related Outstanding/Open Issues Found:
                '''
                {outstandingIssuesContext}
                '''

                3. Relevant Code Search Results from the Repository (e.g., from READMEs, documentation, or code comments):
                '''
                {codeSearchContext}
                '''

                Task:
                1. Carefully review the Original GitHub Webhook Payload and all provided Background Research.
                2. Synthesize this information to draft ONE comprehensive and constructive comment for the original GitHub issue.
                3. Your comment should provide advice, point to relevant existing issues (answered or open), or highlight relevant code/documentation snippets.
                4. Address the user/actor from the original payload if appropriate. Be empathetic.
                5. Structure your comment clearly. You can use markdown for formatting.
                6. CRITICALLY IMPORTANT: Use the available GitHub tool (e.g., a function like 'add_issue_comment') to post this single comment to the original issue. You must correctly pass the inferred repository details and issue number to the tool.
                7. After attempting to post the comment, your final output should be a confirmation message stating whether the comment was successfully posted (and if so, its URL or ID if available from the tool) or if an error occurred.
                   Example success: "Successfully posted comment to issue [owner]/[repo]#[issue_number]. Comment ID: [comment_id]"
                   Example failure: "Failed to post comment to issue [owner]/[repo]#[issue_number]. Error: [error_details]"

                Do NOT create a new issue. Do NOT post multiple comments. Your primary goal is to provide a single, helpful comment on the triggering issue, using the research provided.
                If the research yielded no specific results for some steps, acknowledge that tactfully if relevant, and formulate the best possible comment with the available information.
                Do not invent information not present in the context provided.
                """;
                
                var synthesisExecutionSettings = new OpenAIPromptExecutionSettings
                {
                    // ToolCallBehavior = ToolCallBehavior.None, // Default behavior is likely no auto-invocation
                    Temperature = 0, 
                    MaxTokens = 1000 
                };

                var synthesizedCommentResult = await _kernel.InvokePromptAsync(finalResponsePrompt, new KernelArguments(synthesisExecutionSettings));
                var finalResponse = synthesizedCommentResult.GetValue<string>() ?? "Could not generate a final response at this time.";

                _logger.LogInformation("Generated final response: {Response}", finalResponse);

                // --- Step 5: Attempt to post the synthesized comment ---
                _logger.LogInformation("Step 5: Attempting to post synthesized comment based on payload: '{Payload}'", payload);
                var postCommentPrompt = $"""
                You are an AI assistant. Your explicit task is to:
                Post the following comment text with add_issue_comment tool:
                '''
                {finalResponse}
                '''
                To the GitHub issue identified by an 'issue_number' on the 'owner/repo' backlog.

                To do this, you MUST:
                1. Infer the specific 'issue_number', 'owner' (e.g., the GitHub username or organization), and 'repo' (e.g., the repository name) from the 'Original GitHub Webhook Payload' provided below.
                2. Use your available GitHub tools (e.g., a function like 'add_issue_comment') to perform this posting action.

                CRITICAL PARAMETER INSTRUCTIONS:
                - The 'issue_number' parameter, when passed to any tool, MUST be a JSON NUMBER (e.g., 123 or 7), NOT a JSON STRING (e.g., "123" or "7").
                - The same applies to any 'perPage' or 'page' parameters for other tools: they MUST be JSON numbers.

                Original GitHub Webhook Payload (for inferring issue_number, owner, repo):
                '''{payload}'''

                Your Response Format:
                After attempting to post the comment, your entire output for this step MUST be a single confirmation message formatted as follows:
                - If successful: "Successfully posted comment to issue [owner]/[repo]#[issue_number]. Comment ID: [comment_id]"
                - If failed: "Failed to post comment to issue [owner]/[repo]#[issue_number]. Error: [error_details]"
                  (Replace bracketed placeholders with actual values. If a value isn't available from the tool, omit it or state N/A.)

                IMPORTANT: Do NOT modify the comment text. Your sole responsibility is to accurately infer the target and post the provided comment using your tools.
                """;

                var postCommentExecutionSettings = new OpenAIPromptExecutionSettings
                {
                    // ToolCallBehavior = ToolCallBehavior.None, // Default behavior is likely no auto-invocation
                    Temperature = 0, 
                    MaxTokens = 1000 
                };

                var postCommentResult = await _kernel.InvokePromptAsync(postCommentPrompt, new KernelArguments(postCommentExecutionSettings));
                var postCommentResponse = postCommentResult.GetValue<string>() ?? "Could not post the comment at this time.";

                _logger.LogInformation("Post comment result: {Response}", postCommentResponse);
                return Ok(new { response = postCommentResponse });
            }
            catch (Exception ex)
            { 
                _logger.LogError(ex, "Error processing request for payload: '{Payload}'", payload);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while processing your request. Check service logs for details.");
            }
        }
    }
}