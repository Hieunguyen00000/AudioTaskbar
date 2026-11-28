using System.Text.Json;

namespace AudioTaskbar.Tests;

/// <summary>Fake of the running app for the control panel: same live-edit / save / discard semantics as TrayApp.</summary>
internal sealed class FakePortalHost : IPortalHost
{
    private readonly JsonSerializerOptions _o = ConfigStore.JsonOptions;
    private string _savedJson;
    public FakePortalHost(AppConfig? cfg = null) { Config = cfg ?? new AppConfig(); Config.Normalize(); _savedJson = Json(); }
    private string Json() => JsonSerializer.Serialize(Config, _o);
    public AppConfig Config { get; private set; }
    public bool EffectOn { get; set; } = true;
    public bool Fused => true;
    public string StatusText => "Đang chạy";
    public string HotkeyDisplay => "F8";
    public bool HotkeyOk => true;
    public UpdateCheckResult? LastUpdateResult => null;
    public bool UpdateBusy => false;
    public double UpdateProgress => 0;
    public string UpdateStage => "";
    public PortalInfo WeatherInfo => PortalInfo.Build(null, Config, RegionCatalog.Default);
    public bool HasUnsavedChanges => Json() != _savedJson;
    public event Action? StateChanged;
    public int Saves, Discards, Updates, Exits;
    public void Mutate(Action<AppConfig> change, bool refreshWeather = false, bool hotkey = false, bool fusion = false) { change(Config); Config.Normalize(); StateChanged?.Invoke(); }
    public void SetEffect(bool on) { EffectOn = on; StateChanged?.Invoke(); }
    public Task CheckAndUpdateAsync() { Updates++; return Task.CompletedTask; }
    public void SaveConfig() { Saves++; _savedJson = Json(); StateChanged?.Invoke(); }
    public void DiscardChanges() { Discards++; Config = JsonSerializer.Deserialize<AppConfig>(_savedJson, _o)!; Config.Normalize(); StateChanged?.Invoke(); }
    public void ExitFromPortal() => Exits++;
}

[Collection("Statics")]
public class PortalFormV54Tests
{
    private static void Sta(Action a)
    {
        Exception? err = null;
        var t = new Thread(() => { try { a(); } catch (Exception e) { err = e; } });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (err is not null) throw new Exception("STA test failed: " + err, err);
    }

    /// <summary>Button.PerformClick needs a shown form; call the click handler directly instead.</summary>
    private static void Click(Button b) => typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(b, new object[] { EventArgs.Empty });

    private static void WithForm(Action<PortalForm, FakePortalHost> body, AppConfig? cfg = null) => Sta(() =>
    {
        var host = new FakePortalHost(cfg);
        using var f = new PortalForm(host);
        _ = f.Handle;
        body(f, host);
    });

    [Fact]
    public void System_and_update_settings_and_sound_are_collapsed_by_default()
    {
        WithForm((f, _) =>
        {
            Assert.False(f.SettingsExpanded);
            Assert.False(f.SoundExpanded);
            // always visible: scenery, weather and season; nothing about updates or startup
            var visible = f.VisibleTexts().ToList();
            Assert.Contains(PortalForm.SettingsTitle, visible);
            Assert.DoesNotContain(visible, t => t.Contains("Khởi động cùng Windows"));
            Assert.DoesNotContain(visible, t => t.Contains("cập nhật", StringComparison.OrdinalIgnoreCase) && t != PortalForm.SettingsTitle);
            Assert.DoesNotContain(visible, t => t.Contains("Mở ẩn xuống khay"));
            if (!StoreEdition.UsesStoreUpdates) Assert.True(f.IsInCollapsedSection(f.UpdateButton));
            Assert.True(f.IsInCollapsedSection(f.MasterSlider));
            Assert.False(f.IsInCollapsedSection(f.WeatherBox));
            Assert.False(f.IsInCollapsedSection(f.SeasonBox));
            Assert.False(f.IsInCollapsedSection(f.PaddingBox));
            f.SettingsExpanded = true;
            if (!StoreEdition.UsesStoreUpdates) Assert.False(f.IsInCollapsedSection(f.UpdateButton));
            Assert.Contains(f.VisibleTexts(), t => t.Contains("Khởi động cùng Windows"));
        });
    }

