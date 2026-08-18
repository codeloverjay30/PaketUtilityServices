using PaketUtilityServices.Core.Models;

namespace PaketUtilityServices.Infrastructure.Services;
public interface ICpmXmlUtilityService
{
    List<PackageInfo> ParseCpmElements(
        string filePath,
        string tagName
    );

    bool RemovePackageReferencesVersion(
        string projectFilePath
    );
}
