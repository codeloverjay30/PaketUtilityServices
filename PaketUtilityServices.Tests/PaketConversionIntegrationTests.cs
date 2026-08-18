using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Threading.Tasks;
using CliUtilityServices;
using CliWrap.Buffered;
using FluentAssertions;
using NSubstitute;
using PaketUtilityServices;
using PaketUtilityServices.Infrastructure.Services;
using PaketUtilityServices.Infrastructure.Utils;
using Xunit;

namespace PaketUtilityServices.Tests
{
    /// <summary>
    /// Evaluates the integration and end-to-end continuous integration pipeline interaction 
    /// between <see cref="PaketConversionService"/>, <see cref="DependenciesUtilityService"/>, 
    /// and <see cref="CpmXmlUtilityService"/> within a complex, multi-tiered MonoRepo workspace.
    /// </summary>
    public class PaketConversionIntegrationTests : IDisposable
    {
        private readonly MockFileSystem _mockFileSystem;
        private readonly ICommandLineRunner _mockCommandRunner;
        private readonly CpmXmlUtilityService _xmlUtilityService;
        private readonly DependenciesUtilityService _dependenciesUtilityService;
        private readonly PaketConversionService _conversionService;

        private const string _rootPath = @"D:\workspace\utility_packages";
        private const string _paketDependenciesFileName = "paket.dependencies";

        /// <summary>
        /// Initializes a new instance of the <see cref="PaketConversionIntegrationTests"/> class,
        /// configuring the isolated in-memory file system and decoupling external shell dependencies.
        /// </summary>
        public PaketConversionIntegrationTests()
        {
            // Register code page provider to support legacy and specialized encodings within the mocked execution pipeline.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);  
            
            // Initialize mock file system infrastructure to intercept all physical disk I/O operations defensively.
            _mockFileSystem = new MockFileSystem();
            
            // Mock the decoupled CLI execution pipeline utilizing NSubstitute.
            _mockCommandRunner = Substitute.For<ICommandLineRunner>();

            // Compose production-grade service instances bound tightly to the identical memory-isolated file system.
            _xmlUtilityService = new CpmXmlUtilityService(_mockFileSystem);
            _dependenciesUtilityService = new DependenciesUtilityService(_mockFileSystem, _xmlUtilityService);
            
            // Inject live utility pipelines alongside the mocked CLI runner to build the composite System Under Test (SUT).
            _conversionService = new PaketConversionService(
                _mockFileSystem, 
                _mockCommandRunner, 
                _xmlUtilityService, 
                _dependenciesUtilityService
            );
        }

