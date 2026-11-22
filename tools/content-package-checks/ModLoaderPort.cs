// The offline scanner gets its assembly names from the supplied Managed directory.
public static class ModLoader
{
    private static HashSet<string> gameAssemblyNames;

    internal static void UseManagedDirectory(string managedDirectory)
    {
        if (!File.Exists(Path.Combine(managedDirectory, "Assembly-CSharp.dll")))
            throw new ArgumentException("Managed directory does not contain Assembly-CSharp.dll");
        gameAssemblyNames = Directory.EnumerateFiles(managedDirectory, "*.dll", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsGameAssemblyName(string assemblyName) =>
        gameAssemblyNames?.Contains(assemblyName) == true;
}

public static class LocHelper
{
    public static string GetLocalizedUIOrDefault(string key, string fallback) => fallback;
}
