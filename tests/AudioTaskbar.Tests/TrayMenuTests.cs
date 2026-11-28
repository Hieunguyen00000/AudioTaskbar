namespace AudioTaskbar.Tests;

/// <summary>v5.4 tray menu: removed items stay removed, the update commands are merged, new items are present.</summary>
public class TrayMenuTests
{
    private static string[] Texts(bool store) => TrayMenuModel.TopLevel(store).Where(e => e.Id != TrayMenuModel.Sep).Select(e => e.Text).ToArray();

    [Fact]
    public void Weather_info_toggle_is_gone()
    {
        foreach (var store in new[] { false, true })
        {
            Assert.DoesNotContain(Texts(store), t => t.Contains("Hiện thông tin", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(Texts(store), t => t.Contains("thông tin thời tiết", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Effects_toggle_item_is_hidden_but_the_hotkey_still_exists()
    {
        foreach (var store in new[] { false, true })
        {
            Assert.DoesNotContain(Texts(store), t => t.Contains("F8"));
            Assert.DoesNotContain(Texts(store), t => t.Contains("Tắt hiệu ứng", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(Texts(store), t => t.Contains("Bật hiệu ứng", StringComparison.OrdinalIgnoreCase));
        }
        // the hotkey (default F8) is still configurable and valid
        var cfg = new AppConfig(); cfg.Normalize();
        Assert.Equal("F8", cfg.Hotkey);
        Assert.True(Hotkey.TryParse(cfg.Hotkey, out _, out uint vk, out _) && vk == 0x77);
    }

    [Fact]
    public void There_is_one_merged_update_item_and_none_in_the_store_edition()
    {
        var web = TrayMenuModel.TopLevel(false);
        Assert.Single(web, e => e.Id == TrayMenuModel.Update);
        Assert.Equal("Kiểm tra & cập nhật", web.Single(e => e.Id == TrayMenuModel.Update).Text);
        Assert.Single(web, e => e.Text.Contains("cập nhật", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(web, e => e.Text.Contains("Cập nhật ngay", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(TrayMenuModel.TopLevel(true), e => e.Id == TrayMenuModel.Update);
        Assert.DoesNotContain(TrayMenuModel.TopLevel(true), e => e.Text.Contains("cập nhật", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Expected_items_are_present_in_order_and_exit_is_last()
    {
        var ids = TrayMenuModel.TopLevel(false).Select(e => e.Id).Where(i => i != TrayMenuModel.Sep).ToArray();
        Assert.Equal(new[] { "open", "scene", "time", "season", "region", "preview", "refresh", "reload", "update", "exit" }, ids);
        Assert.Equal("exit", TrayMenuModel.TopLevel(true).Last().Id);
        Assert.Equal(TrayMenuModel.Sep, TrayMenuModel.TopLevel(true)[^2].Id);
        // no double separators, none at either end
        var all = TrayMenuModel.TopLevel(true);
        Assert.NotEqual(TrayMenuModel.Sep, all[0].Id);
        for (int i = 1; i < all.Count; i++) Assert.False(all[i].Id == TrayMenuModel.Sep && all[i - 1].Id == TrayMenuModel.Sep);
    }

    [Fact]
    public void Season_submenu_has_the_automatic_modes_and_four_seasons_and_weather_has_snow_and_random()
    {
        Assert.Equal(new[] { "real", "cycle", "spring", "summer", "autumn", "winter" }, TrayMenuModel.Seasons.Select(s => s.Id));
        Assert.Equal(new[] { "Thực tế", "Tự đổi mùa" }, TrayMenuModel.Seasons.Take(2).Select(s => s.Label));
        Assert.Contains(TrayMenuModel.Weathers, w => w.Id == "snow");
        Assert.Contains(TrayMenuModel.Weathers, w => w.Id == "random" && w.Label == "Ngẫu nhiên");
        Assert.Equal(TrayMenuModel.Weathers, PortalForm.Weathers);
        Assert.Equal(TrayMenuModel.Seasons, PortalForm.Seasons);
    }

    [Fact]
    public void Region_pick_list_has_the_world_destinations_but_not_the_removed_ones()
    {
        var ids = RegionCatalog.PickList().Select(r => r.Id).ToArray();
        foreach (var id in new[] { "sydney", "newyork" }) Assert.Contains(id, ids);
        foreach (var id in new[] { "rome", "giza", "santorini" }) Assert.DoesNotContain(id, ids);
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void Panel_and_tray_use_the_same_update_text()
    {
        Assert.Equal(TrayMenuModel.UpdateText, PortalForm.UpdateButtonText);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Launch_check_respects_the_auto_update_switch_and_the_store_edition(bool autoOn, bool store)
    {
        var cfg = new AppConfig { AutoUpdateEnabled = autoOn };
        Assert.Equal(autoOn && !store, UpdatePolicy.ShouldCheckOnLaunch(cfg, store));
        Assert.False(UpdatePolicy.ShouldCheckOnLaunch(cfg, storeEdition: true));
    }

    [Fact]
    public void Auto_update_and_silent_install_default_to_on()
    {
        var cfg = new AppConfig(); cfg.Normalize();
        Assert.True(cfg.AutoUpdateEnabled);
        Assert.True(cfg.AutoUpdateSilentInstall);
        Assert.True(UpdatePolicy.ShouldCheckOnLaunch(cfg, false));
    }

    [Theory]
    [InlineData(false, false, true, false, true)]   // launch, silent on → installs
    [InlineData(false, false, false, false, false)] // launch, silent off → only notifies
    [InlineData(false, false, true, true, false)]   // launch from a dev checkout → never self-replaces
    [InlineData(true, false, true, false, false)]   // manual check that did not ask to install
    [InlineData(true, true, false, false, true)]    // the merged button always installs
    [InlineData(true, true, false, true, true)]
    public void Install_decision(bool manual, bool install, bool silent, bool dev, bool expected)
        => Assert.Equal(expected, UpdatePolicy.ShouldInstall(manual, install, silent, dev));
}
