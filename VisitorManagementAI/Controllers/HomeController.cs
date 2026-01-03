using Microsoft.AspNetCore.Mvc;
using VisitorManagementAI.Services;

namespace VisitorManagementAI.Controllers;

public class HomeController(IVisitorQueryService queryService) : Controller
{
    public IActionResult Index()
    {
        return View();
    }
    
    [HttpPost("/api/home/ask")]
    public async Task<IActionResult> Ask([FromBody] ChatRequest request)
    {
        if (string.IsNullOrEmpty(request.Prompt)) return BadRequest("Prompt is empty");

        var response = await queryService.ChatAsync(request.Prompt, 1001);
        return Ok(new { answer = response });
    }
}

public record ChatRequest(string Prompt);