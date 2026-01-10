using Microsoft.AspNetCore.Mvc;
using VisitorManagementAI.Services;

namespace VisitorManagementAI.Controllers;

public class ChatRequest
{
    public string Prompt { get; set; } = string.Empty;
    public int SiteId { get; set; }
    public string OutputMethod { get; set; } = "Chat";
}

public class HomeController(IVisitorQueryService queryService, ILogger<HomeController> logger) : Controller
{
    public IActionResult Index()
    {
        return View();
    }
    
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