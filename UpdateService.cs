using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace CRT
{
    // ###########################################################################################
    // Handles checking for, downloading, and applying application updates via Velopack.
    // Uses GitHub Releases as the update source.
    // ###########################################################################################
    public static class UpdateService
    {
        private const string GitHubOwner = "HovKlan-DH";
        private const string GitHubRepo = "CRT-PoC";

        private static UpdateManager? _manager;
        private static UpdateInfo? _pendingUpdate;

        // ###########################################################################################
        // Checks GitHub Releases for a newer version.
        // Returns true if an update is available, false if up to date, null if the check failed.
        // ###########################################################################################
        public static async Task<bool?> CheckForUpdateAsync()
        {
            try
            {
                _manager = new UpdateManager(new GithubSource($"https://github.com/{GitHubOwner}/{GitHubRepo}", null, false));
                _pendingUpdate = await _manager.CheckForUpdatesAsync();
                return _pendingUpdate != null;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Update check failed - [{ex.Message}]");
                return null;
            }
        }

        // ###########################################################################################
        // Downloads the pending update, then applies it and restarts the app.
        // onProgress: optional callback receiving download progress (0-100).
        // ###########################################################################################
        public static async Task DownloadAndInstallAsync(Action<int>? onProgress = null)
        {
            if (_manager == null || _pendingUpdate == null)
                throw new InvalidOperationException("No pending update - call CheckForUpdateAsync first");

            try
            {
                await _manager.DownloadUpdatesAsync(_pendingUpdate, onProgress);
                Logger.Info("Update downloaded - restarting into new version");
                _manager.ApplyUpdatesAndRestart(_pendingUpdate);
            }
            catch (Exception ex)
            {
                Logger.Critical($"Update install failed - [{ex.Message}]");
                throw;
            }
        }

        // ###########################################################################################
        // Returns the version string of the available update, or null if none was found.
        // ###########################################################################################
        public static string? PendingVersion
            => _pendingUpdate?.TargetFullRelease.Version.ToString();
    }
}