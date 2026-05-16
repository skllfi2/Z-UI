// DefaultStrategyConfigs.cs - Default configuration data for strategy generator
using ZUI.Models;

namespace ZUI.Services;

/// <summary>
/// Pure data factory for default strategy parameters and ISP profiles.
/// No logic, no I/O — just creates the default config objects.
/// </summary>
public static class DefaultStrategyConfigs
{
    /// <summary>
    /// Creates the default strategy parameters configuration with all DPI methods,
    /// predefined services, and binary packet definitions.
    /// </summary>
    public static StrategyParamsConfig CreateDefaultStrategyParams()
    {
        return new StrategyParamsConfig
        {
            Version = "1.0.0",
            Updated = DateTime.UtcNow.ToString("O"),
            MinAppVersion = "1.0.0",
            DpiMethods = new Dictionary<string, DpiMethod>
            {
                ["fake"] = new DpiMethod
                {
                    Id = "fake",
                    Name = "Fake Packet",
                    Description = "Подмена пакетов fake-TLS/fake-QUIC",
                    Stability = 85,
                    Compatibility = new List<string> { "passive", "active-partial" },
                    Params = new Dictionary<string, MethodParam>
                    {
                        ["repeats"] = new MethodParam { Default = 11, Range = new[] { 6, 12 } },
                        ["fooling"] = new MethodParam { Default = "badseq", Options = new List<object> { "badseq", "md5sig", "ts" } }
                    }
                },
                ["multisplit"] = new DpiMethod
                {
                    Id = "multisplit",
                    Name = "Multi Split",
                    Description = "Множественное разбиение пакетов",
                    Stability = 75,
                    Compatibility = new List<string> { "active-full" },
                    Params = new Dictionary<string, MethodParam>
                    {
                        ["splitSeqovl"] = new MethodParam { Default = 652, Options = new List<object> { 568, 652, 681 } },
                        ["splitPos"] = new MethodParam { Default = "2", Options = new List<object> { "1", "2", "1,midsld" } }
                    }
                },
                ["fakedsplit"] = new DpiMethod
                {
                    Id = "fakedsplit",
                    Name = "Fake + Split",
                    Description = "Комбинация fake и fakedsplit для сложных провайдеров",
                    Stability = 70,
                    Compatibility = new List<string> { "active-partial" },
                    Params = new Dictionary<string, MethodParam>
                    {
                        ["repeats"] = new MethodParam { Default = 6, Range = new[] { 4, 12 } },
                        ["fooling"] = new MethodParam { Default = "ts", Options = new List<object> { "ts", "badseq", "md5sig" } },
                        ["fakedsplitPattern"] = new MethodParam { Default = "0x00" }
                    }
                },
                ["hostfakesplit"] = new DpiMethod
                {
                    Id = "hostfakesplit",
                    Name = "Host Fake Split",
                    Description = "Подмена с разделением по хосту для сложных DPI",
                    Stability = 65,
                    Compatibility = new List<string> { "active-partial" },
                    Params = new Dictionary<string, MethodParam>
                    {
                        ["hostfakesplitMod"] = new MethodParam { Default = "host=www.google.com", Options = new List<object> { "host=www.google.com", "host=www.youtube.com", "altorder=1" } },
                        ["fooling"] = new MethodParam { Default = "ts", Options = new List<object> { "ts", "badseq", "md5sig" } }
                    }
                },
                ["syndata"] = new DpiMethod
                {
                    Id = "syndata",
                    Name = "SYN Data",
                    Description = "Отправка данных в SYN-пакете",
                    Stability = 60,
                    Compatibility = new List<string> { "active-full" },
                    Params = new Dictionary<string, MethodParam>
                    {
                        ["fooling"] = new MethodParam { Default = "md5sig", Options = new List<object> { "md5sig", "badseq", "ts" } },
                        ["combineMultidisorder"] = new MethodParam { Default = false, Options = new List<object> { true, false } }
                    }
                },
                ["multidisorder"] = new DpiMethod
                {
                    Id = "multidisorder",
                    Name = "Multi Disorder",
                    Description = "Множественная перестановка пакетов с нарушением порядка",
                    Stability = 70,
                    Compatibility = new List<string> { "active-full" },
                    Params = new Dictionary<string, MethodParam>
                    {
                        ["splitSeqovl"] = new MethodParam { Default = 681, Options = new List<object> { 568, 652, 681 } },
                        ["splitPos"] = new MethodParam { Default = "1", Options = new List<object> { "1", "2" } },
                        ["seqovlPattern"] = new MethodParam { Default = "tls_clienthello_www_google_com.bin" }
                    }
                },
                ["udplen"] = new DpiMethod
                {
                    Id = "udplen",
                    Name = "UDP Length",
                    Description = "Манипуляция длиной UDP-пакетов",
                    Stability = 80,
                    Compatibility = new List<string> { "passive" },
                    Params = new Dictionary<string, MethodParam>
                    {
                        ["repeats"] = new MethodParam { Default = 6, Range = new[] { 4, 12 } }
                    }
                }
            },
            Services = new Dictionary<string, ServiceConfig>
            {
                ["youtube"] = new ServiceConfig
                {
                    Id = "youtube",
                    Name = "YouTube",
                    Icon = "\uE71C",
                    Enabled = true,
                    Domains = new List<string> { "youtube.com", "youtu.be", "ytimg.com", "googlevideo.com", "ggpht.com", "yt.be", "youtube-nocookie.com" },
                    TcpPorts = new List<int> { 443 },
                    UdpPorts = new List<int> { 443 }, // QUIC
                    TestUrl = "https://www.youtube.com",
                    TestExpect = "html",
                    Category = "streaming"
                },
                ["discord"] = new ServiceConfig
                {
                    Id = "discord",
                    Name = "Discord",
                    Icon = "\uE9D9",
                    Enabled = true,
                    Domains = new List<string> { "discord.com", "discord.gg", "discord.media", "discordapp.com", "discordapp.net", "dis.gd" },
                    TcpPorts = new List<int> { 443, 2053, 2083, 2087, 2096, 8443 },
                    UdpPorts = new List<int> { 443 }, // QUIC
                    UdpPortRanges = new List<string> { "50000-65535" }, // Voice range
                    L7Filter = "discord,stun",
                    TestUrl = "https://discord.com",
                    TestExpect = "html",
                    Category = "communication",
                    VoicePorts = new VoicePortsConfig
                    {
                        Tcp = new List<int> { 2053, 2083, 2087, 2096, 8443 },
                        Udp = new List<int>() // Handled via UdpPortRanges
                    }
                },
                ["telegram"] = new ServiceConfig
                {
                    Id = "telegram",
                    Name = "Telegram",
                    Icon = "\uE776",
                    Enabled = false, // Disabled by default - IP blocked in Russia
                    Domains = new List<string> { "telegram.org", "t.me", "telegram.me", "telegra.ph", "web.telegram.org" },
                    TcpPorts = new List<int> { 443, 80, 5222 }, // MTProto ports
                    UdpPorts = new List<int>(),
                    TestUrl = "https://telegram.org",
                    TestExpect = "html",
                    Category = "communication"
                },
                ["twitch"] = new ServiceConfig
                {
                    Id = "twitch",
                    Name = "Twitch",
                    Icon = "\uE768",
                    Enabled = false,
                    Domains = new List<string> { "twitch.tv", "ttvnw.net", "twitchcdn.net", "clips.twitch.tv", "static-cdn.jtvnw.net" },
                    TcpPorts = new List<int> { 443, 80 },
                    UdpPorts = new List<int>(),
                    TestUrl = "https://www.twitch.tv",
                    TestExpect = "html",
                    Category = "streaming"
                },
                ["steam"] = new ServiceConfig
                {
                    Id = "steam",
                    Name = "Steam",
                    Icon = "\uE7FC",
                    Enabled = false,
                    Domains = new List<string> { "steamcommunity.com", "store.steampowered.com", "help.steampowered.com", "steampowered.com" },
                    TcpPorts = new List<int> { 27015, 27036 }, // Main Steam ports
                    TcpPortRanges = new List<string> { "27015-27030", "27036-27037" }, // Download/matchmaking
                    UdpPorts = new List<int> { 4380 }, // Steam P2P
                    UdpPortRanges = new List<string> { "27000-27031" }, // Game traffic
                    TestUrl = "https://store.steampowered.com",
                    TestExpect = "html",
                    Category = "gaming"
                },
                ["battlenet"] = new ServiceConfig
                {
                    Id = "battlenet",
                    Name = "Battle.net",
                    Icon = "\uE7FC",
                    Enabled = false,
                    Domains = new List<string> { "blizzard.com", "battle.net", "us.battle.net", "eu.battle.net" },
                    TcpPorts = new List<int> { 80, 443, 1119, 1120, 3724, 4000 }, // Battle.net + Voice
                    TcpPortRanges = new List<string> { "6112-6120", "27014-27050" }, // Game + Download
                    UdpPorts = new List<int> { 1119, 1120, 3724, 4000 }, // Battle.net
                    UdpPortRanges = new List<string> { "3478-3479", "6112-6119", "12000-64000" }, // Voice chat range
                    TestUrl = "https://www.blizzard.com",
                    TestExpect = "html",
                    Category = "gaming"
                },
                ["epicgames"] = new ServiceConfig
                {
                    Id = "epicgames",
                    Name = "Epic Games",
                    Icon = "\uE7FC",
                    Enabled = false,
                    Domains = new List<string> { "epicgames.com", "fortnite.com", "unrealengine.com", "launcher-website-prod07.ol.epicgames.com" },
                    TcpPorts = new List<int> { 80, 443, 5222 }, // Main + XMPP
                    TcpPortRanges = new List<string> { "12000-65000" }, // Game traffic
                    UdpPorts = new List<int> { 3478, 3479, 5060, 5062, 6250 }, // Voice
                    UdpPortRanges = new List<string> { "12000-65000" }, // Game traffic range
                    TestUrl = "https://www.epicgames.com",
                    TestExpect = "html",
                    Category = "gaming"
                },
                ["gog"] = new ServiceConfig
                {
                    Id = "gog",
                    Name = "GOG Galaxy",
                    Icon = "\uE7FC",
                    Enabled = false,
                    Domains = new List<string> { "gog.com", "www.gog.com", "embed.gog.com", "api.gog.com" },
                    TcpPorts = new List<int> { 80, 443 }, // HTTP/HTTPS only
                    UdpPorts = new List<int> { 5687 }, // GOG multiplayer
                    UdpPortRanges = new List<string> { "1024-65535" }, // P2P (random ports)
                    TestUrl = "https://www.gog.com",
                    TestExpect = "html",
                    Category = "gaming"
                },
                ["poe2"] = new ServiceConfig
                {
                    Id = "poe2",
                    Name = "Path of Exile 2",
                    Icon = "\uE7FC",
                    Enabled = false,
                    Domains = new List<string> { "pathofexile.com", "www.pathofexile.com", "login.pathofexile.com", "patch.pathofexile.com" },
                    TcpPorts = new List<int> { 443, 80, 20481 }, // HTTP + Game port
                    UdpPorts = new List<int>(), // Uses TCP mainly
                    TestUrl = "https://www.pathofexile.com",
                    TestExpect = "html",
                    Category = "gaming"
                }
            },
            BinaryPackets = new Dictionary<string, BinaryPacketConfig>
            {
                ["tls"] = new BinaryPacketConfig
                {
                    Default = "tls_clienthello_www_google_com.bin",
                    Alternatives = new List<string> { "tls_clienthello_max_ru.bin", "tls_clienthello_4pda_to.bin" }
                },
                ["quic"] = new BinaryPacketConfig
                {
                    Default = "quic_initial_www_google_com.bin",
                    Alternatives = new List<string>()
                },
                ["http"] = new BinaryPacketConfig
                {
                    Default = "tls_clienthello_max_ru.bin",
                    Alternatives = new List<string>()
                },
                ["stun"] = new BinaryPacketConfig
                {
                    Default = "stun.bin",
                    Alternatives = new List<string>()
                }
            }
        };
    }

