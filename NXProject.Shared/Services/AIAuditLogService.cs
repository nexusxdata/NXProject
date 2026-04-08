using System;
using System.Globalization;
using System.IO;

namespace NXProject.Services
{
    public static class AIAuditLogService
    {
        public static void RegisterUserAcknowledgement(string storageKey, string reason, string prompt)
        {
            if (string.IsNullOrWhiteSpace(storageKey))
                throw new ArgumentException("Storage key obrigatoria.", nameof(storageKey));

            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                storageKey);

            Directory.CreateDirectory(directory);

            var logPath = Path.Combine(directory, "ai-awareness.log");
            var entry = string.Format(
                CultureInfo.InvariantCulture,
                "{0:O} | usuario ciente | motivo={1} | tamanho_prompt={2}{3}",
                DateTimeOffset.Now,
                Sanitize(reason),
                prompt?.Trim().Length ?? 0,
                Environment.NewLine);

            File.AppendAllText(logPath, entry);
        }

        private static string Sanitize(string value)
        {
            return (value ?? string.Empty)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();
        }
    }
}
