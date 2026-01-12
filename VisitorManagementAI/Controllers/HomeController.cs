using Microsoft.AspNetCore.Mvc;
using VisitorManagementAI.Services;

namespace VisitorManagementAI.Controllers;

/// <summary>
/// Represents a chat request from the client.
/// </summary>
public class ChatRequest
{
    /// <summary>
    /// The user's question or prompt.
    /// </summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>
    /// The ID of the site context for the query.
    /// </summary>
    public int SiteId { get; set; }

    /// <summary>
    /// The desired output method (default is "Chat").
    /// </summary>
    public string OutputMethod { get; set; } = "Chat";
}

/// <summary>
/// Controller for handling home page and chat interactions.
/// </summary>
/// <param name="queryService">The service for processing visitor queries.</param>
/// <param name="logger">The logger instance.</param>
public class HomeController(IVisitorQueryService queryService, ILogger<HomeController> logger) : Controller
{
    /// <summary>
    /// Renders the main view.
    /// </summary>
    public IActionResult Index()
    {
        return View();
    }
    
    /// <summary>
    /// Handles the chat API request.
    /// </summary>
    /// <param name="request">The chat request containing the prompt and site ID.</param>
    /// <returns>A JSON response with the AI's answer and metadata.</returns>
    [HttpPost("/api/home/ask")]
    public async Task<IActionResult> Ask([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt)) 
            return BadRequest("Prompt is empty");

        if (request.SiteId <= 0) 
            return BadRequest("Invalid Site ID");
        
        if (!request.OutputMethod.Equals("Chat", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Invalid output method");

        try 
        {
            var result = await queryService.ChatAsync(request.Prompt, request.SiteId);

            return Ok(new 
            { 
                answer = result.AiResponseText,
                dataContext = result.DataContext,
                toolUsed = result.ToolUsed,
                timestamp = result.Timestamp
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing request");
            return StatusCode(500, new { answer = "Error processing request: " + ex.Message });
        }
    }
}