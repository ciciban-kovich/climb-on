using System.Diagnostics.Metrics;

namespace ClimbOn.Infrastructure.Email;

// Design §4.4: email_send_failed counts failed send attempts; infra/env alerts on it. No tags (C32).
public sealed class EmailMetrics
{
    public const string MeterName = "ClimbOn.Email";

    public const string SendFailedName = "email_send_failed";

    private readonly Counter<long> sendFailed;

    public EmailMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        sendFailed = meter.CreateCounter<long>(SendFailedName, description: "Failed email send attempts");
    }

    public void SendFailed() => sendFailed.Add(1);
}