    [Fact]
    public void Panel_has_the_four_cards_with_their_headers()
    {
        WithForm((f, _) =>
        {
            var texts = f.AllTexts().ToList();
            Assert.Equal(new[] { "Thông tin cảnh vật & vị trí", "Thời tiết", "Mùa", "Khung cảnh" }, PortalForm.CardTitles);
            foreach (var t in PortalForm.CardTitles)
                Assert.Single(f.AllControls().OfType<Label>(), l => l.Text == t && l.Font.Name == "Segoe UI Semibold");   // header exactly once
            // the old merged card is gone
            Assert.DoesNotContain(texts, t => t.StartsWith("Cảnh vật &") && t.Contains("Mùa"));
            Assert.Equal(PortalForm.Seasons.Length, f.SeasonBox.Items.Count);
            Assert.Equal(PortalForm.Weathers.Length, f.WeatherBox.Items.Count);
            Assert.Contains(f.SeasonBox.Items.Cast<object>().Select(o => o.ToString()), t => t == "Mùa đông");
        });
    }

    [Fact]
    public void Each_setting_sits_in_the_right_card_and_is_visible_by_default()
    {
        WithForm((f, _) =>
        {
            void In(string card, params Control[] cs) { foreach (var c in cs) { Assert.Equal(card, f.CardTitleOf(c)); Assert.False(f.IsInCollapsedSection(c)); } }
            In(PortalForm.InfoTitle, f.RegionBox);
            In(PortalForm.WeatherTitle, f.WeatherBox, f.WeatherCycleBox, f.EffectsBox);
            In(PortalForm.SeasonTitle, f.SeasonBox, f.SeasonCycleBox);
            In(PortalForm.SceneryTitle, f.TimeBox, f.TrainBox, f.VehiclesBox, f.AmbientBox, f.PaddingBox);
            // location + current weather rows live in the info card, as label/value rows
            var texts = f.AllTexts().ToList();
            foreach (var label in new[] { "Vị trí", "Thời tiết thực", "Khu vực", "Thời tiết", "Tự đổi thời tiết", "Hiệu ứng thời tiết", "Mùa", "Tự đổi mùa",
                                          "Ngày & đêm", "Độ dài chu kỳ", "Tàu hỏa", "Phương tiện", "Chi tiết nền", "Vùng đệm xám" })
                Assert.Contains(label, texts);
            Assert.DoesNotContain(texts, t => t.StartsWith("Vị trí:") || t.StartsWith("Cảnh vật:"));   // no wrapped sentence lines any more
            // Sound and Cài đặt are collapsed expanders
            Assert.True(f.IsInCollapsedSection(f.MasterSlider));
            Assert.False(f.SoundExpanded); Assert.False(f.SettingsExpanded);
            Assert.Equal(PortalForm.SoundTitle, f.CardTitleOf(f.MasterSlider));
        });
    }

    [Fact]
    public void Cards_share_one_style_width_label_column_and_number_box_width()
    {
        WithForm((f, _) =>
        {
            var cards = f.AllControls().Where(c => c.GetType().Name == "Card").ToList();
            Assert.True(cards.Count >= 4);
            Assert.All(cards, c => Assert.Equal(PortalForm.CardWidth, c.Width));
            var grids = cards.Select(c => c.Controls.OfType<TableLayoutPanel>().First()).ToList();
            Assert.All(grids, g => Assert.Equal(PortalForm.LabelColumnWidth, (int)g.ColumnStyles[0].Width));
            var heads = cards.Select(c => c.Controls.OfType<TableLayoutPanel>().First().Controls.OfType<Label>().First()).Where(l => l.Text is "Hệ thống" or "Cập nhật" || PortalForm.CardTitles.Contains(l.Text)).ToList();
            Assert.True(heads.Count >= 6);
            Assert.Single(heads.Select(h => (h.Font.Name, h.Font.Size, h.ForeColor)).Distinct());
            var frames = f.AllControls().OfType<NumericUpDown>().Select(n => n.Parent!).ToList();
            Assert.True(frames.Count >= 5);
            Assert.Single(frames.Select(x => x.Width).Distinct());
        });
    }