    /// <summary>
    /// Creates the default ISP profiles configuration with Russian ISP profiles
    /// and detection rules.
    /// </summary>
    public static IspProfilesConfig CreateDefaultIspProfiles()
    {
        return new IspProfilesConfig
        {
            Version = "1.0.0",
            Updated = DateTime.UtcNow.ToString("O"),
            Profiles = new Dictionary<string, IspProfile>
            {
                ["default"] = new IspProfile
                {
                    Id = "default",
                    Name = "Универсальный",
                    Description = "Работает на большинстве провайдеров",
                    Method = "fake",
                    MethodParams = new Dictionary<string, object>
                    {
                        ["repeats"] = 11,
                        ["fooling"] = "badseq",
                        ["fakeTlsMod"] = "rnd,dupsid,sni=www.google.com"
                    },
                    Confidence = 50
                },
                ["rtk"] = new IspProfile
                {
                    Id = "rtk",
                    Name = "Ростелеком",
                    Asn = new List<string> { "12389", "25490" },
                    Method = "fake",
                    MethodParams = new Dictionary<string, object>
                    {
                        ["repeats"] = 11,
                        ["fooling"] = "badseq"
                    },
                    Confidence = 90
                },
                ["mgts"] = new IspProfile
                {
                    Id = "mgts",
                    Name = "МГТС/МТС",
                    Asn = new List<string> { "25513", "8359" },
                    Method = "fake",
                    MethodParams = new Dictionary<string, object>
                    {
                        ["repeats"] = 11,
                        ["fooling"] = "badseq"
                    },
                    Confidence = 85
                },
                ["beeline"] = new IspProfile
                {
                    Id = "beeline",
                    Name = "Билайн",
                    Asn = new List<string> { "8402", "3216" },
                    Method = "multisplit",
                    MethodParams = new Dictionary<string, object>
                    {
                        ["splitSeqovl"] = 652,
                        ["splitPos"] = "2"
                    },
                    Confidence = 95
                },
                ["megafon"] = new IspProfile
                {
                    Id = "megafon",
                    Name = "Мегафон",
                    Asn = new List<string> { "31133", "25159" },
                    Method = "fake",
                    MethodParams = new Dictionary<string, object>
                    {
                        ["repeats"] = 8,
                        ["fooling"] = "md5sig"
                    },
                    Confidence = 80
                }
            },
            DetectionRules = new List<DetectionRule>
            {
                new DetectionRule { Asn = new List<string> { "12389", "25490" }, ProfileId = "rtk" },
                new DetectionRule { Asn = new List<string> { "25513", "8359" }, ProfileId = "mgts" },
                new DetectionRule { Asn = new List<string> { "8402", "3216" }, ProfileId = "beeline" },
                new DetectionRule { Asn = new List<string> { "31133", "25159" }, ProfileId = "megafon" }
            }
        };
    }
}
