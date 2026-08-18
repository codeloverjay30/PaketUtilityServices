using System;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Threading.Tasks;
using CliUtilityServices;
using CliWrap.Buffered;
using FluentAssertions;
using NSubstitute;
using PaketUtilityServices;
using PaketUtilityServices.Infrastructure.Utils;
using Xunit;

namespace PaketUtilityServices.Tests
{
    public class PaketConversionServiceExecutionTests
    {
        private readonly MockFileSystem _fileSystem;
        private readonly ICommandLineRunner _mockCommandRunner;
        private readonly PaketConversionService _service;
        private const string RootPath = @"D:\workspace\utility_packages";

        public PaketConversionServiceExecutionTests()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _fileSystem = new MockFileSystem();
            _mockCommandRunner = Substitute.For<ICommandLineRunner>();
            
            // 注入 MockFileSystem 與 MockCommandRunner
            _service = new PaketConversionService(_fileSystem, _mockCommandRunner);
        }

        [Fact]
        public async Task RunPaketInstallAsync_WhenCommandSucceeds_ShouldExecuteWithoutThrowing()
        {
            // Arrange: 模擬外部指令執行成功的結果 (ExitCode = 0)
            var expectedResult = new BufferedCommandResult(
                exitCode: 0,
                startTime: DateTimeOffset.UtcNow,
                exitTime: DateTimeOffset.UtcNow,
                standardOutput: "paket install completed successfully.",
                standardError: string.Empty
            );

            // 設定 Mock 當接收到任何 CommandLineInput 時，回傳成功結果
            _mockCommandRunner.ExecuteAsync(Arg.Any<CommandLineInput>())
                .Returns(Task.FromResult(expectedResult));

            // Act & Assert: 驗證執行成功時不會拋出任何例外
            Func<Task> act = async () => await _service.RunPaketInstallAsync(RootPath);
            await act.Should().NotThrowAsync();

            // 驗證 ICommandLineRunner 確實有被呼叫過一次
            await _mockCommandRunner.Received(1).ExecuteAsync(Arg.Is<CommandLineInput>(input => 
                input.Arguments.Contains("paket") && 
                input.Arguments.Contains("install") && 
                input.WorkingDirectory == RootPath
            ));
        }

        [Fact]
        public async Task RunPaketInstallAsync_WhenCommandFails_ShouldThrowExceptionWithErrorMessage()
        {
            // Arrange: 模擬外部指令執行失敗的結果 (ExitCode != 0)
            string expectedErrorMessage = "Error: Cannot resolve package dependency 'Serilog'.";
            var failedResult = new BufferedCommandResult(
                exitCode: 1,
                startTime: DateTimeOffset.UtcNow,
                exitTime: DateTimeOffset.UtcNow,
                standardOutput: string.Empty,
                standardError: expectedErrorMessage
            );

            _mockCommandRunner.ExecuteAsync(Arg.Any<CommandLineInput>())
                .Returns(Task.FromResult(failedResult));

            // Act & Assert: 驗證執行失敗時會拋出 Exception，且訊息包含錯誤內容
            Func<Task> act = async () => await _service.RunPaketInstallAsync(RootPath);
            
            var exception = await act.Should().ThrowAsync<Exception>();
            exception.And.Message.Should().Contain(expectedErrorMessage);
            exception.And.Message.Should().Contain("Failure to execute");
        }
    }
}