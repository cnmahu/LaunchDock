using System.Collections.ObjectModel;
using Avalonia.Threading;
using RolanClone.Core.Storage;

namespace RolanClone.App.ViewModels;

public sealed class NoteItem
{
    public NoteItem(string filePath)
    {
        FilePath = filePath;
        Title = Path.GetFileNameWithoutExtension(filePath);
    }

    public string FilePath { get; }
    public string Title { get; }

    public override string ToString() => Title;
}

/// <summary>启动板内嵌的便签：左侧文本文件列表，右侧编辑区，输入后自动落盘。</summary>
public sealed class NotesViewModel : ViewModelBase
{
    private readonly Func<string> _resolveDirectory;
    private readonly DispatcherTimer _saveTimer;
    private NoteItem? _selectedNote;
    private string _noteText = string.Empty;
    private string _status = string.Empty;
    private bool _isOpen;

    public NotesViewModel(Func<string> resolveDirectory)
    {
        _resolveDirectory = resolveDirectory;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };
    }

    public ObservableCollection<NoteItem> Notes { get; } = new();

    public string DirectoryPath => _resolveDirectory();

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public NoteItem? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (ReferenceEquals(_selectedNote, value))
            {
                return;
            }

            SaveNow();
            SetProperty(ref _selectedNote, value);

            _noteText = ReadNote(value);
            OnPropertyChanged(nameof(NoteText));
            OnPropertyChanged(nameof(HasNote));
        }
    }

    public bool HasNote => SelectedNote is not null;

    public string NoteText
    {
        get => _noteText;
        set
        {
            if (!SetProperty(ref _noteText, value) || SelectedNote is null)
            {
                return;
            }

            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    public void Open()
    {
        Reload();
        IsOpen = true;
    }

    public void Close()
    {
        SaveNow();
        IsOpen = false;
    }

    /// <summary>重新扫描目录（目录设置变化或重新打开时调用）。</summary>
    public void Reload()
    {
        SaveNow();

        var directory = DirectoryPath;
        var previous = SelectedNote?.FilePath;

        Notes.Clear();
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var path in Directory
                         .EnumerateFiles(directory, "*.txt")
                         .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase))
            {
                Notes.Add(new NoteItem(path));
            }
        }
        catch
        {
            Status = "无法读取便签目录";
            return;
        }

        _selectedNote = null;
        SelectedNote = Notes.FirstOrDefault(note =>
                           string.Equals(note.FilePath, previous, StringComparison.OrdinalIgnoreCase))
                       ?? Notes.FirstOrDefault();
        Status = string.Empty;

        if (SelectedNote is null)
        {
            AddNote();
        }
    }

    public void AddNote()
    {
        SaveNow();

        var directory = DirectoryPath;
        try
        {
            Directory.CreateDirectory(directory);

            var index = 1;
            string path;
            do
            {
                path = Path.Combine(directory, $"便签 {index}.txt");
                index++;
            } while (File.Exists(path));

            File.WriteAllText(path, string.Empty);

            var item = new NoteItem(path);
            Notes.Add(item);
            SelectedNote = item;
            Status = "已新建便签";
        }
        catch
        {
            Status = "新建失败，请检查目录权限";
        }
    }

    /// <summary>立即把当前文本写回文件。</summary>
    public void SaveNow()
    {
        _saveTimer.Stop();

        var note = SelectedNote;
        if (note is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(note.FilePath, _noteText);
            Status = $"已自动保存 {DateTime.Now:HH:mm:ss}";
        }
        catch
        {
            Status = "保存失败，请检查目录权限";
        }
    }

    private static string ReadNote(NoteItem? note)
    {
        if (note is null)
        {
            return string.Empty;
        }

        try
        {
            return File.ReadAllText(note.FilePath);
        }
        catch
        {
            return string.Empty;
        }
    }
}
