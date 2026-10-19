using BusTracker.Api.Lines;
using BusTracker.Api.Tracking;
using Microsoft.Extensions.DependencyInjection;

namespace BusTracker.Tests.Lines;

public sealed class EtaAccuracyTests
{
    [Fact]
    public async Task Crossing_ScoresTheSavedPredictionsOfBothModels()
    {
        using var factory = new TestAppFactory();
        factory.CreateClient();
        var accuracy = factory.Services.GetRequiredService<EtaAccuracy>();
        var at = factory.Clock.GetUtcNow();

        accuracy.Predicted(new EtaPrediction("T_G_D0", "B1", 5, at, SpeedSeconds: 300, HopSeconds: 240, DistanceMeters: 1200, AgeSeconds: 20));
        accuracy.Predicted(new EtaPrediction("T_G_D0", "B1", 5, at.AddSeconds(10), 290, 230, 1150, 20));
        accuracy.Predicted(new EtaPrediction("T_G_D0", "B2", 5, at, 100, 100, 400, 20));
        accuracy.Crossed(new StopCrossing("T_G_D0", "B1", 5, at.AddSeconds(270)));

        var summary = await WaitForAsync(accuracy, 1);

        var bucket = Assert.Single(summary.ByDistance);
        Assert.Equal("3–7 min", bucket.Label);
        Assert.Equal(1, bucket.Speed.Count);
        Assert.Equal(30, bucket.Speed.BiasSeconds);
        Assert.Equal(-30, bucket.Hops.BiasSeconds);
        Assert.Equal(1, bucket.Hops.WithinOneMinute);
    }

    private static async Task<EtaSummary> WaitForAsync(EtaAccuracy accuracy, int count)
    {
        for (var i = 0; i < 100; i++)
        {
            var summary = await accuracy.SummaryAsync(7, CancellationToken.None);
            if (summary.Count >= count) return summary;
            await Task.Delay(50);
        }
        return await accuracy.SummaryAsync(7, CancellationToken.None);
    }
}
