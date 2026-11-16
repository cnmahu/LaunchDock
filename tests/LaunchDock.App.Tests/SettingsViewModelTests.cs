using Avalonia.Headless.XUnit;
using LaunchDock.App.ViewModels;
using LaunchDock.Core.Model;
using Xunit;

namespace LaunchDock.App.Tests;

/// <summary>
/// M2 样板收敛后的行为锁定测试：验证收敛后的 SetSetting/SetNumber
/// 完整保留了各设置项原有语义（同值短路、clamp 边界、浮点容差、标签通知、索引映射）。
/// 用 [AvaloniaFact] 保证 ApplyTheme 有存活的 Application 实例。
/// </summary>
public sealed class SettingsViewModelTests
{
    private static (SettingsViewModel Vm, List<string> Changes) Create()
    {
        var changes = new List<string>();
        var vm = new SettingsViewModel(new SettingsModel(), () => changes.Add("changed"));
        return (vm, changes);
    }

    private static List<string> TrackPropertyChanged(SettingsViewModel vm)
    {
        var names = new List<string>();
        vm.PropertyChanged += (_, e) => names.Add(e.PropertyName ?? string.Empty);
        return names;
    }

    // ---------- 布尔项：同值短路 / 变化才保存 ----------

    [AvaloniaFact]
    public void BoolSetter_SameValue_DoesNotSave()
    {
        var (vm, changes) = Create();
        Assert.False(vm.TopMost); // 默认 false

        vm.TopMost = false;
        Assert.Empty(changes);

        vm.TopMost = true;
        Assert.True(vm.TopMost);
        Assert.Single(changes);
    }

    [AvaloniaFact]
    public void BoolSetter_NotifiesOwnProperty()
    {
        var (vm, changes) = Create();
        var notified = TrackPropertyChanged(vm);

        vm.CloseOnEsc = !vm.CloseOnEsc;

        Assert.Contains(nameof(SettingsViewModel.CloseOnEsc), notified);
        Assert.Single(changes);
    }

    // ---------- 数值项：clamp 边界 / 容差短路 / 标签通知 ----------

    [AvaloniaFact]
    public void Opacity_IgnoresSliderZero()
    {
        var (vm, changes) = Create();
        var original = vm.Opacity; // 默认 0.96

        vm.Opacity = 0;

        Assert.Equal(original, vm.Opacity);
        Assert.Empty(changes);
    }

    [AvaloniaFact]
    public void Opacity_ClampsToValidRange()
    {
        var (vm, _) = Create();
        var notified = TrackPropertyChanged(vm);

        vm.Opacity = 0.3;
        Assert.Equal(0.5, vm.Opacity);

        vm.Opacity = 5;
        Assert.Equal(1.0, vm.Opacity);
        Assert.Contains(nameof(SettingsViewModel.OpacityLabel), notified);
    }

    [AvaloniaFact]
    public void Opacity_TinyJitter_DoesNotSave()
    {
        var (vm, changes) = Create();
        vm.Opacity = 0.6;
        var count = changes.Count;

        vm.Opacity = 0.6001; // 距当前值 < 0.001 容差

        Assert.Equal(count, changes.Count);
        Assert.Equal(0.6, vm.Opacity);
    }

    [AvaloniaFact]
    public void IconSizePx_ClampsAndRounds()
    {
        var (vm, changes) = Create();
        var notified = TrackPropertyChanged(vm);

        vm.IconSizePx = 10;   // 下界 24
        Assert.Equal(24, vm.IconSizePx);

        vm.IconSizePx = 999;  // 上界 96
        Assert.Equal(96, vm.IconSizePx);

        vm.IconSizePx = 64.4; // 四舍五入到 64
        Assert.Equal(64, vm.IconSizePx);
        Assert.Contains(nameof(SettingsViewModel.IconSizeLabel), notified);
        Assert.True(changes.Count >= 3);
    }

    [AvaloniaFact]
    public void IconSpacing_ClampsToZeroAndFortyEight()
    {
        var (vm, _) = Create();

        vm.IconSpacing = -5;
        Assert.Equal(0, vm.IconSpacing);

        vm.IconSpacing = 100;
        Assert.Equal(48, vm.IconSpacing);
    }

    [AvaloniaFact]
    public void FontSize_RoundsAndClamps()
    {
        var (vm, _) = Create();

        vm.FontSize = 13.6; // Round → 14
        Assert.Equal(14, vm.FontSize);

        vm.FontSize = 1;   // 下界 10
        Assert.Equal(10, vm.FontSize);

        vm.FontSize = 99;  // 上界 18
        Assert.Equal(18, vm.FontSize);
    }

    [AvaloniaFact]
    public void FontSize_RoundToSameValue_DoesNotSave()
    {
        var (vm, changes) = Create();
        vm.FontSize = 12;
        var count = changes.Count;

        vm.FontSize = 12.3; // Round → 12，同值短路

        Assert.Equal(count, changes.Count);
    }

    // ---------- 索引项：clamp + 枚举映射 + 附加动作 ----------

    [AvaloniaFact]
    public void ThemeIndex_ClampsAndAppliesThemeWithoutCrash()
    {
        var (vm, changes) = Create();

        vm.ThemeIndex = 99;  // clamp → Dark
        Assert.Equal(2, vm.ThemeIndex);

        vm.ThemeIndex = -5;  // clamp → Light
        Assert.Equal(0, vm.ThemeIndex);
        Assert.True(changes.Count >= 2); // ApplyTheme 在无 Application 时安全跳过
    }

    [AvaloniaFact]
    public void MaterialIndex_SameValue_DoesNotSave()
    {
        var (vm, changes) = Create();
        Assert.Equal(0, vm.MaterialIndex); // 默认 Acrylic

        vm.MaterialIndex = 0;
        Assert.Empty(changes);

        vm.MaterialIndex = 1;
        Assert.Single(changes);
    }

    [AvaloniaFact]
    public void IndexProperties_MapToEnumValues()
    {
        var (vm, changes) = Create();

        vm.LabelPositionIndex = 2;
        Assert.Equal(2, vm.LabelPositionIndex); // None

        vm.LabelPositionIndex = 1;
        Assert.Equal(1, vm.LabelPositionIndex); // Right

        vm.CloseBehaviorIndex = 1;
        Assert.Equal(1, vm.CloseBehaviorIndex); // Exit

        vm.ShowPositionIndex = 9;  // clamp → 2
        Assert.Equal(2, vm.ShowPositionIndex); // Mouse

        vm.FontWeightIndex = 3;
        Assert.Equal(3, vm.FontWeightIndex); // SemiBold

        vm.LayoutIndex = 1;
        Assert.Equal(1, vm.LayoutIndex); // Vertical

        vm.OpenModeIndex = 1;   // 双击运行
        vm.SortModeIndex = 1;   // 按使用频率

        // LabelPositionIndex 赋值两次，其余各一次 → 共 8 次保存
        Assert.Equal(8, changes.Count);
    }

    [AvaloniaFact]
    public void SetPassword_HashesAndEnablesAutoLock()
    {
        var (vm, changes) = Create();

        vm.SetPassword("secret");

        Assert.True(vm.HasPassword);
        Assert.True(vm.AutoLock); // 设密码后自动开启锁定
        Assert.Equal("已设置密码", vm.PasswordStatus);

        vm.ClearPassword();
        Assert.False(vm.HasPassword);
        Assert.Equal("未设置密码", vm.PasswordStatus);
    }
}
