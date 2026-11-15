using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RolanClone.Core.Model;
using RolanClone.Windows.Launching;

namespace RolanClone.App.Views;

public partial class NewItemWindow : Window
{
    public NewItemWindow()
    {
        InitializeComponent();

        foreach (var action in BuiltinActions.All)
        {
            ActionBox.Items.Add(new ComboBoxItem { Content = action.Name, Tag = action.Key });
        }

        ActionBox.SelectedIndex = 0;
        Opened += (_, _) => NameBox.Focus();
    }

    private ItemKind SelectedKind => TypeBox.SelectedIndex switch
    {
        1 => ItemKind.File,
        2 => ItemKind.Folder,
        3 => ItemKind.Url,
        4 => ItemKind.Command,
        5 => ItemKind.Builtin,
        _ => ItemKind.Exe
    };

    private void OnTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TargetPanel is null || BuiltinPanel is null)
        {
            return;
        }

        var isBuiltin = SelectedKind == ItemKind.Builtin;
        TargetPanel.IsVisible = !isBuiltin;
        BuiltinPanel.IsVisible = isBuiltin;

        if (BrowseButton is not null)
        {
            BrowseButton.IsVisible = SelectedKind is ItemKind.Exe or ItemKind.File or ItemKind.Folder;
        }
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var storage = StorageProvider;
        if (storage is null)
        {
            return;
        }

        if (SelectedKind == ItemKind.Folder)
        {
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择文件夹",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                ApplyPickedPath(folders[0].TryGetLocalPath());
            }

            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择文件",
            AllowMultiple = false
        });

        if (files.Count > 0)
        {
            ApplyPickedPath(files[0].TryGetLocalPath());
        }
    }

    private void ApplyPickedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        TargetBox.Text = path;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            NameBox.Text = Path.GetFileNameWithoutExtension(path);
        }
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedKind == ItemKind.Builtin)
        {
            if (ActionBox.SelectedItem is not ComboBoxItem { Tag: string key })
            {
                return;
            }

            var name = string.IsNullOrWhiteSpace(NameBox.Text) ? BuiltinActions.GetName(key) : NameBox.Text.Trim();
            Close(new ItemModel
            {
                DisplayName = name,
                Kind = ItemKind.Builtin,
                Target = key
            });
            return;
        }

        var target = TargetBox.Text?.Trim() ?? string.Empty;
        if (target.Length == 0)
        {
            return;
        }

        var displayName = NameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = SelectedKind == ItemKind.Url
                ? target
                : Path.GetFileNameWithoutExtension(target);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = target;
            }
        }

        Close(new ItemModel
        {
            DisplayName = displayName,
            Kind = SelectedKind,
            Target = target
        });
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
