namespace Brewvio.Models;

// Key/value store for configurable settings (tax rate, store name/address, currency, etc.).
public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
