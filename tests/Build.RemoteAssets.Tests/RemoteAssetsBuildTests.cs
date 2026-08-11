using System.Diagnostics;
using System.Reflection;
using System.Text;
using Shouldly;
using Xunit;

namespace Kebechet.Build.RemoteAssets.Tests;

public sealed class RemoteAssetsBuildTests : IDisposable
{
	private static readonly string _targetsPath = Assembly.GetExecutingAssembly()
		.GetCustomAttributes<AssemblyMetadataAttribute>()
		.First(x => x.Key == "RemoteAssetsTargetsPath")
		.Value!;

	private static readonly byte[] _heroPayload = Encoding.UTF8.GetBytes("hero-v1-payload");
	private static readonly byte[] _otherPayload = Encoding.UTF8.GetBytes("a-completely-different-payload");

	/// <summary>SHA256 of <c>_heroPayload</c>, computed rather than pasted so the fixtures cannot drift.</summary>
	private static readonly string _heroSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_heroPayload));

	private readonly string _projectDirectory;
	private readonly LocalHttpServer _server;

	public RemoteAssetsBuildTests()
	{
		_projectDirectory = Path.Combine(Path.GetTempPath(), $"remoteassets-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_projectDirectory);

		_server = new LocalHttpServer(new Dictionary<string, byte[]>
		{
			["v1/hero.webp"] = _heroPayload,
			["v2/hero.webp"] = _otherPayload,
			["v1/extra.webp"] = _otherPayload,
		});
	}

	[Fact]
	public void Build_AssetDeclared_DownloadsFileToDestination()
	{
		// Arrange
		WriteProject($"""<RemoteAsset Include="{_server.BaseUrl}/v1/hero.webp" Path="assets" />""");

		// Act
		var result = Build();

		// Assert
		result.IsSuccess.ShouldBeTrue(result.Output);
		File.ReadAllBytes(Path.Combine(_projectDirectory, "assets", "hero.webp")).ShouldBe(_heroPayload);
	}

	[Fact]
	public void Build_RunTwice_DoesNotDownloadAgain()
	{
		// Arrange
		WriteProject($"""<RemoteAsset Include="{_server.BaseUrl}/v1/hero.webp" Path="assets" />""");
		Build().IsSuccess.ShouldBeTrue();

		// Act
		var result = Build();

		// Assert
		result.IsSuccess.ShouldBeTrue(result.Output);
		_server.RequestCountFor("v1/hero.webp").ShouldBe(1);
	}

	[Fact]
	public void Build_UrlChanged_ReplacesCachedFile()
	{
		// Arrange
		WriteProject($"""<RemoteAsset Include="{_server.BaseUrl}/v1/hero.webp" Path="assets" />""");
		Build().IsSuccess.ShouldBeTrue();

		// Act - same destination file name, different URL
		WriteProject($"""<RemoteAsset Include="{_server.BaseUrl}/v2/hero.webp" Path="assets" />""");
		var result = Build();

		// Assert
		result.IsSuccess.ShouldBeTrue(result.Output);
		File.ReadAllBytes(Path.Combine(_projectDirectory, "assets", "hero.webp")).ShouldBe(_otherPayload);
	}

	[Fact]
	public void Build_Sha256Matches_Succeeds()
	{
		// Arrange
		WriteProject($"""<RemoteAsset Include="{_server.BaseUrl}/v1/hero.webp" Path="assets" Sha256="{_heroSha256}" />""");

		// Act
		var result = Build();

		// Assert
		result.IsSuccess.ShouldBeTrue(result.Output);
	}

	[Fact]
	public void Build_Sha256Mismatch_FailsBuildAndDiscardsFile()
	{
		// Arrange
		WriteProject($"""<RemoteAsset Include="{_server.BaseUrl}/v1/hero.webp" Path="assets" Sha256="0000000000000000000000000000000000000000000000000000000000000000" />""");

		// Act
		var result = Build();

		// Assert
		result.IsSuccess.ShouldBeFalse();
		result.Output.ShouldContain("failed hash verification");
		File.Exists(Path.Combine(_projectDirectory, "assets", "hero.webp")).ShouldBeFalse();
	}

	[Fact]
	public void Build_AssetRemovedFromList_DeletesFetchedFileButKeepsCommittedNeighbour()
	{
		// Arrange
		WriteProject($"""
			<RemoteAsset Include="{_server.BaseUrl}/v1/hero.webp" Path="assets" />
			    <RemoteAsset Include="{_server.BaseUrl}/v1/extra.webp" Path="assets" />
			""");
		Build().IsSuccess.ShouldBeTrue();

		var committedNeighbour = Path.Combine(_projectDirectory, "assets", "committed.webp");
		File.WriteAllText(committedNeighbour, "committed");

		// Act
		WriteProject($"""<RemoteAsset Include="{_server.BaseUrl}/v1/hero.webp" Path="assets" />""");
		var result = Build();

		// Assert
		result.IsSuccess.ShouldBeTrue(result.Output);
		File.Exists(Path.Combine(_projectDirectory, "assets", "extra.webp")).ShouldBeFalse();
		File.Exists(Path.Combine(_projectDirectory, "assets", "hero.webp")).ShouldBeTrue();
		File.Exists(committedNeighbour).ShouldBeTrue();
	}

	[Fact]
	public void Build_UrlWithQueryStringInInclude_FailsWithGuidance()
	{
		// Arrange - '?' is an MSBuild wildcard, so this form must be rejected rather than silently dropped
		WriteProject($"""<RemoteAsset Include="hero.webp" Url="{_server.BaseUrl}/v1/hero.webp?sig=abc" Path="assets" Name="hero.webp?sig=abc" />""");

		// Act
		var result = Build();

		// Assert
		result.IsSuccess.ShouldBeFalse();
		result.Output.ShouldContain("query string");
	}

	[Fact]
	public void Build_UrlPassedAsMetadata_UsesIncludeAsFileName()
	{
		// Arrange
		WriteProject($"""<RemoteAsset Include="renamed.webp" Url="{_server.BaseUrl}/v1/hero.webp" Path="assets" />""");

		// Act
		var result = Build();

		// Assert
		result.IsSuccess.ShouldBeTrue(result.Output);
		File.ReadAllBytes(Path.Combine(_projectDirectory, "assets", "renamed.webp")).ShouldBe(_heroPayload);
	}

	[Fact]
	public void Build_RazorProject_RegistersAssetAsStaticWebAsset()
	{
		// Arrange
		WriteProject($"""<RemoteAsset Include="{_server.BaseUrl}/v1/hero.webp" Path="wwwroot/img" />""", sdk: "Microsoft.NET.Sdk.Razor");

		// Act
		var result = Build();

		// Assert
		result.IsSuccess.ShouldBeTrue(result.Output);

		var manifest = Directory.GetFiles(_projectDirectory, "staticwebassets.build.json", SearchOption.AllDirectories).First();
		File.ReadAllText(manifest).ShouldContain("hero.webp");
	}

	[Fact]
	public void Build_ItemTypeNone_LeavesAssetOutOfTheOutputDirectory()
	{
		// Arrange
		WriteProject($"""<RemoteAsset Include="{_server.BaseUrl}/v1/hero.webp" Path="assets" ItemType="None" />""");

		// Act
		var result = Build();

		// Assert
		result.IsSuccess.ShouldBeTrue(result.Output);
		File.Exists(Path.Combine(_projectDirectory, "assets", "hero.webp")).ShouldBeTrue();
		Directory.GetFiles(_projectDirectory, "hero.webp", SearchOption.AllDirectories)
			.ShouldNotContain(x => x.Contains($"bin{Path.DirectorySeparatorChar}"));
	}

	private void WriteProject(string remoteAssetItems, string sdk = "Microsoft.NET.Sdk")
	{
		var content = $"""
			<Project Sdk="{sdk}">

			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
			  </PropertyGroup>

			  <ItemGroup>
			    {remoteAssetItems}
			  </ItemGroup>

			  <Import Project="{_targetsPath}" />

			</Project>
			""";

		File.WriteAllText(Path.Combine(_projectDirectory, "Fixture.csproj"), content);
	}

	private BuildResult Build()
	{
		var startInfo = new ProcessStartInfo("dotnet", "build -v:m --nologo")
		{
			WorkingDirectory = _projectDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};

		using var process = Process.Start(startInfo)!;
		var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
		process.WaitForExit();

		return new BuildResult(process.ExitCode == 0, output);
	}

	public void Dispose()
	{
		_server.Dispose();

		try
		{
			Directory.Delete(_projectDirectory, recursive: true);
		}
		catch (IOException)
		{
			// A build node can still hold a handle under the temp project; the OS reclaims it later.
		}
	}

	private sealed record BuildResult(bool IsSuccess, string Output);
}
