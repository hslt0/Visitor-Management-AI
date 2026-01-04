using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using VisitorManagementAI.Data;

namespace VisitorManagementAI.Models;

[McpServerToolType]
public class VisitorTools(AppDbContext db)
{
    [McpServerTool, Description("Finds visitors by their name or license plate number. Returns a list of matches.")]
    public async Task<string> FindVisitor(string query, int siteId)
    {
        var visitors = await db.Checkins
            .Where(c => c.SiteId == siteId &&
                        c.VisitorName != null &&
                        (c.VisitorVehicleRegistrationPlate == query || c.VisitorName.Contains(query)))
            .OrderByDescending(c => c.CheckinTimestamp)
            .Take(5) // Беремо до 5 збігів
            .Select(c => new { c.VisitorName, c.VisitorCustomOne, c.CheckinTimestamp, c.VisitorVehicleRegistrationPlate })
            .ToListAsync();

        return visitors.Any() ? JsonSerializer.Serialize(visitors) : "No visitors found for this query.";
    }

    [McpServerTool, Description("Lists all visitors who visited a specific unit (e.g., ABC, 101).")]
    public async Task<string> GetUnitVisitors(string unit, int siteId)
    {
        var visitors = await db.Checkins
            .Where(c => c.SiteId == siteId && c.VisitorCustomOne == unit)
            .OrderByDescending(c => c.CheckinTimestamp)
            .Take(10)
            .Select(c => new { c.VisitorName, c.VisitorCustomOne, c.CheckinTimestamp })
            .ToListAsync();

        return visitors.Any() ? JsonSerializer.Serialize(visitors) : $"No one found in unit {unit}.";
    }
}