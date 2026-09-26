namespace SvMcp.Bridge;

public static class BridgePathResolver
{
    public static string Resolve(string fallbackRoot)
    {
        const string teklaBase = @"C:\TeklaStructures";
        if (Directory.Exists(teklaBase))
        {
            var extensionsBridge = Directory.GetDirectories(teklaBase)
                .Where(directory => Version.TryParse(Path.GetFileName(directory), out var version) && version.Major >= 2025)
                .OrderByDescending(directory => Version.Parse(Path.GetFileName(directory)))
                .Select(directory => Path.Combine(directory, "Environments", "common", "extensions", "svMCP", "TeklaBridge.exe"))
                .FirstOrDefault(File.Exists);
            if (extensionsBridge != null)
                return extensionsBridge;
        }

        return Path.Combine(fallbackRoot, "bridge", "TeklaBridge.exe");
    }
}
