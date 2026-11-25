using DentalLab.Application;
using DentalLab.Shared;
using Xunit;

namespace DentalLab.Application.Tests;

public class BasicTests
{
    [Fact]
    public void AppPaths_AreUnderLocalAppDataDentalLab_AndNotInstallDir()
    {
        Assert.EndsWith("DentalLab", AppPaths.Root);
        Assert.StartsWith(AppPaths.Root, AppPaths.DatabaseFile);
        Assert.EndsWith("dentallab.db", AppPaths.DatabaseFile);
        Assert.DoesNotContain(AppContext.BaseDirectory.TrimEnd('\\', '/'), AppPaths.Root);
    }

    [Fact]
    public void SettingKeys_AreUnique()
    {
        var keys = new[] { SettingKeys.Theme, SettingKeys.Language, SettingKeys.ReducedMotion,
                           SettingKeys.SidebarExpanded, SettingKeys.LabName };
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }
}
