using CommunityToolkit.Mvvm.DependencyInjection;
using Files.Shared;
using Files.Shared.Enums;
using Files.App.Helpers;
using CommunityToolkit.WinUI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml.Media.Imaging;
using Files.App.Shell;
using Vanara.PInvoke;
using Vanara.Windows.Shell;

namespace Files.App.Filesystem
{
    public class RecentItemsManager : IDisposable
    {
        private readonly ILogger logger = Ioc.Default.GetService<ILogger>();
        private const string QuickAccessGuid = "::{679f85cb-0220-4080-b29b-5540cc05aab6}";

        public EventHandler<NotifyCollectionChangedEventArgs> RecentFilesChanged;
        public EventHandler<NotifyCollectionChangedEventArgs> RecentFoldersChanged;

        // recent files
        private readonly List<RecentItem> recentFiles = new();
        public IReadOnlyList<RecentItem> RecentFiles    // already sorted
        {
            get
            {
                lock (recentFiles)
                {
                    return recentFiles.ToList().AsReadOnly();
                }
            }
        }

        // recent folders
        private readonly List<RecentItem> recentFolders = new();
        public IReadOnlyList<RecentItem> RecentFolders  // already sorted
        {
            get
            {
                lock (recentFolders)
                {
                    return recentFolders.ToList().AsReadOnly();
                }
            }
        }

        /// <summary>
        /// Refetch recent files to `recentFiles`.
        /// </summary>
        public async Task UpdateRecentFilesAsync()
        {
            // enumerate with fulltrust process
            List<RecentItem> enumeratedFiles = await ListRecentFilesAsync();
            if (enumeratedFiles != null)
            {
                lock (recentFiles)
                {
                    recentFiles.Clear();
                    recentFiles.AddRange(enumeratedFiles);
                    // do not sort here, enumeration order *is* the correct order since we get it from Quick Access
                }

                // todo: potentially optimize this and figure out if list changed by either (1) Add (2) Remove (3) Move
                // this way the UI doesn't have to refresh the entire list everytime a change occurs
                RecentFilesChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }

        /// <summary>
        /// Refetch recent folders to `recentFolders`.
        /// </summary>
        public async Task UpdateRecentFoldersAsync()
        {
            // enumerate with fulltrust process
            List<RecentItem> enumeratedFolders = ListRecentFolders();
            if (enumeratedFolders != null)
            {
                lock (recentFolders)
                {
                    recentFolders.Clear();
                    recentFolders.AddRange(enumeratedFolders);

                    // shortcut modifications in `Windows\Recent` consist of a delete + add operation;
                    // thus, last modify date is reset and we can sort off it
                    recentFolders.Sort((x, y) => y.LastModified.CompareTo(x.LastModified));
                }

                RecentFoldersChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }

        /// <summary>
        /// Enumerate recently accessed files via `Quick Access`.
        /// </summary>
        public async Task<List<RecentItem>> ListRecentFilesAsync()
        {
            var items = (await Win32Shell.GetShellFolderAsync(QuickAccessGuid, "Enumerate", 0, int.MaxValue)).Enumerate
                                   .Select(link => new RecentItem(link)).ToList();
            return items;
        }

        /// <summary>
        /// Enumerate recently accessed folders via `Windows\Recent`.
        /// </summary>
        public List<RecentItem> ListRecentFolders()
        {
            var recentItems = new List<RecentItem>();
            var excludeMask = FileAttributes.Hidden;
            var linkFilePaths = Directory.EnumerateFiles(CommonPaths.RecentItemsPath).Where(f => (new FileInfo(f).Attributes & excludeMask) == 0);

            foreach (var linkFilePath in linkFilePaths)
            {
                using var link = new ShellLink(linkFilePath, LinkResolution.NoUIWithMsgPump, null, TimeSpan.FromMilliseconds(100));

                try
                {
                    if (!string.IsNullOrEmpty(link.TargetPath) && link.Target.IsFolder)
                    {
                        var shellLinkItem = ShellFolderExtensions.GetShellLinkItem(link);
                        recentItems.Add(new RecentItem(shellLinkItem));
                    }
                }
                catch (FileNotFoundException)
                {
                    // occurs when shortcut or shortcut target is deleted and accessed (link.Target)
                    // consequently, we shouldn't include the item as a recent item
                }
                catch (Exception ex)
                {
                    App.Logger.Warn(ex, ex.Message);
                }
            }

            return recentItems;
        }

        /// <summary>
        /// Adds a shortcut to `Windows\Recent`. The path can be to a file or folder.
        /// It will update to `recentFiles` or `recentFolders` respectively.
        /// </summary>
        /// <param name="path">Path to a file or folder</param>
        /// <returns>Whether the action was successfully handled or not</returns>
        public bool AddToRecentItems(string path)
        {
            try
            {
                Shell32.SHAddToRecentDocs(Shell32.SHARD.SHARD_PATHW, path);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.Warn(ex, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Clears both `recentFiles` and `recentFolders`.
        /// This will also clear the Recent Files (and its jumplist) in File Explorer.
        /// </summary>
        /// <returns>Whether the action was successfully handled or not</returns>
        public bool ClearRecentItems()
        {
            try
            {
                Shell32.SHAddToRecentDocs(Shell32.SHARD.SHARD_PIDL, (string)null);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.Warn(ex, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Unpin (or remove) a file from `recentFiles`.
        /// This will also unpin the item from the Recent Files in File Explorer.
        /// </summary>
        /// <returns>Whether the action was successfully handled or not</returns>
        public bool UnpinFromRecentFiles(string path)
        {
            try
            {
                var command = $"-command \"((New-Object -ComObject Shell.Application).Namespace('shell:{QuickAccessGuid}\').Items() " +
                              $"| Where-Object {{ $_.Path -eq '{path}' }}).InvokeVerb('remove')\"";
                return Win32API.RunPowershellCommand(command, false);
            }
            catch (Exception ex)
            {
                App.Logger.Warn(ex, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Handle any events received from the fulltrust process.
        /// Events are only received when the user is on the home page (YourHomeViewModel is loaded).
        /// </summary>
        public async Task HandleWin32RecentItemsEvent(string changeType)
        {
            System.Diagnostics.Debug.WriteLine(nameof(HandleWin32RecentItemsEvent) + $": ({changeType})");

            switch (changeType)
            {
                case "QuickAccessJumpListChanged":
                    await UpdateRecentFilesAsync();
                    break;

                default:
                    logger.Warn($"{nameof(HandleWin32RecentItemsEvent)}: Received invalid changeType of {changeType}");
                    break;
            }
        }

        /// <summary>
        /// Returns whether two RecentItem enumerables have the same order.
        /// This function depends on `RecentItem` implementing IEquatable.
        /// </summary>
        private bool RecentItemsOrderEquals(IEnumerable<RecentItem> oldOrder, IEnumerable<RecentItem> newOrder)
        {
            if (oldOrder is null || newOrder is null)
            {
                return false;
            }

            return oldOrder.SequenceEqual(newOrder);
        }

        public void Dispose() { }
    }
}