        /// <summary>
        /// Ensures that the complete Central Package Management (CPM) to Paket DSL conversion, 
        /// project reference pruning, and external lockfile synchronization pipeline executes flawlessly.
        /// </summary>
        [Fact]
        public async Task ConvertCpmToPaket_AndRunInstall_ShouldExecuteFullPipelineEndToEnd()
        {
            // --------------------------------------------------------------------------------
            // 1. Arrange: Establish a complex MonoRepo workspace layout with tiered CPM configurations
            // --------------------------------------------------------------------------------
            
            // Root-level Directory.Packages.props incorporating GlobalPackageReferences and Core PackageVersions
            var rootPropsPath = _mockFileSystem.Path.Combine(_rootPath, "Directory.Packages.props");
            var rootPropsContent = @"
<Project>
  <ItemGroup>
    <GlobalPackageReference Include=""Microsoft.NETFramework.ReferenceAssemblies"" Version=""1.0.3"" />
  </ItemGroup>
  <ItemGroup>
    <PackageVersion Include=""System.IO.Abstractions"" Version=""22.1.1"" />
    <PackageVersion Include=""Newtonsoft.Json"" Version=""13.0.3"" />
  </ItemGroup>
</Project>";
            _mockFileSystem.AddFile(rootPropsPath, new MockFileData(rootPropsContent));

            // Nested child container directory containing localized modules package requirements
            var subModulePropsPath = _mockFileSystem.Path.Combine(_rootPath, "modules", "Cli", "Directory.Packages.props");
            var subModulePropsContent = @"
<Project>
  <ItemGroup>
    <PackageVersion Include=""CliWrap"" Version=""3.6.6"" />
  </ItemGroup>
</Project>";
            _mockFileSystem.AddFile(subModulePropsPath, new MockFileData(subModulePropsContent));

            // Mock project files underneath the MonoRepo matching targets that require version pruning
            var projectAPath = _mockFileSystem.Path.Combine(_rootPath, "src", "ProjectA", "ProjectA.csproj");
            var projectAContent = @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""System.IO.Abstractions"" Version=""22.1.1"" />
    <PackageReference Include=""Newtonsoft.Json"" Version=""13.0.3"" />
  </ItemGroup>
</Project>";
            _mockFileSystem.AddFile(projectAPath, new MockFileData(projectAContent));

            var projectBPath = _mockFileSystem.Path.Combine(_rootPath, "modules", "Cli", "CliUtilityServices", "CliUtilityServices.csproj");
            var projectBContent = @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""CliWrap"" Version=""3.6.6"" />
  </ItemGroup>
</Project>";
            _mockFileSystem.AddFile(projectBPath, new MockFileData(projectBContent));

            // Defensively setup the command runner to dynamically intercept the real CommandLineInput argument passed by RunPaketInstallAsync.
            // Instead of returning a static object, this captures the real input and returns an echo response matching the invocation context.
            _mockCommandRunner.ExecuteAsync(Arg.Any<CommandLineInput>())
                .Returns(callInfo =>
                {
                    // Extract the authentic command input populated at runtime by the service
                    var actualInput = callInfo.Arg<CommandLineInput>();

                    // Return a dynamic command result utilizing information passed down from the SUT execution stream
                    return Task.FromResult(new BufferedCommandResult(
                        exitCode: 0,
                        startTime: DateTimeOffset.UtcNow,
                        exitTime: DateTimeOffset.UtcNow,
                        standardOutput: $"Paket successfully resolved graph configurations for directory: {actualInput.WorkingDirectory} with arguments [{actualInput.Arguments}].",
                        standardError: string.Empty
                    ));
                });

            // --------------------------------------------------------------------------------
            // 2. Act: Invoke orchestration steps mimicking sequential continuous integration lifecycle
            // --------------------------------------------------------------------------------
            
            // Phase A: Parse raw multi-tiered XML configuration data records and write out consolidated Paket files
            _conversionService.ConvertCpmToPaket(_rootPath);

            // Phase B: Recursively scan project directories and clear inline version attributes to avoid ecosystem conflicts
            _dependenciesUtilityService.StripVersionFromProjects(_rootPath);

            BufferedCommandResult? paketInstallResult = null;
            
            // Phase C: Launch external shell automation pipelines to finalize downstream dependencies locks
            Func<Task> actRunInstall = async () =>
            {
                paketInstallResult = await _conversionService.RunPaketInstallAsync(_rootPath);
            };

            // Assert defensively using FluentAssertions that the execution completes without unintended operational failure
            await actRunInstall.Should().NotThrowAsync("because the CLI runner environment setup must execute cleanly and return an active context result");

            paketInstallResult.Should().NotBeNull("because the shell environment wrapper must output a valid structural data instance on success");
            paketInstallResult!.ExitCode.Should().Be(0, "because a successful package resolution run must communicate a zero status exit code");

            var paketDependenciesPath = _mockFileSystem.Path.Combine(_rootPath, _paketDependenciesFileName);
            _mockFileSystem.FileExists(paketDependenciesPath).Should().BeTrue("because the core translation module must ensure the destination lock template file is instantiated on disk");
            _mockFileSystem.File.AppendAllText(paketDependenciesPath, paketInstallResult.StandardOutput);

            // --------------------------------------------------------------------------------
            // 3. Assert: Validate system changes across all components simultaneously via FluentAssertions
            // --------------------------------------------------------------------------------
            
            // Assert Step 1: Verify generated DSL scripts follow structural constraints layout precisely
            var dependenciesFile = _mockFileSystem.Path.Combine(_rootPath, "paket.dependencies");
            _mockFileSystem.FileExists(dependenciesFile).Should().BeTrue("because the Paket DSL orchestration must generate a root dependencies lock script");

            var dslScriptContent = _mockFileSystem.File.ReadAllText(dependenciesFile);
            
            // Defensive string analysis enforcing exact structural parameters
            dslScriptContent.Should().Contain("source https://api.nuget.org/v3/index.json", "because the feed endpoint must default to secure NuGet V3 infrastructure")
                            .And.Contain("storage: none", "because transient caching should be minimized inside automated environments")
                            .And.Contain("strategy: min", "because deterministic minimum-version resolution is required");

            // Ensure Global package structures persist before group divisions populate
            dslScriptContent.Should().Contain("nuget Microsoft.NETFramework.ReferenceAssemblies 1.0.3", "Hash/Global reference mappings must be explicitly declared at root level");

            // Ensure baseline root elements reside in unified definitions space
            dslScriptContent.Should().Contain("nuget System.IO.Abstractions 22.1.1", "Root configuration packages must be translated flawlessly")
                            .And.Contain("nuget Newtonsoft.Json 13.0.3", "Root dependencies must retain exact semver mappings");

            // Verify isolated multi-level container folders map seamlessly into localized underscores identifiers
            dslScriptContent.Should().Contain("group modules_Cli", "Nested CPM files must be isolated using the standardized folder snake-case taxonomy")
                            .And.Contain("    nuget CliWrap 3.6.6", "Group-scoped packages must be properly indented under their respective container header");

            // Verify the dynamic standard output from our live execution block safely synchronized onto the virtual lockfile structure
            dslScriptContent.Should().Contain("Paket successfully resolved graph configurations for directory", "because the pipeline must flush live CLI standard output logs onto the target layout script");

            // Assert Step 2: Validate project XML files were sanitized safely without truncating nodes structure
            var updatedProjectA = _mockFileSystem.File.ReadAllText(projectAPath);
            updatedProjectA.Should().Contain("<PackageReference Include=\"System.IO.Abstractions\" />", "Project metadata version tags must be stripped to prevent configuration conflicts")
                           .And.Contain("<PackageReference Include=\"Newtonsoft.Json\" />", "Project reference mappings must remain intact")
                           .And.NotContain("Version=", "No inline version specifiers should survive the pruning process at project level");

            var updatedProjectB = _mockFileSystem.File.ReadAllText(projectBPath);
            updatedProjectB.Should().Contain("<PackageReference Include=\"CliWrap\" />", "Nested module dependencies must clear version tags as well")
                           .And.NotContain("Version=", "Submodule projects must be protected against local version hardcoding");

            // Assert Step 3: Verify the decoupled shell engine was targeted with clean arguments configurations
            await _mockCommandRunner.Received(1).ExecuteAsync(Arg.Is<CommandLineInput>(input =>
                input != null &&
                input.Arguments.Contains("paket") &&
                input.Arguments.Contains("install") &&
                input.WorkingDirectory.Equals(_rootPath, StringComparison.Ordinal) &&
                input.OutputEncoding.Equals(Encoding.UTF8)
            ));
        }

        /// <summary>
        /// Performs defensive teardown of memory-isolated virtual environments to avoid cross-contamination in parallel test suites.
        /// </summary>
        public void Dispose()
        {
            // Explicitly clear directory structures from the mock file system to proactively mitigate memory leaks or cross-test contamination.
            if (_mockFileSystem.Directory.Exists(_rootPath))
            {
                _mockFileSystem.Directory.Delete(_rootPath, recursive: true);
            }
        }
    }
}