namespace CentralChat.Infrastructure;

public sealed class JwtOptions { public const string Section = "Jwt"; public string Issuer { get; set; } = "CentralChat"; public string Audience { get; set; } = "CentralChat"; public string SigningKey { get; set; } = null!; public int AccessTokenMinutes { get; set; } = 30; public int RefreshTokenDays { get; set; } = 7; }
public sealed class RabbitMqOptions { public const string Section = "RabbitMq"; public string Host { get; set; } = "localhost"; public int Port { get; set; } = 5672; public string UserName { get; set; } = "centralchat"; public string Password { get; set; } = "centralchat_dev"; public string VirtualHost { get; set; } = "/"; }
public sealed class MetaWhatsAppOptions { public const string Section = "MetaWhatsApp"; public string VerifyToken { get; set; } = null!; public string AppSecret { get; set; } = null!; public string AccessToken { get; set; } = null!; public string ApiVersion { get; set; } = "v23.0"; public bool ValidateSignature { get; set; } = true; public bool UseDevelopmentClient { get; set; } = false; }
public sealed class BootstrapOptions
{
    public const string Section = "Bootstrap";
    public string? AdminEmail { get; set; } public string? AdminPassword { get; set; } public string AdminDisplayName { get; set; } = "Administrator"; public string AdminRole { get; set; } = "SuperAdmin";
    public string? AgentEmails { get; set; } public string? AgentPassword { get; set; } public string TeamName { get; set; } = "Support";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AdminEmail) && !string.IsNullOrWhiteSpace(AdminPassword);
    public IReadOnlyList<string> AgentEmailList => (AgentEmails ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed class RetentionOptions
{
    public const string Section = "Retention";
    public bool Enabled { get; set; } = true;
    public int OutboxDays { get; set; } = 7; public int InboxDays { get; set; } = 7; public int WebhookEventDays { get; set; } = 30;
    public int IntervalHours { get; set; } = 24; public int StartupDelayMinutes { get; set; } = 5; public int BatchSize { get; set; } = 5000;
}
