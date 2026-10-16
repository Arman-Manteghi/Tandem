using Tandem.Core.Interop;

namespace Tandem.Core.Claude;

/// <summary>Installed-package lookups through the documented app-model APIs (no WinRT needed).</summary>
internal static unsafe class PackageQueries
{
    public static IReadOnlyList<string> FindPackagesByFamily(string familyName)
    {
        uint count = 0;
        uint bufferLength = 0;
        var rc = Kernel32.GetPackagesByPackageFamily(familyName, ref count, null, ref bufferLength, null);
        if (rc != Kernel32.ERROR_INSUFFICIENT_BUFFER || count == 0)
        {
            return [];
        }

        var names = new IntPtr[count];
        var buffer = new char[bufferLength];
        fixed (IntPtr* pNames = names)
        fixed (char* pBuffer = buffer)
        {
            rc = Kernel32.GetPackagesByPackageFamily(familyName, ref count, pNames, ref bufferLength, pBuffer);
            if (rc != Kernel32.ERROR_SUCCESS)
            {
                return [];
            }

            var result = new List<string>((int)count);
            for (var i = 0; i < count; i++)
            {
                result.Add(new string((char*)names[i]));
            }

            return result;
        }
    }

    public static string? GetPackagePath(string packageFullName)
    {
        uint length = 0;
        var rc = Kernel32.GetPackagePathByFullName(packageFullName, ref length, null);
        if (rc != Kernel32.ERROR_INSUFFICIENT_BUFFER || length == 0)
        {
            return null;
        }

        var buffer = new char[length];
        fixed (char* p = buffer)
        {
            return Kernel32.GetPackagePathByFullName(packageFullName, ref length, p) == Kernel32.ERROR_SUCCESS
                ? new string(p)
                : null;
        }
    }

    /// <summary>"Claude_2.37337.1.0_x64__pzs8sxrjxfjjc" → 2.37337.1.0.</summary>
    public static Version? ParseVersion(string packageFullName)
    {
        var parts = packageFullName.Split('_');
        return parts.Length >= 2 && Version.TryParse(parts[1], out var version) ? version : null;
    }
}
