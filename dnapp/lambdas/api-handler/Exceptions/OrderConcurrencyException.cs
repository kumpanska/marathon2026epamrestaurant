using System;

namespace APIHandler.Exceptions;

public sealed class OrderConcurrencyException : Exception
{
    public OrderConcurrencyException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
