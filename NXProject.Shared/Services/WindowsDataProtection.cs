// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NXProject.Services
{
    /// <summary>
    /// Cifra/decifra segredos no escopo do usuario atual usando DPAPI (crypt32).
    /// Usado para guardar tokens (ex.: PAT do Azure DevOps) sem dependencias externas.
    /// </summary>
    public static class WindowsDataProtection
    {
        public static string Encrypt(string value, string description = "NXProject")
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var bytes = Encoding.UTF8.GetBytes(value);
            var protectedBytes = ProtectForCurrentUser(bytes, description);
            return Convert.ToBase64String(protectedBytes);
        }

        /// <summary>
        /// Decifra com entropia adicional (a chave do par que cifrou). Usado pela NxAgentApi: o
        /// NXPilot entrega o PAT cifrado assim — ele não passa em texto por parâmetro nem por log.
        /// Vazio quando não decifra (outro usuário do Windows, outra chave, lixo).
        /// </summary>
        public static string DecryptWithEntropy(string encryptedValue, byte[] entropy)
        {
            if (string.IsNullOrWhiteSpace(encryptedValue)) return string.Empty;
            try { return Encoding.UTF8.GetString(UnprotectForCurrentUser(Convert.FromBase64String(encryptedValue), entropy)); }
            catch { return string.Empty; }
        }

        /// <summary>Cifra com entropia adicional (par de <see cref="DecryptWithEntropy"/>).</summary>
        public static string EncryptWithEntropy(string value, byte[] entropy, string description = "NXProject")
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return Convert.ToBase64String(ProtectForCurrentUser(Encoding.UTF8.GetBytes(value), description, entropy));
        }

        public static string Decrypt(string encryptedValue)
        {
            if (string.IsNullOrWhiteSpace(encryptedValue))
                return string.Empty;

            try
            {
                var protectedBytes = Convert.FromBase64String(encryptedValue);
                var bytes = UnprotectForCurrentUser(protectedBytes);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static byte[] ProtectForCurrentUser(byte[] plainBytes, string description, byte[]? entropy = null)
        {
            var input = CreateBlob(plainBytes);
            DATA_BLOB output = default;
            var extra = entropy is { Length: > 0 } ? CreateBlob(entropy) : default;
            var extraPtr = IntPtr.Zero;

            try
            {
                if (entropy is { Length: > 0 }) { extraPtr = Marshal.AllocHGlobal(Marshal.SizeOf<DATA_BLOB>()); Marshal.StructureToPtr(extra, extraPtr, false); }
                if (!CryptProtectData(ref input, description, extraPtr, IntPtr.Zero, IntPtr.Zero, 0, ref output))
                    throw new InvalidOperationException("Nao foi possivel criptografar o token localmente.");

                return CopyBlob(output);
            }
            finally
            {
                FreeBlob(input);
                FreeProtectedBlob(output);
                FreeBlob(extra);
                if (extraPtr != IntPtr.Zero) Marshal.FreeHGlobal(extraPtr);
            }
        }

        private static byte[] UnprotectForCurrentUser(byte[] protectedBytes, byte[]? entropy = null)
        {
            var input = CreateBlob(protectedBytes);
            DATA_BLOB output = default;
            var extra = entropy is { Length: > 0 } ? CreateBlob(entropy) : default;
            var extraPtr = IntPtr.Zero;

            try
            {
                if (entropy is { Length: > 0 }) { extraPtr = Marshal.AllocHGlobal(Marshal.SizeOf<DATA_BLOB>()); Marshal.StructureToPtr(extra, extraPtr, false); }
                if (!CryptUnprotectData(ref input, IntPtr.Zero, extraPtr, IntPtr.Zero, IntPtr.Zero, 0, ref output))
                    throw new InvalidOperationException("Nao foi possivel descriptografar o token localmente.");

                return CopyBlob(output);
            }
            finally
            {
                FreeBlob(input);
                FreeProtectedBlob(output);
                FreeBlob(extra);
                if (extraPtr != IntPtr.Zero) Marshal.FreeHGlobal(extraPtr);
            }
        }

        private static DATA_BLOB CreateBlob(byte[] bytes)
        {
            var blob = new DATA_BLOB
            {
                cbData = bytes.Length,
                pbData = Marshal.AllocHGlobal(bytes.Length)
            };

            Marshal.Copy(bytes, 0, blob.pbData, bytes.Length);
            return blob;
        }

        private static byte[] CopyBlob(DATA_BLOB blob)
        {
            if (blob.pbData == IntPtr.Zero || blob.cbData <= 0)
                return Array.Empty<byte>();

            var bytes = new byte[blob.cbData];
            Marshal.Copy(blob.pbData, bytes, 0, blob.cbData);
            return bytes;
        }

        private static void FreeBlob(DATA_BLOB blob)
        {
            if (blob.pbData != IntPtr.Zero)
                Marshal.FreeHGlobal(blob.pbData);
        }

        private static void FreeProtectedBlob(DATA_BLOB blob)
        {
            if (blob.pbData != IntPtr.Zero)
                LocalFree(blob.pbData);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(
            ref DATA_BLOB pDataIn,
            string szDataDescr,
            IntPtr pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            ref DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(
            ref DATA_BLOB pDataIn,
            IntPtr ppszDataDescr,
            IntPtr pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            ref DATA_BLOB pDataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}
