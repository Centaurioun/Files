using Files.Shared;
using Files.App.Helpers;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;
using Windows.UI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using static Files.App.Views.PropertiesCustomization;
using Files.Shared.Extensions;
using Files.App.Shell;

namespace Files.App.Views
{
    public sealed partial class CustomFolderIcons : Page
    {
        private string selectedItemPath;
        private string iconResourceItemPath;
        private IShellPage appInstance;

        public ICommand RestoreDefaultIconCommand { get; private set; }
        public bool IsShortcutItem { get; private set; }

        public CustomFolderIcons()
        {
            this.InitializeComponent();
            RestoreDefaultIconCommand = new AsyncRelayCommand(RestoreDefaultIcon);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is IconSelectorInfo selectorInfo)
            {
                selectedItemPath = selectorInfo.SelectedItem;
                IsShortcutItem = selectorInfo.IsShortcut;
                iconResourceItemPath = selectorInfo.InitialPath;
                appInstance = selectorInfo.AppInstance;
                ItemDisplayedPath.Text = iconResourceItemPath;

                LoadIconsForPath(iconResourceItemPath);
            }
        }

        private async void PickDllButton_Click(object sender, RoutedEventArgs e)
        {
            Windows.Storage.Pickers.FileOpenPicker picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
            picker.FileTypeFilter.Add(".dll");
            picker.FileTypeFilter.Add(".exe");
            picker.FileTypeFilter.Add(".ico");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                iconResourceItemPath = file.Path;
                ItemDisplayedPath.Text = iconResourceItemPath;
                LoadIconsForPath(file.Path);
            }
        }

        private async void LoadIconsForPath(string path)
        {
            var icons = Win32API.ExtractIconsFromDLL(path);
            IconSelectionGrid.ItemsSource = icons;
        }

        private async void IconSelectionGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedIconInfo = (sender as GridView).SelectedItem as IconFileInfo;
            if (selectedIconInfo == null)
            {
                return;
            }
            var setIconResult = IsShortcutItem ?
                await SetCustomFileIcon(selectedItemPath, iconResourceItemPath, selectedIconInfo.Index) :
                SetCustomFolderIcon(selectedItemPath, iconResourceItemPath, selectedIconInfo.Index);
            if (setIconResult)
            {
                await App.Window.DispatcherQueue.EnqueueAsync(() =>
                {
                    appInstance?.FilesystemViewModel?.RefreshItems(null);
                });
            }
        }

        private async Task RestoreDefaultIcon()
        {
            RestoreDefaultButton.IsEnabled = false;

            var setIconResult = IsShortcutItem ?
                await SetCustomFileIcon(selectedItemPath, null) :
                SetCustomFolderIcon(selectedItemPath, null);
            if (setIconResult)
            {
                await App.Window.DispatcherQueue.EnqueueAsync(() =>
                {
                    appInstance?.FilesystemViewModel?.RefreshItems(null, async () =>
                    {
                        await DispatcherQueue.EnqueueAsync(() => RestoreDefaultButton.IsEnabled = true);
                    });
                });
            }
        }

        private bool SetCustomFolderIcon(string folderPath, string iconFile, int iconIndex = 0)
        {
            return Win32API.SetCustomDirectoryIcon(folderPath, iconFile, iconIndex);
        }

        private async Task<bool> SetCustomFileIcon(string filePath, string iconFile, int iconIndex = 0)
        {
            return await Win32API.SetCustomFileIconAsync(filePath, iconFile, iconIndex);
        }
    }
}
