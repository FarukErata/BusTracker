namespace BusTracker.Api.Ibb;

public sealed class OutboundGate(TimeProvider clock, ILogger<OutboundGate> logger)
{
    private const int MaxConcurrent = 2;
    private const int FailuresBeforePause = 3;
    private static readonly TimeSpan MinSpacing = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SlowFailure = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FirstPause = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxPause = TimeSpan.FromMinutes(30);

    private readonly SemaphoreSlim _slots = new(MaxConcurrent, MaxConcurrent);
    private readonly SemaphoreSlim _spacing = new(1, 1);
    private readonly Lock _state = new();
    private DateTimeOffset _lastStart = DateTimeOffset.MinValue;
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;
    private TimeSpan _nextPause = FirstPause;
    private int _failures;

    public DateTimeOffset? PausedUntil
    {
        get
        {
            lock (_state) return clock.GetUtcNow() < _pausedUntil ? _pausedUntil : null;
        }
    }

    public async Task<HttpResponseMessage> SendAsync(Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        if (PausedUntil is { } until)
            throw new HttpRequestException($"Outbound calls are paused until {until:HH:mm:ss} UTC after repeated timeouts");

        await _slots.WaitAsync(ct);
        try
        {
            await WaitForTurnAsync(ct);
            var started = clock.GetUtcNow();
            try
            {
                var response = await send(ct);
                Succeeded();
                return response;
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
            {
                if (e is HttpRequestException || clock.GetUtcNow() - started >= SlowFailure) Failed();
                throw;
            }
        }
        finally
        {
            _slots.Release();
        }
    }

    private async Task WaitForTurnAsync(CancellationToken ct)
    {
        await _spacing.WaitAsync(ct);
        try
        {
            var wait = _lastStart + MinSpacing - clock.GetUtcNow();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, clock, ct);
            _lastStart = clock.GetUtcNow();
        }
        finally
        {
            _spacing.Release();
        }
    }

    private void Succeeded()
    {
        lock (_state)
        {
            _failures = 0;
            _nextPause = FirstPause;
        }
    }

    private void Failed()
    {
        lock (_state)
        {
            if (++_failures < FailuresBeforePause) return;
            _pausedUntil = clock.GetUtcNow() + _nextPause;
            logger.LogWarning("Outbound calls keep timing out; pausing them for {Pause}", _nextPause);
            _nextPause = TimeSpan.FromTicks(Math.Min(_nextPause.Ticks * 2, MaxPause.Ticks));
            _failures = 0;
        }
    }
}

public sealed class OutboundGateHandler(OutboundGate gate) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        gate.SendAsync(token => base.SendAsync(request, token), ct);
}
