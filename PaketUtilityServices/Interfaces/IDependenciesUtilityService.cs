using System.CodeDom.Compiler;
using PaketUtilityServices.Core.Models;

namespace PaketUtilityServices.Infrastructure.Utils;

public interface IDependenciesUtilityService
{
    void WritePaketDependenciesLayout(string solutionRoot, string cpmPropsFilePath);
    List<PackageInfo> ParseGlobalPackages(string filePath);
    List<PackageInfo> ParseDirectoryPackagesProps(string filePath);

    void StripVersionFromProjects(string rootPath);
}
