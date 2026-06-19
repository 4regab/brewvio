namespace Brewvio.Services;

// Raised when an order can't be fulfilled because one or more recipe ingredients
// lack the stock to cover it. Derives from InvalidOperationException so the global
// error middleware surfaces it as a clean HTTP 400 with the message intact.
public class InsufficientStockException : InvalidOperationException
{
    // Creates the exception with a message describing which ingredient(s) ran short.
    // message: the human-readable shortage description.
    public InsufficientStockException(string message) : base(message) { }
}
