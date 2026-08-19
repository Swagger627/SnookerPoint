using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Catalog;
using SnookerPoint.Application.Common;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Optional, manually-triggered barcode metadata lookup via Open Food Facts. It never
/// returns a price and never touches local data. Any network failure or missing result is
/// reported as a friendly message so the offline-first app is never destabilised.
/// </summary>
public sealed class OpenFoodFactsLookupProvider : IProductLookupProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    private readonly ILogger<OpenFoodFactsLookupProvider> _logger;

    public OpenFoodFactsLookupProvider(ILogger<OpenFoodFactsLookupProvider> logger)
    {
        _logger = logger;
    }

    public async Task<OperationResult<ProductLookupResult>> LookupAsync(string barcode, CancellationToken cancellationToken = default)
    {
        var code = barcode?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            return OperationResult<ProductLookupResult>.Failure("Please enter a barcode to look up.");
        }

        try
        {
            var url = $"https://world.openfoodfacts.org/api/v2/product/{Uri.EscapeDataString(code)}.json";
            using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return OperationResult<ProductLookupResult>.Failure("The lookup service could not be reached. You can still enter the details manually.");
            }

            var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!doc.TryGetProperty("status", out var status) || status.GetInt32() != 1 ||
                !doc.TryGetProperty("product", out var product))
            {
                return OperationResult<ProductLookupResult>.Failure("No online record was found for that barcode. Please enter the details manually.");
            }

            var result = new ProductLookupResult(
                Barcode: code,
                Name: ReadString(product, "product_name"),
                Brand: ReadString(product, "brands"),
                Size: ReadString(product, "quantity"),
                Category: FirstCategory(ReadString(product, "categories")));

            return OperationResult<ProductLookupResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Online barcode lookup failed (offline or unavailable).");
            return OperationResult<ProductLookupResult>.Failure("No internet connection was available for the lookup. You can still enter the details manually.");
        }
    }

    private static string? ReadString(JsonElement product, string name) =>
        product.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? EmptyToNull(value.GetString())
            : null;

    private static string? FirstCategory(string? categories) =>
        string.IsNullOrWhiteSpace(categories) ? null : categories.Split(',').Last().Trim();

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
