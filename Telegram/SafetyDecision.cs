namespace HeartPing;

internal sealed record SafetyDecision(bool CanSend, string Reason)
{
    public static SafetyDecision Allow() => new(true, "Allowed.");
    public static SafetyDecision Block(string reason) => new(false, reason);
}
