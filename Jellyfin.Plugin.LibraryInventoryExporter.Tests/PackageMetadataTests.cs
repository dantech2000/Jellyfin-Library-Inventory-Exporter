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
        Assert.Contains("version: \"0.1.4.0\"", buildYaml);
        Assert.Contains("targetAbi: \"10.11.0.0\"", buildYaml);
        Assert.Contains("owner: \"dantech2000\"", buildYaml);
        Assert.Contains("repositoryName: \"Library Inventory Exporter\"", buildYaml);
        Assert.Contains("repositoryUrl: \"https://raw.githubusercontent.com/dantech2000/Jellyfin-Library-Inventory-Exporter/gh-pages/manifest.json\"", buildYaml);
        Assert.Contains("imageUrl: \"https://raw.githubusercontent.com/dantech2000/Jellyfin-Library-Inventory-Exporter/main/assets/library-inventory-exporter.png\"", buildYaml);
        Assert.Contains("<Version>0.1.4.0</Version>", project);
        Assert.Contains("<AssemblyVersion>0.1.4.0</AssemblyVersion>", project);
        Assert.Contains("<NoWarn>$(NoWarn);CS1591</NoWarn>", project);
        Assert.Contains("<PackageReference Include=\"Jellyfin.Controller\" Version=\"10.11.3\">", project);
        Assert.Contains("Include=\"meta.json\" CopyToPublishDirectory=\"PreserveNewest\"", project);
        Assert.Contains("Link=\"library-inventory-exporter.png\" CopyToPublishDirectory=\"PreserveNewest\"", project);
    }

    [Fact]
    public void Workflows_CoverCiReleaseChecksumsAndManifestPublishing()
    {
        var root = TestPaths.RepositoryRoot();
        var ci = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        var release = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        var publish = File.ReadAllText(Path.Combine(root, ".github", "workflows", "publish-manifest.yml"));

        Assert.Contains("actions/checkout@v5", ci);
        Assert.Contains("actions/setup-dotnet@v5", ci);
        Assert.Contains("dotnet restore", ci);
        Assert.Contains("dotnet build --configuration Release --no-restore", ci);
        Assert.Contains("dotnet test --configuration Release --no-build", ci);
        Assert.DoesNotContain("actions/checkout@v4", ci);
        Assert.DoesNotContain("actions/setup-dotnet@v4", ci);
        Assert.Contains("actions/checkout@v5", release);
        Assert.Contains("actions/setup-dotnet@v5", release);
        Assert.Contains("dotnet publish ./Jellyfin.Plugin.LibraryInventoryExporter/Jellyfin.Plugin.LibraryInventoryExporter.csproj", release);
        Assert.Contains("zip -r ../Jellyfin.Plugin.LibraryInventoryExporter.${GITHUB_REF_NAME}.zip", release);
        Assert.Contains("md5sum Jellyfin.Plugin.LibraryInventoryExporter.${GITHUB_REF_NAME}.zip", release);
        Assert.Contains("softprops/action-gh-release@v2", release);
        Assert.DoesNotContain("actions/checkout@v4", release);
        Assert.DoesNotContain("actions/setup-dotnet@v4", release);
        Assert.Contains("actions/checkout@v5", publish);
        Assert.Contains("actions/setup-python@v6", publish);
        Assert.Contains("workflow_run:", publish);
        Assert.Contains("workflows: [\"Release\"]", publish);
        Assert.Contains("github.event.workflow_run.conclusion == 'success'", publish);
        Assert.Contains("python source/tools/generate_manifest.py source/build.yaml pages/manifest.json", publish);
        Assert.Contains("git checkout --orphan gh-pages", publish);
        Assert.Contains("git add manifest.json", publish);
        Assert.Contains("git push origin gh-pages", publish);
        Assert.DoesNotContain("actions/checkout@v4", publish);
        Assert.DoesNotContain("actions/setup-python@v5", publish);
    }

    [Fact]
    public void PluginAdminPage_WiresRequiredEndpoints()
    {
        var root = TestPaths.RepositoryRoot();
        var html = File.ReadAllText(Path.Combine(root, "Jellyfin.Plugin.LibraryInventoryExporter", "Configuration", "configPage.html"));
        var script = File.ReadAllText(Path.Combine(root, "Jellyfin.Plugin.LibraryInventoryExporter", "Configuration", "configPage.js"));
        var controller = File.ReadAllText(Path.Combine(root, "Jellyfin.Plugin.LibraryInventoryExporter", "Controllers", "InventoryExporterController.cs"));

        Assert.Contains("id=\"chkAllLibraries\"", html);
        Assert.Contains("id=\"libraryList\"", html);
        Assert.Contains("id=\"outputDirectoryOptions\"", html);
        Assert.DoesNotContain("id=\"selLibraries\"", html);
        Assert.Contains("InventoryExporter/Export", script);
        Assert.Contains("InventoryExporter/Exports", script);
        Assert.Contains("InventoryExporter/Exports/Latest", script);
        Assert.Contains("InventoryExporter/Libraries", script);
        Assert.Contains("InventoryExporter/OutputDirectories", script);
        Assert.Contains("data-library-id", script);
        Assert.Contains("data-output-directory", script);
        Assert.Contains("selectedLibraryIds()", script);
        Assert.Contains("scheduleStatusRefresh", script);
        Assert.Contains("setTimeout(refreshStatus, 2000)", script);
        Assert.Contains("Export Running", script);
        Assert.Contains("No exports yet.", script);
        Assert.Contains("getDownloadUrl", script);
        Assert.Contains("api_key=", script);
        Assert.DoesNotContain("selectedOptions", script);
        Assert.DoesNotContain("#selLibraries", script);
        Assert.Contains("data-delete-export", script);
        Assert.Contains("[Authorize(Policy = \"RequiresElevation\")]", controller);
        Assert.Contains("[HttpGet(\"OutputDirectories\")]", controller);
        Assert.Contains("[HttpPost(\"Export\")]", controller);
        Assert.Contains("[HttpGet(\"Exports/{id}/Download\")]", controller);
        Assert.Contains("[HttpDelete(\"Exports/{id}\")]", controller);
    }

    [Fact]
    public void Readme_DocumentsJellyfinRepositoryInstallFlow()
    {
        var root = TestPaths.RepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.Contains("https://raw.githubusercontent.com/dantech2000/Jellyfin-Library-Inventory-Exporter/gh-pages/manifest.json", readme);
        Assert.Contains("Open `Dashboard`", readme);
        Assert.Contains("Open `Catalog`", readme);
        Assert.Contains("Install the latest compatible version", readme);
        Assert.Contains("Restart Jellyfin", readme);
        Assert.Contains("raw GitHub URL above is the most direct Jellyfin repository URL", readme);
    }

    [Fact]
    public void PluginCatalogImageAsset_IsPresent()
    {
        var root = TestPaths.RepositoryRoot();
        var imagePath = Path.Combine(root, "assets", "library-inventory-exporter.png");
        var metaJson = File.ReadAllText(Path.Combine(root, "Jellyfin.Plugin.LibraryInventoryExporter", "meta.json"));

        Assert.True(File.Exists(imagePath));
        Assert.True(new FileInfo(imagePath).Length > 10_000);
        Assert.Contains("\"imagePath\": \"library-inventory-exporter.png\"", metaJson);
        Assert.Contains("\"id\": \"7184fe02-8e91-4fd2-9140-6d58d5e91f0a\"", metaJson);
    }
}
