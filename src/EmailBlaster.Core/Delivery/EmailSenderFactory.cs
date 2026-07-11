using EmailBlaster.Core.Configuration;

namespace EmailBlaster.Core.Delivery;

/// <summary>Creates the appropriate <see cref="IEmailSender"/> for the configured provider.</summary>
public static class EmailSenderFactory
{
    public static IEmailSender Create(EmailBlasterConfig config) => config.Provider switch
    {
        SendProvider.Smtp => new SmtpEmailSender(config),
        SendProvider.Aws => new SesEmailSender(config),
        _ => throw new NotSupportedException($"Unknown provider '{config.Provider}'.")
    };
}
