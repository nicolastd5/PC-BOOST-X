// The real services are linked into this test assembly. Only Windows registry,
// application data location and the WPF presentation model are substituted.
namespace Microsoft.Win32
{
    public static class Registry
    {
        public static RegistryKey CurrentUser { get; } = new("HKEY_CURRENT_USER");
        public static RegistryKey LocalMachine { get; } = new("HKEY_LOCAL_MACHINE");
    }

    public sealed class RegistryKey(string name) : IDisposable
    {
        private static readonly Dictionary<string, Dictionary<string, (object Value, RegistryValueKind Kind)>> Keys =
            new(StringComparer.OrdinalIgnoreCase);
        public static Action<string, string>? BeforeWrite { get; set; }
        public static string? FailWritePath { get; set; }
        public static int WriteCount { get; private set; }
        public string Name { get; } = name;

        public static void Reset()
        {
            Keys.Clear();
            BeforeWrite = null;
            FailWritePath = null;
            WriteCount = 0;
        }

        public RegistryKey? OpenSubKey(string path, bool writable = false)
        {
            var full = Name + "\\" + path;
            return Keys.ContainsKey(full) ? new RegistryKey(full) : null;
        }

        public RegistryKey CreateSubKey(string path, bool writable = true)
        {
            var full = Name + "\\" + path;
            Keys.TryAdd(full, new(StringComparer.OrdinalIgnoreCase));
            return new RegistryKey(full);
        }

        public object? GetValue(string name, object? defaultValue = null,
            RegistryValueOptions options = RegistryValueOptions.None) =>
            Keys.TryGetValue(Name, out var values) && values.TryGetValue(name, out var entry)
                ? entry.Value : defaultValue;

        public RegistryValueKind GetValueKind(string name) => Keys[Name][name].Kind;
        public string[] GetValueNames() => Keys.TryGetValue(Name, out var values) ? values.Keys.ToArray() : [];
        public string[] GetSubKeyNames() => Keys.Keys.Where(k => k.StartsWith(Name + "\\", StringComparison.OrdinalIgnoreCase))
            .Select(k => k[(Name.Length + 1)..].Split('\\')[0]).Distinct().ToArray();

        public void SetValue(string name, object value, RegistryValueKind kind)
        {
            Write(name);
            Keys[Name][name] = (value, kind);
        }

        public void DeleteValue(string name, bool throwOnMissingValue = true)
        {
            Write(name);
            if (!Keys[Name].Remove(name) && throwOnMissingValue)
                throw new ArgumentException("Registry value is missing.");
        }

        private void Write(string name)
        {
            BeforeWrite?.Invoke(Name, name);
            if (FailWritePath is { } path && Name.EndsWith(path, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Registry access denied by the test fixture.");
            WriteCount++;
        }

        /// <summary>Todos os valores existentes, em ordem estável, para comparar antes e depois.</summary>
        public static string Dump() => string.Join(Environment.NewLine, Keys
            .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(k => k.Value.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
                .Select(v => $"{k.Key}/{v.Key}={v.Value.Kind}:{v.Value.Value}")));

        public void Dispose() { }
    }
}

