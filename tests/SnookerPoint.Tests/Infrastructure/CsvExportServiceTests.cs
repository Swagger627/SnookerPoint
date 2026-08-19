using SnookerPoint.Application.Reporting;
using SnookerPoint.Infrastructure.Services;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class CsvExportServiceTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("has \"quote\"", "\"has \"\"quote\"\"\"")]
    [InlineData("line\nbreak", "\"line\nbreak\"")]
    public void Escape_HandlesSpecialCharacters(string input, string expected)
    {
        Assert.Equal(expected, CsvExportService.Escape(input));
    }

    [Theory]
    [InlineData("=1+2", "'=1+2")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("@x", "'@x")]
    public void Escape_NeutralisesFormulaInjection(string input, string expected)
    {
        Assert.Equal(expected, CsvExportService.Escape(input));
    }

    [Fact]
    public void Escape_LeavesMoneyStringsIntact()
    {
        Assert.Equal("Rs 1,234", CsvExportService.Escape("Rs 1,234").Trim('"')); // quoted for the comma, but value intact
        Assert.Equal("-Rs 5", CsvExportService.Escape("-Rs 5")); // leading minus on money is not treated as a formula
    }

    [Fact]
    public void Export_WritesFile_PreservingBarcodeLeadingZeroes()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        var doc = new CsvDocument("Test-Export",
            new[] { "Name", "Barcode" },
            new List<IReadOnlyList<string>> { new[] { "Cola", "0012345" } });

        var result = env.Csv.Export(doc, null, ownerId);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(File.Exists(result.Value));

        var text = File.ReadAllText(result.Value!);
        Assert.Contains("Name,Barcode", text);
        Assert.Contains("0012345", text); // leading zero preserved in the file
    }

    [Fact]
    public void Export_UsesDefaultExportsFolder_WhenNoDestination()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        var doc = new CsvDocument("Folder-Test", new[] { "A" }, new List<IReadOnlyList<string>> { new[] { "1" } });
        var result = env.Csv.Export(doc, null, ownerId);

        Assert.True(result.Succeeded);
        Assert.StartsWith(env.Csv.DefaultExportsFolder, result.Value);
    }
}
