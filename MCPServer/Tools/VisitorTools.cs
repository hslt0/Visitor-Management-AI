using System.ComponentModel;
using System.Text.Json;
using MCPServer.Data;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace MCPServer.Tools;

[McpServerToolType]
public class VisitorTools(AppDbContext db)
{
    [McpServerTool, Description("Finds visitors by name or plate." +
                                " Required input param: query is visitor name (e.g., 'Alex') or plate (e.g., 'ABC123') without extra words." +
                                " Optional input param: daysLookBack is amount of day you need to look back (default is 30, i.e., last month) ")]
    public async Task<string> FindVisitor(string query, int siteId, int daysLookBack = 30)
    {
        var date = DateTime.Today.AddDays(-daysLookBack);
        
        query = query.Trim();
        
        var visitors = await db.Checkins
            .Where(c => c.SiteId == siteId
                        && c.CheckinTimestamp >= date
                        && c.VisitorName != null 
                        && (c.VisitorVehicleRegistrationPlate == query || c.VisitorName.Contains(query)))
            .OrderByDescending(c => c.CheckinTimestamp)
            .Take(50)
            .Select(c => new 
            { 
                c.VisitorName, 
                c.VisitorCustomOne, 
                c.CheckinTimestamp, 
                c.CheckoutTimestamp,
                c.VisitorVehicleRegistrationPlate 
            })
            .ToListAsync();

        return visitors.Any() ? JsonSerializer.Serialize(visitors) : "No visitors found for this query.";
    }

    [McpServerTool, Description("Lists all visitors who visited a specific unit." +
                                " Required input param: unit (e.g., ABC, 101)." +
                                " Optional input param: daysLookBack is amount of day you need to look back (default is 30, i.e., last month)")]
    public async Task<string> GetUnitVisitors(string unit, int siteId, int daysLookBack = 30)
    {
        var date = DateTime.Today.AddDays(-daysLookBack);
            
        var visitors = await db.Checkins
            .Where(c => c.SiteId == siteId
                        && c.VisitorCustomOne == unit
                        && c.CheckinTimestamp >= date)
            .OrderByDescending(c => c.CheckinTimestamp)
            .Take(50)
            .Select(c => new { c.VisitorName, c.VisitorCustomOne, c.CheckinTimestamp, c.CheckoutTimestamp })
            .ToListAsync();

        return visitors.Any() ? JsonSerializer.Serialize(visitors) : $"No one found in unit {unit}.";
    }
}