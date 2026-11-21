namespace SmartBowl.Contracts;

public sealed record BowlNotificationDto(Guid Id, Guid DeviceId, string DeviceName, DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt, decimal WeightGrams);
public sealed record PushKeysDto(string P256dh, string Auth);
public sealed record PushSubscriptionRequest(string Endpoint, PushKeysDto Keys);
public sealed record RemovePushSubscriptionRequest(string Endpoint);
public sealed record PushConfigurationDto(string PublicKey, bool Configured);
