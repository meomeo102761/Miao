using System.Diagnostics;
using System.Net.Http;

namespace Miao.Core.Services
{
    /// <summary>
    /// Starts the local CTranslate2 Flask server only when translation is actually needed.
    /// The server is shared by all CTranslate2Provider instances.
    /// </summary>
    public static class CTranslate2ServerService
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        private static readonly SemaphoreSlim StartLock = new(1, 1);
        private static Process? _serverProcess;

        public static async Task<bool> EnsureRunningAsync(CancellationToken cancellationToken = default)
        {
            if (await IsRunningAsync(cancellationToken))
                return true;

            await StartLock.WaitAsync(cancellationToken);
            try
            {
                // Another translation request may have started it while we were waiting.
                if (await IsRunningAsync(cancellationToken))
                    return true;

                var serverDirectory = FindServerDirectory();
                if (serverDirectory == null)
                    return false;

                var appFile = Path.Combine(serverDirectory, "app.py");
                if (!File.Exists(appFile))
                    return false;

                if (_serverProcess is { HasExited: false })
                    _serverProcess = null;

                var startInfo = new ProcessStartInfo
                {
                    FileName = FindPythonCommand(),
                    Arguments = $"-u \"{appFile}\"",
                    WorkingDirectory = serverDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                try
                {
                    _serverProcess = Process.Start(startInfo);
                }
                catch
                {
                    _serverProcess = null;
                    return false;
                }

                if (_serverProcess == null)
                    return false;

                // The model is loaded during server startup, so give it enough time.
                for (var i = 0; i < 120; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (_serverProcess.HasExited)
                    {
                        _serverProcess.Dispose();
                        _serverProcess = null;
                        return false;
                    }

                    if (await IsRunningAsync(cancellationToken))
                        return true;

                    await Task.Delay(500, cancellationToken);
                }

                return false;
            }
            finally
            {
                StartLock.Release();
            }
        }

        public static void Stop()
        {
            try
            {
                if (_serverProcess is { HasExited: false })
                {
                    _serverProcess.Kill(entireProcessTree: true);
                    _serverProcess.WaitForExit(2000);
                }
            }
            catch
            {
                // Do not let server cleanup prevent Miao from closing.
            }
            finally
            {
                _serverProcess?.Dispose();
                _serverProcess = null;
            }
        }

        private static async Task<bool> IsRunningAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var response = await Http.GetAsync(
                    "http://127.0.0.1:5001/health",
                    cancellationToken);

                // 200 = current server with /health.
                // 404/405 = an older Miao TranslateServer is already listening on 5001.
                return response.IsSuccessStatusCode ||
                       response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                       response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed;
            }
            catch
            {
                return false;
            }
        }

        private static string? FindServerDirectory()
        {
            var candidates = new List<string>();

            // Development: E:\Miao\Miao.Wpf\bin\Debug\net8.0-windows\
            var current = AppContext.BaseDirectory;
            var directory = new DirectoryInfo(current);
            for (var i = 0; i < 6 && directory != null; i++)
            {
                candidates.Add(Path.Combine(directory.FullName, "TranslateServer"));
                directory = directory.Parent;
            }

            // Running from the repository/project working directory.
            candidates.Add(Path.GetFullPath(
                Path.Combine(Environment.CurrentDirectory, "TranslateServer")));
            candidates.Add(Path.GetFullPath(
                Path.Combine(Environment.CurrentDirectory, "..", "TranslateServer")));

            return candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(path => File.Exists(Path.Combine(path, "app.py")));
        }

        private static string FindPythonCommand()
        {
            // The project currently uses the normal Windows Python installation.
            return "python";
        }
    }
}
