using System;

namespace Miao.Core.Services
{
    public sealed class FanqieClientConfig
    {
        public string Aid { get; init; } = "2329";
        public string VersionCode { get; init; } = "999";
        public string InstallId { get; init; } = "";
        public string ServerDeviceId { get; init; } = "";
        public string RegKey { get; init; } = "";

        public static FanqieClientConfig FromEnvironment()
        {
            return new FanqieClientConfig
            {
                Aid = GetEnvironment(
                    "MIAO_FANQIE_AID",
                    "2329"),

                VersionCode = GetEnvironment(
                    "MIAO_FANQIE_VERSION_CODE",
                    "999"),

                InstallId = GetEnvironment(
                    "MIAO_FANQIE_INSTALL_ID"),

                ServerDeviceId = GetEnvironment(
                    "MIAO_FANQIE_SERVER_DEVICE_ID"),

                RegKey = GetEnvironment(
                    "MIAO_FANQIE_REG_KEY")
            };
        }

        private static string GetEnvironment(
            string name,
            string defaultValue = "")
        {
            return Environment.GetEnvironmentVariable(name)
                   ?? defaultValue;
        }
    }
}