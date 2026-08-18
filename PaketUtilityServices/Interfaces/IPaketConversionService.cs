using CliWrap.Buffered;
using CommonModels;

namespace PaketUtilityServices.Infrastructure.Utils;

public interface IPaketConversionService
{
    StatusJsonModels ConvertCpmToPaket(string rootPath);

    Task<BufferedCommandResult> RunPaketInstallAsync(string rootPath);
}
