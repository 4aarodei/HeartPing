using TL;
using WTelegram;
using TelegramMessage = TL.Message;

namespace HeartPing;

internal sealed class TelegramSender(Client client, TelegramOptions options)
{
    public async Task<InputPeer> ResolveTargetAsync()
    {
        if (!string.IsNullOrWhiteSpace(options.TargetUsername))
        {
            var username = options.TargetUsername.Trim().TrimStart('@');
            var resolved = await client.Contacts_ResolveUsername(username);

            if (resolved.User is not null)
            {
                return resolved.User;
            }

            if (resolved.UserOrChat is InputPeer peer)
            {
                return peer;
            }
        }

        var dialogs = await client.Messages_GetAllDialogs();
        foreach (var user in dialogs.users.Values)
        {
            if (options.TargetUserId is not null && user.id == options.TargetUserId.Value)
            {
                return user;
            }

            if (!string.IsNullOrWhiteSpace(options.TargetPhoneNumber) &&
                string.Equals(NormalizePhone(user.phone), NormalizePhone(options.TargetPhoneNumber), StringComparison.Ordinal))
            {
                return user;
            }
        }

        throw new InvalidOperationException("Target Telegram user was not found. Set TargetUsername, TargetUserId, or TargetPhoneNumber, and make sure the chat exists in your dialog list.");
    }

    public async Task<SafetyDecision> CheckSafetyAsync(InputPeer target, SafetyOptions safety, DateTimeOffset nowUtc)
    {
        if (!safety.RespectRecentOutgoingMessages)
        {
            return SafetyDecision.Allow();
        }

        var history = await client.Messages_GetHistory(target, limit: safety.RecentHistoryLimit);
        var minDateUtc = nowUtc.AddMinutes(-safety.MinMinutesBetweenSentMessages).UtcDateTime;

        foreach (var item in history.Messages)
        {
            if (item is not TelegramMessage message)
            {
                continue;
            }

            if (!message.flags.HasFlag(TelegramMessage.Flags.out_))
            {
                continue;
            }

            if (message.Date.ToUniversalTime() >= minDateUtc)
            {
                return SafetyDecision.Block($"Skipped: there is already an outgoing message in this chat within the last {safety.MinMinutesBetweenSentMessages} minutes.");
            }
        }

        return SafetyDecision.Allow();
    }

    private static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }
}
