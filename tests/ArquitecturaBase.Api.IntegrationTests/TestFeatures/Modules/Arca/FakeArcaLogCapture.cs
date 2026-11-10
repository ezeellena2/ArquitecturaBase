using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;

/// <summary>
/// Guarda cada log de la Api con su excepción, para buscar ahí la clave fiscal que manejó el robot (ADR 0026). Es uno por
/// factory; cada test busca sólo su clave, que es única.
/// </summary>
internal sealed class FakeArcaLogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> entries = new();

    public bool Contains(string text) => entries.Any(entry => entry.Contains(text, StringComparison.Ordinal));

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, entries);

    public void Dispose() { }

    private sealed class Logger(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (entries.Count > 20_000) entries.TryDequeue(out _);
            entries.Enqueue($"{category}: {formatter(state, exception)} {exception}");
        }
    }
}
