using Xunit;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Tests;

public sealed class PackageMetadataTests
{
    [Fact]
    public void BuildYaml_MatchesProjectIdentityAndTarget()
    {
        var root = TestPaths.RepositoryRoot();
        var buildYaml = File.ReadAllText(Path.Combine(root, "build.yaml"));
        var project = File.ReadAllText(Path.Combine(root, "Jellyfin.Plugin.LibraryInventoryExporter", "Jellyfin.Plugin.LibraryInventoryExporter.csproj"));

        Assert.Contains("name: \"Library Inventory Exporter\"", buildYaml);
        Assert.Contains("guid: \"7184fe02-8e91-4fd2-9140-6d58d5e91f0a\"", buildYaml);
        Assert.Contains("version: \"0.1.0.0\"", buildYaml);
        Assert.Contains("targetAbi: \"10.11.0.0\"", buildYaml);
        Assert.Contains("owner: \"dantech2000\"", buildYaml);
        Assert.Contains("<Version>0.1.0.0</Version>", project);
        Assert.Contains("<AssemblyVersion>0.1.0.0</AssemblyVersion>", project);
        Assert.Contains("<PackageReference Include=\"Jellyfin.Controller\" Version=\"10.11.3\">", project);
    }

    [Fact]
    public void Workflows_CoverCiReleaseChecksumsAndManifestPublishing()
    {
        var root = TestPaths.RepositoryRoot();
        var ci = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        var release = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        var publish = File.ReadAllText(Path.Combine(root, ".github", "workflows", "publish-manifest.yml"));

        Assert.Contains("dotnet restore", ci);
        Assert.Contains("dotnet build --configuration Release --no-restore", ci);
        Assert.Contains("dotnet test --configuration Release --no-build", ci);
        Assert.Contains("dotnet publish ./Jellyfin.Plugin.LibraryInventoryExporter/Jellyfin.Plugin.LibraryInventoryExporter.csproj", release);
        Assert.Contains("zip -r ../Jellyfin.Plugin.LibraryInventoryExporter.${GITHUB_REF_NAME}.zip", release);
        Assert.Contains("md5sum Jellyfin.Plugin.LibraryInventoryExporter.${GITHUB_REF_NAME}.zip", release);
        Assert.Contains("softprops/action-gh-release@v2", release);
        Assert.Contains("python source/tools/generate_manifest.py source/build.yaml pages/manifest.json", publish);
        Assert.Contains("git checkout --orphan gh-pages", publish);
        Assert.Contains("git add manifest.json", publish);
        Assert.Contains("git push origin gh-pages", publish);
    }

    [Fact]
    public void PluginAdminPage_WiresRequiredEndpoints()
    {
        var root = TestPaths.RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "Jellyfin.Plugin.LibraryInventoryExporter", "Configuration", "configPage.js"));
        var controller = File.ReadAllText(Path.Combine(root, "Jellyfin.Plugin.LibraryInventoryExporter", "Controllers", "InventoryExporterController.cs"));

        Assert.Contains("InventoryExporter/Export", script);
        Assert.Contains("InventoryExporter/Exports", script);
        Assert.Contains("InventoryExporter/Exports/Latest", script);
        Assert.Contains("InventoryExporter/Libraries", script);
        Assert.Contains("data-delete-export", script);
        Assert.Contains("[Authorize(Policy = \"RequiresElevation\")]", controller);
        Assert.Contains("[HttpPost(\"Export\")]", controller);
        Assert.Contains("[HttpGet(\"Exports/{id}/Download\")]", controller);
        Assert.Contains("[HttpDelete(\"Exports/{id}\")]", controller);
    }

    [Fact]
    public void Readme_DocumentsJellyfinRepositoryInstallFlow()
    {
        var root = TestPaths.RepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.Contains("https://dantech2000.github.io/Jellyfin-Library-Inventory-Exporter/manifest.json", readme);
        Assert.Contains("Open `Dashboard`", readme);
        Assert.Contains("Open `Catalog`", readme);
        Assert.Contains("Install the latest compatible version", readme);
        Assert.Contains("Restart Jellyfin", readme);
        Assert.Contains("GitHub Pages must be enabled", readme);
    }
}
