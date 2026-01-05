namespace MCPServer.Models;

/// <summary>
/// Dense, query-friendly subset of CheckinDto.
/// IMPORTANT: Property names match CheckinDto exactly for easy future swap.
///
/// Notes / conventions for the sample:
/// - VisitorCustomOne is used as "Unit/Lot" (e.g., "ABC", "A-1203")
/// - SiteId is used as "tenant/customer partition key" in sample data
/// </summary>
public sealed class CheckinDenseDto
{
    // Identity / partition
    public int CheckinId { get; set; }
    public int SiteId { get; set; }                 // tenant/customer boundary in shared environments
    public int VisitPropertyId { get; set; }        // property/site grouping if relevant

    // Who / what
    public string? VisitorName { get; set; }
    public string? VisitorCompany { get; set; }
    public string? VisitorEmail { get; set; }
    public string? VisitorPhonenumber { get; set; }

    // Unit/Lot (stored in custom field to match existing DTO naming)
    public string? VisitorCustomOne { get; set; }   // Unit/Lot

    // Vehicle
    public string? VisitorVehicleRegistrationPlate { get; set; }
    public bool VisitorVehicleRegistrationPlateVerified { get; set; }
    public string? VisitorVehicleDescription { get; set; }
    public string? VisitorVehicleMake { get; set; }
    public string? VisitorVehicleColor { get; set; }
    public string? VisitorVehicleState { get; set; }
    public string? VisitorVehicleCountry { get; set; }

    // Timing
    public DateTime CheckinTimestamp { get; set; }
    public DateTime CheckinTimestampExtra { get; set; }
    public DateTime? CheckoutTimestamp { get; set; }
    public DateTime? CheckoutTimestampExtra { get; set; }

    // Visit metadata (useful for filtering/grouping)
    public int VisitNumber { get; set; }
    public int VisitorType { get; set; }
    public int VisitType { get; set; }
    public int VisitTypeCategory { get; set; }
    public string? VisitTypeCategoryName { get; set; }
    public string? VisitGuid { get; set; }

    // Receiver (who is being visited)
    public int VisitingReceiverId { get; set; }

    // Flags often queried
    public bool VisitorIsGuest { get; set; }
    public bool VisitorIsVendor { get; set; }
    public string? VendorName { get; set; }
    public bool VisitorIsParcelDelivery { get; set; }
    public string? VisitorParcelCompany { get; set; }
    public string? VisitorParcelTrackingNumber { get; set; }

    // Useful for “what happened”
    public bool VisitorTurnedAway { get; set; }
    public int VisitorCheckinSuccess { get; set; }
    public string? VisitGuardExternalNote { get; set; }
}

