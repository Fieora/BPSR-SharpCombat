using System.Text.Json;
using Microsoft.JSInterop;

namespace BPSR_SharpCombat.Services;

public class WindowRecord
{
    public string? Id { get; set; }
    public string? Url { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Title { get; set; }
}

public class WindowState
{
    public Bounds? Bounds { get; set; }
    public string? MainId { get; set; }
    public List<WindowRecord>? Windows { get; set; }
}

public class Bounds { public int x; public int y; public int width; public int height; }

public class WindowManagerService
{
    private readonly IJSRuntime _js;
    private readonly ILogger<WindowManagerService> _logger;

    public WindowManagerService(IJSRuntime js, ILogger<WindowManagerService> logger)
    {
        _js = js;
        _logger = logger;
    }

    public async Task<WindowState?> GetWindowStateAsync()
    {
        try
        {
            var obj = await _js.InvokeAsync<object>("electron.getWindowState");
            if (obj == null) return null;
            var json = JsonSerializer.Serialize(obj);
            var state = JsonSerializer.Deserialize<WindowState>(json);
            return state;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get window state via electron.getWindowState");
            return null;
        }
    }

    public async Task SetWindowStateAsync(object state)
    {
        try
        {
            await _js.InvokeVoidAsync("electron.setWindowState", state);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set window state via electron.setWindowState");
        }
    }

    public async Task<bool> OpenNewWindowAsync(string url, int width = 900, int height = 700, string title = "")
    {
        try
        {
            var res = await _js.InvokeAsync<object>("electron.appControl.openNewWindow", url, new { width, height, title });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to request new window via electron.appControl.openNewWindow");
            return false;
        }
    }

    public async Task<bool> CloseWindowByIdAsync(string id)
    {
        try
        {
            var res = await _js.InvokeAsync<object>("electron.appControl.closeWindowById", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to request close window by id");
            return false;
        }
    }

    public async Task<bool> CloseCurrentWindowAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("electron.appControl.closeCurrentWindow");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to request close current window");
            return false;
        }
    }
}
