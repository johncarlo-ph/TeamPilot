namespace TeamPilot.Domain.Enums;

public enum AuditEventType
{
    LoginSucceeded,
    LoginFailed,
    Logout,
    TokenRefreshed,
    TokenReuseDetected,
}
