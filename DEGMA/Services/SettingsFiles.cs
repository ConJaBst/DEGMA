using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DEGMA.Services
{
    // Settings come from two files next to the app:
    //   appsettings.Defaults.json  shipped with every release and replaced by updates
    //   appsettings.json           the clinic's own settings (SMTP password, batch number, ...);
    //                              never shipped, so unzipping an update can't overwrite it
    // appsettings.json overrides the defaults key by key.
    public static class SettingsFiles
    {
        public const string DefaultsFile = "appsettings.Defaults.json";
        public const string LocalFile = "appsettings.json";

        // The sections a clinic normally edits; copied into a new appsettings.json.
        private static readonly string[] StarterSections = { "EmailSettings", "ClinicSettings" };

        private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

        public static void AddDefaults(WebApplicationBuilder builder)
        {
            var contentRoot = builder.Environment.ContentRootPath;
            EnsureLocalFile(contentRoot);

            // Lowest priority, so appsettings.json, appsettings.{Environment}.json and
            // environment variables all override the shipped defaults.
            builder.Configuration.Sources.Insert(0, new JsonConfigurationSource
            {
                FileProvider = new PhysicalFileProvider(contentRoot),
                Path = DefaultsFile,
                Optional = true,
                ReloadOnChange = true
            });

            // appsettings.json may have just been created; make sure it is loaded.
            ((IConfigurationRoot)builder.Configuration).Reload();
        }

        // A new install has no appsettings.json yet: start one from the defaults.
        public static void EnsureLocalFile(string contentRoot)
        {
            var localPath = Path.Combine(contentRoot, LocalFile);
            if (File.Exists(localPath))
                return;

            var starter = new JsonObject();
            var defaultsPath = Path.Combine(contentRoot, DefaultsFile);
            if (File.Exists(defaultsPath) && JsonNode.Parse(File.ReadAllText(defaultsPath)) is JsonObject defaults)
            {
                foreach (var section in StarterSections)
                {
                    if (defaults[section] is JsonNode node)
                        starter[section] = node.DeepClone();
                }
            }

            File.WriteAllText(localPath, starter.ToJsonString(Indented));
        }

        // Saves one value into the clinic's appsettings.json, keeping everything else in it.
        public static void SetLocalValue(string contentRoot, string section, string key, string value)
        {
            var localPath = Path.Combine(contentRoot, LocalFile);
            var root = File.Exists(localPath) && JsonNode.Parse(File.ReadAllText(localPath)) is JsonObject existing
                ? existing
                : new JsonObject();

            if (root[section] is not JsonObject sectionNode)
            {
                sectionNode = new JsonObject();
                root[section] = sectionNode;
            }
            sectionNode[key] = value;

            File.WriteAllText(localPath, root.ToJsonString(Indented));
        }
    }
}
