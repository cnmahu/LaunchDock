using System.Collections.ObjectModel;
using Avalonia.Threading;
using LaunchDock.Core.Storage;

namespace LaunchDock.App.ViewModels;

public sealed class NoteItem : ViewModelBase
{
    private string _filePath;
    private string _title;

    public NoteItem(string filePath)
    {
        _filePath = filePath;
        _title = Path.GetFileNameWithoutExtension(filePath);
    }

    public string FilePath
    {
        get => _filePath;
        private set => SetProperty(ref _filePath, value);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    /// <summary>标题是否由正文自动生成；手动重命名后不再跟随正文变化。</summary>
    public bool IsAutoTitle { get; set; } = true;

    public void UpdateFile(string filePath)
    {
        FilePath = filePath;
        Title = Path.GetFileNameWithoutExtension(filePath);
    }

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
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var path in Directory
                         .EnumerateFiles(directory, "*.txt")
                         .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
                         .ToList())
            {
                var item = new NoteItem(path);
                var content = ReadNote(item);
                item.IsAutoTitle = IsPlaceholderTitle(item.Title)
                                   || string.Equals(item.Title, BuildTitle(content), StringComparison.CurrentCultureIgnoreCase);

                var oldPath = item.FilePath;
                if (item.IsAutoTitle)
                {
                    TryAutoRename(item, content);
                }

                if (!string.Equals(oldPath, item.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    renamed[oldPath] = item.FilePath;
                }

                Notes.Add(item);
            }
        }
        catch
        {
            Status = "无法读取便签目录";
            return;
        }

        if (previous is not null && renamed.TryGetValue(previous, out var renamedPath))
        {
            previous = renamedPath;
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

            var path = MakeUniquePath(directory, "新便签");
            File.WriteAllText(path, string.Empty);

            var item = new NoteItem(path);
            Notes.Add(item);
            SelectedNote = item;
            Status = "已新建便签，输入正文后自动命名";
        }
        catch
        {
            Status = "新建失败，请检查目录权限";
        }
    }

    /// <summary>手动重命名便签（同时重命名磁盘文件），之后不再跟随正文自动改名。</summary>
    public bool RenameNote(NoteItem note, string name)
    {
        var input = name.Trim();
        if (input.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            input = input[..^4];
        }

        var title = NormalizeTitle(input);
        var directory = Path.GetDirectoryName(note.FilePath);
        if (title.Length == 0 || string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        if (ReferenceEquals(note, SelectedNote))
        {
            // 先把当前编辑内容落盘，避免重命名后丢字
            note.IsAutoTitle = false;
            SaveNow();
        }

        if (string.Equals(title, note.Title, StringComparison.OrdinalIgnoreCase))
        {
            note.IsAutoTitle = false;
            return true;
        }

        var target = MakeUniquePath(directory, title);
        try
        {
            File.Move(note.FilePath, target);
        }
        catch
        {
            return false;
        }

        note.UpdateFile(target);
        note.IsAutoTitle = false;
        Status = "已重命名便签";
        return true;
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

            if (note.IsAutoTitle)
            {
                TryAutoRename(note, _noteText);
            }

            Status = $"已自动保存 {DateTime.Now:HH:mm:ss}";
        }
        catch
        {
            Status = "保存失败，请检查目录权限";
        }
    }

    /// <summary>按正文第一行重命名文件；空内容或重名冲突时保持原名。</summary>
    private static void TryAutoRename(NoteItem note, string content)
    {
        var title = BuildTitle(content);
        if (title.Length == 0)
        {
            // 空便签只把历史遗留的「便签 N」统一成「新便签」
            if (!IsPlaceholderTitle(note.Title) || note.Title.StartsWith("新便签", StringComparison.Ordinal))
            {
                return;
            }

            title = "新便签";
        }

        if (string.Equals(title, note.Title, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var directory = Path.GetDirectoryName(note.FilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var target = MakeUniquePath(directory, title);
        try
        {
            File.Move(note.FilePath, target);
            note.UpdateFile(target);
        }
        catch
        {
            // 文件名非法、被占用或重名时保留原名
        }
    }

    private static string BuildTitle(string content)
    {
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length > 0)
            {
                return NormalizeTitle(line);
            }
        }

        return string.Empty;
    }

    private static string NormalizeTitle(string raw)
    {
        var chars = raw.Trim().ToCharArray();
        var invalid = Path.GetInvalidFileNameChars();
        for (var index = 0; index < chars.Length; index++)
        {
            if (Array.IndexOf(invalid, chars[index]) >= 0)
            {
                chars[index] = ' ';
            }
        }

        var text = string.Join(' ', new string(chars).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        text = text.Trim().TrimEnd('.');
        if (text.Length > 30)
        {
            text = text[..30].Trim();
        }

        return text;
    }

    private static bool IsPlaceholderTitle(string title)
    {
        foreach (var prefix in new[] { "新便签", "未命名", "便签", "便笺" })
        {
            if (!title.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = title[prefix.Length..].Trim();
            return rest.Length == 0
                   || rest.All(ch => char.IsDigit(ch) || ch is '(' or ')' or ' ');
        }

        return false;
    }

    private static string MakeUniquePath(string directory, string title)
    {
        var path = Path.Combine(directory, title + ".txt");
        for (var index = 2; File.Exists(path); index++)
        {
            path = Path.Combine(directory, $"{title} ({index}).txt");
        }

        return path;
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