public static class CheckinDenseSampleData
{
    /// <summary>
    /// Generates deterministic sample data for searching/testing.
    /// Includes guaranteed records for queries like:
    /// - plate "ABC123" in December 2025
    /// - unit "ABC" on Friday Jan 2, 2026 (Europe/Stockholm context)
    /// </summary>
    public static List<CheckinDenseDto> Generate(int n, int seed = 1337)
    {
        if (n <= 0) return new List<CheckinDenseDto>();

        var rng = new Random(seed);

        var firstNames = new[] { "Alex", "Sam", "Taylor", "Jordan", "Casey", "Robin", "Avery", "Jamie", "Morgan", "Riley" };
        var lastNames = new[] { "Andersson", "Johansson", "Karlsson", "Nilsson", "Eriksson", "Larsson", "Olsson", "Persson", "Svensson", "Gustafsson" };

        var companies = new[] { "Northgate AB", "Blue Harbor", "Krona Tech", "Sundby Consulting", "Arbor Works", "ParcelPro", "Vendorline", "Site Services" };
        var makes = new[] { "Volvo", "Toyota", "Volkswagen", "BMW", "Tesla", "Ford", "Kia", "Skoda" };
        var colors = new[] { "Black", "White", "Silver", "Blue", "Red", "Grey", "Green" };
        var units = new[] { "ABC", "A-1203", "B-0402", "C-0901", "D-1507", "E-0101", "F-2210" };

        // Spread across late 2025 and early 2026 so you can test “this December”, “this Friday”, etc.
        var windowStart = new DateTime(2025, 11, 15, 0, 0, 0, DateTimeKind.Unspecified);
        var windowEnd = new DateTime(2026, 01, 10, 23, 59, 59, DateTimeKind.Unspecified);

        // Two tenants/sites to test isolation at the provisioning layer
        var siteIds = new[] { 1001, 2001 };

        var list = new List<CheckinDenseDto>(n);

        // --- Guaranteed “known hits” ---
        // Friday before Sat Jan 3, 2026 is Jan 2, 2026
        var guaranteedFriday = new DateTime(2026, 01, 02, 14, 35, 00, DateTimeKind.Unspecified);

        list.Add(new CheckinDenseDto
        {
            CheckinId = 1,
            SiteId = 1001,
            VisitPropertyId = 77,

            VisitorName = "Alex Andersson",
            VisitorCompany = "Northgate AB",
            VisitorEmail = "alex.andersson@example.test",
            VisitorPhonenumber = "+46-70-111-2233",

            VisitorCustomOne = "ABC", // unit

            VisitorVehicleRegistrationPlate = "ABC123",
            VisitorVehicleRegistrationPlateVerified = true,
            VisitorVehicleDescription = "Blue sedan",
            VisitorVehicleMake = "Volvo",
            VisitorVehicleColor = "Blue",
            VisitorVehicleState = "STHLM",
            VisitorVehicleCountry = "SE",

            CheckinTimestamp = new DateTime(2025, 12, 12, 09, 10, 00, DateTimeKind.Unspecified), // December hit
            CheckinTimestampExtra = new DateTime(2025, 12, 12, 09, 10, 10, DateTimeKind.Unspecified),
            CheckoutTimestamp = new DateTime(2025, 12, 12, 11, 45, 00, DateTimeKind.Unspecified),
            CheckoutTimestampExtra = new DateTime(2025, 12, 12, 11, 45, 10, DateTimeKind.Unspecified),

            VisitNumber = 10001,
            VisitorType = 1,
            VisitType = 1,
            VisitTypeCategory = 10,
            VisitTypeCategoryName = "Visitor",
            VisitGuid = Guid.NewGuid().ToString("N"),

            VisitingReceiverId = 501,

            VisitorIsGuest = true,
            VisitorIsVendor = false,
            VendorName = null,
            VisitorIsParcelDelivery = false,
            VisitorParcelCompany = null,
            VisitorParcelTrackingNumber = null,

            VisitorTurnedAway = false,
            VisitorCheckinSuccess = 1,
            VisitGuardExternalNote = "Arrived on time."
        });

        list.Add(new CheckinDenseDto
        {
            CheckinId = 2,
            SiteId = 1001,
            VisitPropertyId = 77,

            VisitorName = "Sam Nilsson",
            VisitorCompany = "Krona Tech",
            VisitorEmail = "sam.nilsson@example.test",
            VisitorPhonenumber = "+46-70-222-3344",

            VisitorCustomOne = "ABC", // unit

            VisitorVehicleRegistrationPlate = "ZZZ999",
            VisitorVehicleRegistrationPlateVerified = false,
            VisitorVehicleDescription = "White SUV",
            VisitorVehicleMake = "Toyota",
            VisitorVehicleColor = "White",
            VisitorVehicleState = "STHLM",
            VisitorVehicleCountry = "SE",

            CheckinTimestamp = guaranteedFriday, // “this Friday” hit (Jan 2, 2026)
            CheckinTimestampExtra = guaranteedFriday.AddSeconds(8),
            CheckoutTimestamp = null,
            CheckoutTimestampExtra = null,

            VisitNumber = 10002,
            VisitorType = 1,
            VisitType = 1,
            VisitTypeCategory = 10,
            VisitTypeCategoryName = "Visitor",
            VisitGuid = Guid.NewGuid().ToString("N"),

            VisitingReceiverId = 502,

            VisitorIsGuest = true,
            VisitorIsVendor = false,
            VisitorIsParcelDelivery = false,

            VisitorTurnedAway = false,
            VisitorCheckinSuccess = 1,
            VisitGuardExternalNote = "Waiting for host confirmation."
        });

        // Ensure tenant B has some similar-looking data to validate isolation (but should never be provided together)
        list.Add(new CheckinDenseDto
        {
            CheckinId = 3,
            SiteId = 2001,
            VisitPropertyId = 88,

            VisitorName = "Taylor Svensson",
            VisitorCompany = "Site Services",
            VisitorEmail = "taylor.svensson@example.test",
            VisitorPhonenumber = "+46-70-333-4455",

            VisitorCustomOne = "ABC",
            VisitorVehicleRegistrationPlate = "ABC123",
            VisitorVehicleRegistrationPlateVerified = true,

            VisitorVehicleMake = "Volkswagen",
            VisitorVehicleColor = "Grey",
            VisitorVehicleCountry = "SE",

            CheckinTimestamp = new DateTime(2025, 12, 05, 16, 20, 00, DateTimeKind.Unspecified),
            CheckinTimestampExtra = new DateTime(2025, 12, 05, 16, 20, 06, DateTimeKind.Unspecified),

            VisitNumber = 20001,
            VisitorType = 2,
            VisitType = 2,
            VisitTypeCategory = 20,
            VisitTypeCategoryName = "Vendor",
            VisitGuid = Guid.NewGuid().ToString("N"),

            VisitingReceiverId = 801,

            VisitorIsGuest = false,
            VisitorIsVendor = true,
            VendorName = "Vendorline",

            VisitorTurnedAway = false,
            VisitorCheckinSuccess = 1
        });

        // --- Fill the remaining rows with randomized but searchable data ---
        var nextId = 4;
        while (list.Count < n)
        {
            var siteId = siteIds[rng.Next(siteIds.Length)];
            var first = firstNames[rng.Next(firstNames.Length)];
            var last = lastNames[rng.Next(lastNames.Length)];
            var name = $"{first} {last}";

            var unit = units[rng.Next(units.Length)];
            var company = companies[rng.Next(companies.Length)];
            var make = makes[rng.Next(makes.Length)];
            var color = colors[rng.Next(colors.Length)];

            var checkinTs = RandomDateTime(rng, windowStart, windowEnd);

            // Some will check out, some won’t
            DateTime? checkoutTs = rng.NextDouble() < 0.75
                ? checkinTs.AddMinutes(10 + rng.Next(240))
                : null;

            // Generate plates like "KLP482" (easy to search)
            var plate = RandomPlate(rng);

            var isVendor = rng.NextDouble() < 0.15;
            var isParcel = !isVendor && rng.NextDouble() < 0.10;

            list.Add(new CheckinDenseDto
            {
                CheckinId = nextId++,
                SiteId = siteId,
                VisitPropertyId = siteId == 1001 ? 77 : 88,

                VisitorName = name,
                VisitorCompany = company,
                VisitorEmail = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}@example.test",
                VisitorPhonenumber = $"+46-70-{rng.Next(100, 999)}-{rng.Next(1000, 9999)}",

                VisitorCustomOne = unit,

                VisitorVehicleRegistrationPlate = plate,
                VisitorVehicleRegistrationPlateVerified = rng.NextDouble() < 0.60,
                VisitorVehicleDescription = $"{color} {make}",
                VisitorVehicleMake = make,
                VisitorVehicleColor = color,
                VisitorVehicleState = "STHLM",
                VisitorVehicleCountry = "SE",

                CheckinTimestamp = checkinTs,
                CheckinTimestampExtra = checkinTs.AddSeconds(rng.Next(1, 15)),
                CheckoutTimestamp = checkoutTs,
                CheckoutTimestampExtra = checkoutTs?.AddSeconds(rng.Next(1, 15)),

                VisitNumber = siteId == 1001 ? (10000 + nextId) : (20000 + nextId),
                VisitorType = isVendor ? 2 : 1,
                VisitType = isVendor ? 2 : 1,
                VisitTypeCategory = isVendor ? 20 : 10,
                VisitTypeCategoryName = isVendor ? "Vendor" : "Visitor",
                VisitGuid = Guid.NewGuid().ToString("N"),

                VisitingReceiverId = siteId == 1001 ? (500 + rng.Next(1, 50)) : (800 + rng.Next(1, 50)),

                VisitorIsGuest = !isVendor,
                VisitorIsVendor = isVendor,
                VendorName = isVendor ? "Vendorline" : null,

                VisitorIsParcelDelivery = isParcel,
                VisitorParcelCompany = isParcel ? "ParcelPro" : null,
                VisitorParcelTrackingNumber = isParcel ? $"TRK-{rng.Next(100000, 999999)}" : null,

                VisitorTurnedAway = rng.NextDouble() < 0.03,
                VisitorCheckinSuccess = 1,
                VisitGuardExternalNote = rng.NextDouble() < 0.10 ? "Manual verification performed." : null
            });
        }

        return list;
    }

    private static DateTime RandomDateTime(Random rng, DateTime start, DateTime end)
    {
        var rangeSeconds = (end - start).TotalSeconds;
        var offset = rng.NextDouble() * rangeSeconds;
        return start.AddSeconds(offset);
    }

    private static string RandomPlate(Random rng)
    {
        // Simple and readable: 3 letters + 3 digits
        char L() => (char)('A' + rng.Next(0, 26));
        return $"{L()}{L()}{L()}{rng.Next(0, 10)}{rng.Next(0, 10)}{rng.Next(0, 10)}";
    }
}