    [Fact]
    public void There_is_exactly_one_update_button_with_the_merged_text()
    {
        WithForm((f, h) =>
        {
            if (StoreEdition.UsesStoreUpdates)
            {
                // Store builds are updated by the Store: no update button at all
                Assert.DoesNotContain(f.AllControls().OfType<Button>(), b => b.Text.Contains("cập nhật", StringComparison.OrdinalIgnoreCase));
                return;
            }
            var buttons = f.AllControls().OfType<Button>().Where(b => b.Text.Contains("cập nhật", StringComparison.OrdinalIgnoreCase) || b.Text.Contains("Kiểm tra")).ToList();
            Assert.Single(buttons);
            Assert.Equal("Kiểm tra & cập nhật", buttons[0].Text);
            Assert.DoesNotContain(f.AllTexts(), t => t.Contains("Cập nhật ngay", StringComparison.OrdinalIgnoreCase));
            Click(buttons[0]);
            Assert.Equal(1, h.Updates);
        });
    }

    [Fact]
    public void Edits_apply_live_but_are_saved_only_by_the_save_button()
    {
        WithForm((f, h) =>
        {
            Assert.False(f.SaveButton.Enabled);
            f.PaddingBox.Checked = false;
            Assert.False(h.Config.ShowIconPadding);          // live preview
            Assert.True(h.HasUnsavedChanges);
            Assert.Equal(0, h.Saves);
            Assert.True(f.SaveButton.Enabled);
            Click(f.SaveButton);
            Assert.Equal(1, h.Saves);
            Assert.False(h.HasUnsavedChanges);
            Assert.False(f.SaveButton.Enabled);
            Assert.Equal(SaveText, f.SaveButton.Text);
        });
    }

    private const string SaveText = "Lưu cấu hình";

    [Fact]
    public void Choosing_snow_in_the_panel_switches_to_winter_and_leaving_winter_clears_it()
    {
        WithForm((f, h) =>
        {
            int snow = Array.FindIndex(PortalForm.Weathers, w => w.Id == "snow");
            f.WeatherBox.SelectedIndex = snow;
            Assert.Equal("snow", h.Config.WeatherPreview);
            Assert.Equal("winter", h.Config.Season);
            Assert.Equal(Array.FindIndex(PortalForm.Seasons, s => s.Id == "winter"), f.SeasonBox.SelectedIndex);
            f.SeasonBox.SelectedIndex = Array.FindIndex(PortalForm.Seasons, s => s.Id == "summer");
            Assert.Equal("live", h.Config.WeatherPreview);
        });
    }

    [Fact]
    public void Sound_sliders_start_at_zero_and_write_to_the_config()
    {
        WithForm((f, h) =>
        {
            Assert.Equal(0, f.MasterSlider.Value);
            Assert.Equal(SoundEffects.Ids.OrderBy(x => x), f.EffectSliders.Keys.OrderBy(x => x));
            Assert.All(f.EffectSliders.Values, s => Assert.Equal(0, s.Value));
            f.MasterSlider.Value = 40;
            f.EffectSliders["rain"].Value = 70;
            Assert.Equal(40, h.Config.SoundMasterVolume);
            Assert.Equal(70, h.Config.SoundVolumes["rain"]);
            Assert.Equal(0, h.Config.SoundVolumes["birds"]);
        });
    }

    [Fact]
    public void Sliders_show_the_configs_saved_values()
    {
        var c = new AppConfig { SoundMasterVolume = 55 }; c.SoundVolumes["waves"] = 80;
        WithForm((f, _) =>
        {
            Assert.Equal(55, f.MasterSlider.Value);
            Assert.Equal(80, f.EffectSliders["waves"].Value);
            Assert.Equal(0, f.EffectSliders["rain"].Value);
        }, c);
    }

    [Fact]
    public void Leaving_with_nothing_unsaved_never_asks()
    {
        WithForm((f, h) =>
        {
            f.PromptUnsaved = () => throw new InvalidOperationException("should not prompt");
            Assert.True(f.ConfirmLeave());
        });
    }

    [Theory]
    [InlineData("Save", true, 1, 0)]
    [InlineData("Discard", true, 0, 1)]
    [InlineData("Cancel", false, 0, 0)]
    public void Leaving_with_unsaved_edits_follows_the_users_choice(string choiceName, bool leaves, int saves, int discards)
    {
        WithForm((f, h) =>
        {
            f.PaddingBox.Checked = false;
            var choice = Enum.Parse<UnsavedChoice>(choiceName);
            int asked = 0;
            f.PromptUnsaved = () => { asked++; return choice; };
            Assert.Equal(leaves, f.ConfirmLeave());
            Assert.Equal(1, asked);
            Assert.Equal(saves, h.Saves);
            Assert.Equal(discards, h.Discards);
            Assert.Equal(choice == UnsavedChoice.Cancel, h.HasUnsavedChanges);
            if (choice == UnsavedChoice.Discard) Assert.True(h.Config.ShowIconPadding);
        });
    }

