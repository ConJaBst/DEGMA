using System.Diagnostics;
using Microsoft.Win32;

namespace DEGMA.Services
{
    public class PdfGenerationException : Exception
    {
        public PdfGenerationException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Turns a self-contained HTML page into a PDF by printing it with Microsoft Edge in
    /// headless mode. Edge ships with Windows 10/11 and works offline, and it renders the
    /// page with the same engine as the browser that shows the Preview, so the PDF matches it.
    /// </summary>
    public class PdfService
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

        // One Edge at a time — each print starts a full browser
        private static readonly SemaphoreSlim Gate = new(1, 1);

        private readonly ILogger<PdfService> _logger;
        private readonly string? _configuredEdgePath;

        public PdfService(ILogger<PdfService> logger, IConfiguration configuration)
        {
            _logger = logger;
            // Optional override in appsettings.json: "Pdf": { "EdgePath": "C:\\...\\msedge.exe" }
            _configuredEdgePath = configuration["Pdf:EdgePath"];
        }

        public async Task<byte[]> HtmlToPdfAsync(string html, CancellationToken cancellationToken = default)
        {
            var edge = FindEdge()
                ?? throw new PdfGenerationException(
                    "Microsoft Edge was not found on this PC, so the PDF could not be created. " +
                    "Use Preview Report and print it instead, or set Pdf:EdgePath in appsettings.json.");

            var workDir = Path.Combine(Path.GetTempPath(), "DEGMA-pdf", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
            var htmlPath = Path.Combine(workDir, "report.html");
            var pdfPath = Path.Combine(workDir, "report.pdf");

            await Gate.WaitAsync(cancellationToken);
            try
            {
                await File.WriteAllTextAsync(htmlPath, html, cancellationToken);

                var psi = new ProcessStartInfo(edge)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var arg in new[]
                {
                    "--headless",
                    "--disable-gpu",
                    "--no-pdf-header-footer",
                    "--print-to-pdf-no-header",            // older Edge versions
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--disable-extensions",
                    "--disable-background-networking",
                    "--disable-sync",
                    $"--user-data-dir={Path.Combine(workDir, "profile")}", // never touch the user's Edge profile
                    $"--print-to-pdf={pdfPath}",
                    new Uri(htmlPath).AbsoluteUri,
                })
                {
                    psi.ArgumentList.Add(arg);
                }

                using var process = Process.Start(psi)
                    ?? throw new PdfGenerationException("Microsoft Edge could not be started to create the PDF.");

                // Drain output so Edge never blocks on a full pipe
                var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
                var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(Timeout);
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    TryKill(process);
                    if (cancellationToken.IsCancellationRequested) throw;
                    throw new PdfGenerationException(
                        $"Creating the PDF took longer than {Timeout.TotalSeconds:0} seconds and was stopped. Please try again.");
                }

                var output = (await stdout + await stderr).Trim();
                var pdf = File.Exists(pdfPath) ? await File.ReadAllBytesAsync(pdfPath, cancellationToken) : [];
                if (pdf.Length < 5 || pdf[0] != '%' || pdf[1] != 'P' || pdf[2] != 'D' || pdf[3] != 'F')
                {
                    _logger.LogError("Edge exited with code {ExitCode} without writing a PDF. Output: {Output}",
                        process.ExitCode, output);
                    throw new PdfGenerationException(
                        $"Microsoft Edge did not produce a PDF (exit code {process.ExitCode}). Please try again.");
                }

                return pdf;
            }
            finally
            {
                Gate.Release();
                await DeleteWorkDirAsync(workDir);
            }
        }

        private string? FindEdge()
        {
            if (!string.IsNullOrWhiteSpace(_configuredEdgePath))
                return File.Exists(_configuredEdgePath) ? _configuredEdgePath : null;

            var candidates = new List<string?>();
            if (OperatingSystem.IsWindows())
            {
                candidates.Add(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe", null, null) as string);
                candidates.Add(Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe", null, null) as string);
            }
            foreach (var root in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            })
            {
                if (!string.IsNullOrEmpty(root))
                    candidates.Add(Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe"));
            }

            return candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
        }

        private void TryKill(Process process)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not stop Edge after PDF timeout."); }
        }

        // Edge's helper processes can hold the profile folder for a moment after exit
        private async Task DeleteWorkDirAsync(string dir)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                    return;
                }
                catch (IOException) { await Task.Delay(500); }
                catch (UnauthorizedAccessException) { await Task.Delay(500); }
            }
            _logger.LogWarning("Could not delete temporary PDF folder {Dir}", dir);
        }
    }
}
