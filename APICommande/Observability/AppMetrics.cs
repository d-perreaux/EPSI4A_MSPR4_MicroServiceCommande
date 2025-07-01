using System.Diagnostics.Metrics;

namespace APIOrder.Utilitaires
{
    public static class AppMetrics
    {
        public readonly static Meter Meter = new("APIMetrics.Meter", "1.0");
        private readonly static string CountType = "count";

        public readonly static Counter<long> RequestCounter = Meter.CreateCounter<long>("http_request_total", CountType, "Total number of incoming request");
        public readonly static Counter<long> HttpResponseCounter = Meter.CreateCounter<long>("http_response_total", CountType, "Total number of outgoing request");
         public readonly static Counter<long> AppErrorCounter = Meter.CreateCounter<long>("app_error_total", CountType, "Total number of application errors.");
        public readonly static Counter<long> DatabaseErrorCounter = Meter.CreateCounter<long>("db_error_total", CountType, "Total number of database errors.");
        public readonly static Counter<long> RabbitMQErrorCounter = Meter.CreateCounter<long>("rabbitmq_error_total", CountType, "Total number of RabbitMQ errors.");
        public readonly static UpDownCounter<int> ActiveOrders = Meter.CreateUpDownCounter<int>("active_orders");
        public readonly static Histogram<double> ResponseTimes = Meter.CreateHistogram<double>("response_time_ms");

        public readonly static ObservableGauge<double> MemoryGauge = Meter.CreateObservableGauge("app_memory_used_mb", () =>
        {
            long bytesUsed = GC.GetTotalMemory(false);
            return new Measurement<double>(bytesUsed / 1024.0 / 1024.0);
        }, description: "Memory used (MB)");
    }
}