    [Fact]
    public void Opening_every_section_never_makes_the_window_taller_than_the_work_area_and_closing_shrinks_it_back()
    {
        Sta(() =>
        {
            var host = new FakePortalHost();
            using var f = new PortalForm(host) { StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), ShowInTaskbar = false };
            f.Show();
            f.FitToScreen(reposition: false);
            int collapsed = f.Height;
            f.SettingsExpanded = true; f.SoundExpanded = true;
            f.FitToScreen(reposition: false);
            var wa = Screen.FromControl(f).WorkingArea;
            Assert.True(f.Height <= wa.Height, $"window {f.Height} vs work area {wa.Height}");
            Assert.True(f.Height >= collapsed);
            f.SettingsExpanded = false; f.SoundExpanded = false;
            f.FitToScreen(reposition: false);
            Assert.Equal(collapsed, f.Height);
            f.ForceClose();
        });
    }

    [Fact]
    public void Footer_has_close_exit_and_save_but_no_effects_toggle()
    {
        WithForm((f, h) =>
        {
            var buttons = f.AllControls().OfType<Button>().ToList();
            Assert.DoesNotContain(buttons, b => b.Text.Contains("hiệu ứng", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(buttons, b => b.Text.Contains("F8"));
            Assert.Single(buttons, b => b.Text == "Lưu cấu hình");   // one save button, no separate save row
            var footer = (TableLayoutPanel)f.SaveButton.Parent!;
            Assert.Same(footer, f.CloseButton.Parent);
            Assert.Same(footer, f.ExitButton.Parent);
            Assert.Equal(0, footer.GetColumn(f.CloseButton));
            Assert.Equal(1, footer.GetColumn(f.ExitButton));
            Assert.Equal(2, footer.GetColumn(f.SaveButton));          // bottom-right primary slot
            Assert.Equal("Đóng (Ẩn xuống khay)", f.CloseButton.Text);
            Assert.Equal("Kết thúc (Thoát)", f.ExitButton.Text);
            // save is the last row of the stack
            Assert.Same(footer, f.Content.Controls[f.Content.Controls.Count - 1]);
            // the hotkey is still announced in the status line
            Assert.Contains(f.AllTexts(), t => t.Contains("F8") && t.Contains("bật/tắt"));
        });
    }

    [Fact]
    public void Saved_hint_follows_the_unsaved_state_and_the_save_button_enables()
    {
        WithForm((f, h) =>
        {
            Assert.Contains("✓ Đã lưu", f.AllTexts());
            Assert.False(f.SaveButton.Enabled);
            f.PaddingBox.Checked = false;
            Assert.Contains("● Chưa lưu", f.AllTexts());
            Assert.DoesNotContain("✓ Đã lưu", f.AllTexts());
            Assert.True(f.SaveButton.Enabled);
        });
    }

    [Fact]
    public void Portal_info_shows_location_weather_region_and_season()
    {
        var c = new AppConfig { Season = "autumn", WeatherPreview = "rain" }; c.Normalize();
        var w = new WeatherSnapshot { Ok = true, LocationName = "Hà Nội", TemperatureC = 24.4f, WeatherCode = 61, IsDay = true };
        var info = PortalInfo.Build(w, c, RegionCatalog.All.First(r => r.Id == "hanoi"));
        Assert.Contains("Hà Nội", info.Line1);
        Assert.Contains("24", info.Line1);
        Assert.Contains("Mùa thu", info.Line2);
        Assert.Contains("Mưa", info.Line2, StringComparison.OrdinalIgnoreCase);
        // never throws without weather
        Assert.False(string.IsNullOrWhiteSpace(PortalInfo.Build(null, c, RegionCatalog.Default).Line1));
    }

    [Fact]
    public void Weather_hud_on_the_taskbar_is_gone_from_the_config_surface()
    {
        var c = new AppConfig { ShowWeatherHud = true }; c.Normalize();
        // the badge is no longer drawn: ShowWeatherHud is ignored and migrates to false
        using var d = new TempDir();
        var p = d.File("config.json");
        File.WriteAllText(p, "{ \"configVersion\": 51, \"showWeatherHud\": true }");
        Assert.False(ConfigStore.Load(p).ShowWeatherHud);
    }
}
