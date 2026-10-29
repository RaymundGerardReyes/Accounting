using System.Text.Json;

string pythonBaseUrl = args.Length > 0 ? args[0] : "http://localhost:5000";
string outputDir = args.Length > 1 ? args[1] : "./golden-fixtures";

Directory.CreateDirectory(outputDir);

string[] probeRoutes =
[
    "/health",
    "/api/health",
    "/api/dashboard",
    "/api/products",
    "/api/variance",
    "/api/cost-allocation",
    "/api/v1/orders",
    "/api/v1/finance/approval-matrix",
    "/api/v1/finance/users",
    "/api/v1/finance/tax-codes",
    "/api/v1/integrations/google-forms/status"
];

using var http = new HttpClient { BaseAddress = new Uri(pythonBaseUrl), Timeout = TimeSpan.FromSeconds(5) };

Console.WriteLine($"Exporting Golden-Master fixtures from {pythonBaseUrl} to {outputDir}...");

foreach (string route in probeRoutes)
{
    string safeName = route.Trim('/').Replace('/', '_');
    string targetFile = Path.Combine(outputDir, $"{safeName}.json");

    try
    {
        using HttpResponseMessage response = await http.GetAsync(route).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var fixture = new
        {
            route,
            status_code = (int)response.StatusCode,
            captured_at_utc = DateTimeOffset.UtcNow,
            body_json = body
        };

        await File.WriteAllTextAsync(
            targetFile,
            JsonSerializer.Serialize(fixture, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);

        Console.WriteLine($"  [CAPTURED] {route} -> {response.StatusCode}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  [OFFLINE]  {route} ({ex.Message})");
    }
}

return 0;
