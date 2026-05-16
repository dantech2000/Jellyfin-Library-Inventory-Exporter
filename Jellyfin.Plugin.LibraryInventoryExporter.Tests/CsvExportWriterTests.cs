using Jellyfin.Plugin.LibraryInventoryExporter.Services;
using Xunit;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Tests;

public sealed class CsvExportWriterTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("a\nb", "\"a\nb\"")]
    public void Escape_HandlesCsvSpecialCharacters(string input, string expected)
    {
        Assert.Equal(expected, CsvExportWriter.Escape(input));
    }
}
