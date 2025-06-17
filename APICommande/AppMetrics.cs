using System.Diagnostics.Metrics;

namespace APIOrder.Utilitaires
{
    public static class AppMetrics
    {
        public readonly static Meter Meter = new("APIMetrics.Meter", "1.0");

        public readonly static Counter<long> RequestCounter = Meter.CreateCounter<long>("request_count");
        public readonly static UpDownCounter<int> ActiveOrders = Meter.CreateUpDownCounter<int>("active_orders");
        public readonly static Histogram<double> ResponseTimes = Meter.CreateHistogram<double>("response_time_ms");

        public readonly static ObservableGauge<double> MemoryGauge = Meter.CreateObservableGauge("app_memory_used_mb", () =>
        {
            long bytesUsed = GC.GetTotalMemory(false);
            return new Measurement<double>(bytesUsed / 1024.0 / 1024.0);
        }, description: "Memory used (MB)");
    }
}
