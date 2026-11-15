using System.Collections.ObjectModel;
using RolanClone.Core.Model;

namespace RolanClone.App.ViewModels;

public sealed class TabViewModel : ViewModelBase
{
    private bool _isSelected;
    private string _name;

    public TabViewModel(TabModel model, int tabIndex)
    {
        Model = model;
        _name = model.Name;

        if (model.Kind != TabKind.Normal)
        {
            // 文件夹分组的内容由 LauncherViewModel 按目录实时生成，不写回配置
            return;
        }

        for (var i = 0; i < model.Items.Count; i++)
        {
            Items.Add(new ItemViewModel(model.Items[i], tabIndex + i));
        }
    }

    public TabModel Model { get; }
    public string Id => Model.Id;
    public bool IsLiveFolder => Model.Kind == TabKind.LiveFolder;
    public string? SourcePath => Model.SourcePath;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                Model.Name = value;
            }
        }
    }

    public ObservableCollection<ItemViewModel> Items { get; } = new();

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
