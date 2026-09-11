using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Tests;

public sealed class PackageMetadataTests
{
    [Fact]
    public void BuildYaml_MatchesProjectIdentity()
    {
        var root = TestPaths.RepositoryRoot();
        var buildYaml = File.ReadAllText(Path.Combine(root, "build.yaml"));
        var project = File.ReadAllText(Path.Combine(root, "Jellyfin.Plugin.LibraryInventoryExporter", "Jellyfin.Plugin.LibraryInventoryExporter.csproj"));

        Assert.Contains("name: \"Library Inventory Exporter\"", buildYaml);
        Assert.Contains("guid: \"7184fe02-8e91-4fd2-9140-6d58d5e91f0a\"", buildYaml);
        Assert.Contains($"version: \"{PluginVersion(root)}\"", buildYaml);
        Assert.DoesNotContain("targetAbi:", buildYaml);
        Assert.DoesNotContain("framework:", buildYaml);
        Assert.Contains("owner: \"dantech2000\"", buildYaml);
        Assert.Contains("repositoryName: \"Library Inventory Exporter\"", buildYaml);
        Assert.Contains("repositoryUrl: \"https://github.com/dantech2000/Jellyfin-Library-Inventory-Exporter\"", buildYaml);
        Assert.Contains("imageUrl: \"https://cdn.jsdelivr.net/gh/dantech2000/Jellyfin-Library-Inventory-Exporter@", buildYaml);
        Assert.Contains("/assets/library-inventory-exporter.png\"", buildYaml);
        Assert.Contains("- \"meta.json\"", buildYaml);
        Assert.Contains("- \"library-inventory-exporter.png\"", buildYaml);
        Assert.Contains("<TargetFrameworks>net9.0;net10.0</TargetFrameworks>", project);
        Assert.Contains("<Version>$(PluginVersion).$(JellyfinAbiRevision)</Version>", project);
        Assert.Contains("<NoWarn>$(NoWarn);CS1591</NoWarn>", project);
        Assert.Contains("<FrameworkReference Include=\"Microsoft.AspNetCore.App\" />", project);
        Assert.Contains("<PackageReference Include=\"Jellyfin.Controller\" Version=\"$(JellyfinVersion)\">", project);
        Assert.Contains("<PackageReference Include=\"Jellyfin.Model\" Version=\"$(JellyfinVersion)\">", project);
        Assert.Contains("Link=\"library-inventory-exporter.png\" CopyToPublishDirectory=\"PreserveNewest\"", project);
        Assert.Contains("<Target Name=\"WritePluginMeta\" AfterTargets=\"Build\"", project);
        Assert.Contains("<Target Name=\"PublishPluginMeta\" AfterTargets=\"Publish\"", project);
    }

    [Theory]
    [InlineData("net9.0", "10.11.11", "10.11.0.0", "10")]
    [InlineData("net10.0", "12.0.0", "12.0.0.0", "12")]
    public void BuildProps_MapEachTargetFrameworkToOneJellyfinLine(string targetFramework, string jellyfinVersion, string targetAbi, string revision)
    {
        var props = XDocument.Load(Path.Combine(TestPaths.RepositoryRoot(), "Directory.Build.props"));
        var group = Assert.Single(
            props.Root!.Elements("PropertyGroup"),
            g => (string?)g.Attribute("Condition") == $"'$(TargetFramework)' == '{targetFramework}'");

        Assert.Equal(jellyfinVersion, group.Element("JellyfinVersion")?.Value);
        Assert.Equal(targetAbi, group.Element("JellyfinTargetAbi")?.Value);
        Assert.Equal(revision, group.Element("JellyfinAbiRevision")?.Value);
    }

    [Fact]
    public void BuiltPlugin_CarriesTheVersionAndTargetAbiOfItsJellyfinLine()
    {
#if NET10_0_OR_GREATER
        const int revision = 12;
        const string targetAbi = "12.0.0.0";
#else
        const int revision = 10;
        const string targetAbi = "10.11.0.0";
#endif
        var root = TestPaths.RepositoryRoot();
        var assemblyVersion = typeof(Plugin).Assembly.GetName().Version!;
        var testOutput = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        var metaPath = Path.Combine(root, "Jellyfin.Plugin.LibraryInventoryExporter", "bin", testOutput.Parent!.Name, testOutput.Name, "meta.json");
        using var meta = JsonDocument.Parse(File.ReadAllText(metaPath));

        Assert.Equal(new Version(PluginVersion(root) + "." + revision), assemblyVersion);
        Assert.Equal(assemblyVersion.ToString(), meta.RootElement.GetProperty("version").GetString());
        Assert.Equal(targetAbi, meta.RootElement.GetProperty("targetAbi").GetString());
    }

