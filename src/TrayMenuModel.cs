namespace AudioTaskbar;

/// <summary>
/// v5.4: the structure of the tray right-click menu, separate from WinForms so it can be unit-tested.
/// "Bật/tắt hiệu ứng (F8)" and "Hiện thông tin thời tiết & địa điểm" are gone (the F8 hotkey still works; weather and
/// location info live in the control panel), and the two update commands are one: "Kiểm tra & cập nhật".
/// </summary>
internal static class TrayMenuModel
{
    public const string Open = "open", Scene = "scene", Time = "time", Season = "season", Region = "region", Preview = "preview",
        Refresh = "refresh", Reload = "reload", Update = "update", Exit = "exit", Sep = "-";

    public const string UpdateText = "Kiểm tra & cập nhật";

    public sealed record Entry(string Id, string Text);

    public static IReadOnlyList<Entry> TopLevel(bool storeUpdates)
    {
        var list = new List<Entry>
        {
            new(Open, "Mở bảng điều khiển"),
            new(Sep, ""),
            new(Scene, "Hoạt ảnh"),
            new(Time, "Ngày & đêm (mặt trời / mặt trăng)"),
            new(Season, "Mùa"),
            new(Region, "Khu vực & Cảnh vật"),
            new(Preview, "Xem trước thời tiết"),
            new(Refresh, "Làm mới thời tiết & vị trí"),
            new(Sep, ""),
            new(Reload, "Tải lại cấu hình"),
        };
        // Microsoft Store builds are updated by the Store, never from GitHub.
        if (!storeUpdates) list.Add(new Entry(Update, UpdateText));
        list.Add(new Entry(Sep, ""));
        list.Add(new Entry(Exit, "Kết thúc (Thoát)"));
        return list;
    }

    public static IReadOnlyList<(string Id, string Label)> Seasons => new[]
    {
        ("spring", "Mùa xuân"), ("summer", "Mùa hè"), ("autumn", "Mùa thu"), ("winter", "Mùa đông")
    };

    public static IReadOnlyList<(string Id, string Label)> Weathers => new[]
    {
        ("live", "Thực tế"), ("sunny", "Nắng đẹp"), ("cloudy", "Mây trôi"), ("rain", "Mưa rào"),
        ("storm", "Giông bão"), ("snow", "Tuyết rơi"), ("night", "Đêm sao")
    };

    public static IReadOnlyList<(string Id, string Label)> Times => new[]
    {
        ("cycle", "Theo chu kỳ tùy chỉnh"), ("real", "Theo giờ thật"), ("day", "Cố định ban ngày"), ("night", "Cố định ban đêm")
    };

    public const string IconPaddingText = "Vùng đệm xám quanh icon";
}
