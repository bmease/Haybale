using NuGet.Versioning;
using System.Diagnostics;
using System.Text.Json;


namespace updater
{
    class Program
    {

        private const string GitHubOwner = "bmease";
        private const string GitHubRepo = "CheCommand";
        private const string AppName = "CheCommand";

        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppName);
        private static readonly string VersionFilePath = Path.Combine(AppDataDir, "version.txt");
        private static readonly string InstallPathFile = Path.Combine(AppDataDir, "install_path.txt");


        static async Task<int> Main(string[] argsA)
        {
            // Console.WriteLine(AppDataDir);

            // Read local version (or default to 0.0.0 if missing)
            NuGetVersion localVersion;
            if (!File.Exists(VersionFilePath))
            {
                Console.WriteLine("Local version file not found; will install latest release.");
                localVersion = new NuGetVersion(0, 0, 0);
            }
            else
            {
                var localVersionText = File.ReadAllText(VersionFilePath).Trim();
                localVersion = NuGetVersion.Parse(localVersionText);
                Console.WriteLine($"Current version: {localVersion}");
            }

            // Ensure AppData directory exists
            Directory.CreateDirectory(AppDataDir);

            // Read install path
            if (!File.Exists(InstallPathFile))
            {
                Console.WriteLine("Install path file not found.");
                return 1;
            }
            var installDir = File.ReadAllText(InstallPathFile).Trim();
            if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
            {
                Console.WriteLine("Invalid install directory specified in install_path.txt.");
                return 1;
            }

            // Fetch latest GitHub release
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppName}-updater");
            var apiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
            var response = await http.GetAsync(apiUrl);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            var root = doc.RootElement;
            var tagName = root.GetProperty("tag_name").GetString();
            if (string.IsNullOrEmpty(tagName)) throw new Exception("tag_name empty");

            var latestVersion = NuGetVersion.Parse(tagName.TrimStart('v', 'V'));
            Console.WriteLine($"Latest version: {latestVersion}");

            // Compare versions
            if (latestVersion <= localVersion)
            {
                Console.WriteLine("Already up-to-date.");
                return 0;
            }

            // Download .exe asset
            var assets = root.GetProperty("assets").EnumerateArray();
            string downloadUrl = null;
            string fileName = null;
            foreach (var asset in assets)
            {
                var name = asset.GetProperty("name").GetString();
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    fileName = name;
                    break;
                }
            }
            if (downloadUrl == null)
                throw new Exception("No .exe asset found in latest release.");

            Console.WriteLine($"Downloading {fileName}...");
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);
            // Console.WriteLine($"Downloading to: {tempPath}");
            await DownloadFileAsync(http, downloadUrl, tempPath);

            // Run installer silently
            Console.WriteLine("Running installer...");
            var installerArgs = $"/VERYSILENT /DIR=\"{installDir}\"";
            // var installerArgs = "";
            var psi = new ProcessStartInfo(tempPath, installerArgs)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var installProc = Process.Start(psi);
            installProc.WaitForExit();
            if (installProc.ExitCode != 0)
                throw new Exception($"Installer exited with code {installProc.ExitCode}");

            // Update local version file
            File.WriteAllText(VersionFilePath, latestVersion.ToString());
            Console.WriteLine("Update complete.");


            return 0;
        }

        private static async Task DownloadFileAsync(HttpClient client, string url, string destination)
        {
            using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using var fs = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await resp.Content.CopyToAsync(fs);
        }
    }
}