    [Fact]
    public void Workflows_CoverCiE2eReleaseChecksumsAndManifestPublishing()
    {
        var root = TestPaths.RepositoryRoot();
        var ci = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        var release = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        var publish = File.ReadAllText(Path.Combine(root, ".github", "workflows", "publish-manifest.yml"));

        Assert.Contains("actions/checkout@v5", ci);
        Assert.Contains("actions/setup-dotnet@v5", ci);
        Assert.Contains("9.0.x", ci);
        Assert.Contains("10.0.x", ci);
        Assert.Contains("dotnet restore", ci);
        Assert.Contains("dotnet build --configuration Release --no-restore", ci);
        Assert.Contains("dotnet test --configuration Release --no-build", ci);
        Assert.Contains("jellyfin: [\"10.11\", \"12\"]", ci);
        Assert.Contains("./e2e/run.sh ${{ matrix.jellyfin }}", ci);
        Assert.Contains("actions/upload-artifact@", ci);
        Assert.DoesNotContain("actions/checkout@v4", ci);
        Assert.DoesNotContain("actions/setup-dotnet@v4", ci);
        Assert.Contains("actions/checkout@v5", release);
        Assert.Contains("actions/setup-dotnet@v5", release);
        Assert.Contains("9.0.x", release);
        Assert.Contains("10.0.x", release);
        Assert.Contains("<PluginVersion>", release);
        Assert.Contains("for tfm in net9.0 net10.0", release);
        Assert.Contains("dotnet publish ./Jellyfin.Plugin.LibraryInventoryExporter/Jellyfin.Plugin.LibraryInventoryExporter.csproj", release);
        Assert.Contains("python3 tools/package_plugin.py dist/publish/net9.0 dist/packages", release);
        Assert.Contains("python3 tools/package_plugin.py dist/publish/net10.0 dist/packages", release);
        Assert.Contains("dist/packages/*.zip.md5", release);
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

    [Theory]
    [InlineData("10.11", "net9.0", "jellyfin/jellyfin:10.11.")]
    [InlineData("12", "net10.0", "jellyfin/jellyfin:12.0")]
    public void E2eEnvironment_PairsEachJellyfinImageWithItsPluginBuild(string line, string targetFramework, string imagePrefix)
    {
        var env = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot(), "e2e", "env", line + ".env"));

        Assert.Contains($"JELLYFIN_LINE={line}\n", env);
        Assert.Contains($"PLUGIN_TFM={targetFramework}\n", env);
        Assert.Contains($"JELLYFIN_IMAGE={imagePrefix}", env);
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
        Assert.Contains("id=\"exportProgress\"", html);
        Assert.Contains("role=\"progressbar\"", html);
        Assert.Contains("id=\"inventoryExporterError\"", html);
        Assert.Contains("id=\"outputDirectoryWarning\"", html);
        Assert.DoesNotContain("id=\"selLibraries\"", html);
        Assert.Contains("dataType: 'json'", script);
        Assert.Contains("camelCaseKeys", script);
        Assert.Contains("reportError(", script);
        Assert.Contains("InventoryExporter/Export", script);
        Assert.Contains("InventoryExporter/Exports", script);
        Assert.Contains("InventoryExporter/Exports/Latest", script);
        Assert.Contains("InventoryExporter/Libraries", script);
        Assert.Contains("InventoryExporter/OutputDirectories", script);
        Assert.Contains("data-library-id", script);
        Assert.Contains("data-output-directory", script);
        Assert.Contains("selectedLibraryIds()", script);
        Assert.Contains("scheduleStatusRefresh", script);
        Assert.Contains("scheduleInitialStatusRefresh", script);
        Assert.Contains("setTimeout(refreshStatus, 250)", script);
        Assert.Contains("setTimeout(refreshStatus, 2000)", script);
        Assert.Contains("Export Running", script);
        Assert.Contains("normalizeStatus", script);
        Assert.Contains("ProgressPercent", script);
        Assert.Contains("renderProgress", script);
        Assert.Contains("aria-valuenow", script);
        Assert.Contains("Dashboard.toast", script);
        Assert.Contains("Library inventory export completed.", script);
        Assert.Contains("Library inventory export failed", script);
        Assert.Contains("No exports yet.", script);
        Assert.Contains("getDownloadUrl", script);
        Assert.Contains("ApiKey=", script);
        Assert.DoesNotContain("api_key=", script);
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
        Assert.DoesNotContain("\"imagePath\"", metaJson);
        Assert.Contains("\"id\": \"7184fe02-8e91-4fd2-9140-6d58d5e91f0a\"", metaJson);
    }

    [Fact]
    public void PackagedManifest_IsResolvedPerBuildAndDoesNotOverrideCatalogImageDownload()
    {
        var root = TestPaths.RepositoryRoot();
        var metaJson = File.ReadAllText(Path.Combine(root, "Jellyfin.Plugin.LibraryInventoryExporter", "meta.json"));

        Assert.Contains("\"version\": \"@PLUGIN_VERSION@\"", metaJson);
        Assert.Contains("\"targetAbi\": \"@TARGET_ABI@\"", metaJson);
        Assert.Contains("\"autoUpdate\": true", metaJson);
        Assert.DoesNotContain("\"imagePath\"", metaJson);
        Assert.DoesNotContain("\"imageUrl\"", metaJson);
    }

    private static string PluginVersion(string root)
        => XDocument.Load(Path.Combine(root, "Directory.Build.props")).Root!
            .Elements("PropertyGroup")
            .Elements("PluginVersion")
            .Single()
            .Value;
}
