namespace DEGMA.Models
{
    public class UpdateSettings
    {
        // GitHub repository whose releases are installed, as "owner/name".
        public string Repository { get; set; } = "ConJaBst/DEGMA";

        // GitHub API base address. Only changed for testing against a local stand-in.
        public string ApiBaseUrl { get; set; } = "https://api.github.com";

        // The release asset to install: the one whose name ends with this.
        public string AssetSuffix { get; set; } = "-win-x64.zip";
    }
}
