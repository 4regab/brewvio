using System.Text.Json.Serialization;

namespace Brewvio.Dtos;

// Store configuration. TaxRatePercent is applied to (subtotal - discount) at checkout.
public record StoreSettingsDto(string StoreName, string Address, string Currency, [property: JsonRequired] decimal TaxRatePercent);
