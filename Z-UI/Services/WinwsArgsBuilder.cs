// WinwsArgsBuilder.cs - Static helper for building winws.exe command-line arguments
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZUI.Models;

namespace ZUI.Services;

/// <summary>
/// Static utility for building winws.exe command-line arguments from service configs
/// and ISP profiles. Used by both StrategyGeneratorService and GeneratorViewModel.
/// </summary>
public static class WinwsArgsBuilder
{
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>
    /// Builds a winws CLI argument string for the specified DPI method and parameters.
    /// Used by the UI to preview what arguments will be passed to winws.exe.
    /// </summary>
    /// <param name="methodId">DPI method identifier (e.g. "fake", "multisplit")</param>
    /// <param name="fooling">Fooling mode (e.g. "badseq", "md5sig")</param>
    /// <param name="fakeRepeats">Number of fake packet repeats</param>
    /// <param name="splitPos">Split position (string to support values like "1,midsld")</param>
    /// <param name="splitSeqovl">Split sequence overlap value</param>
    /// <param name="fakedsplitPattern">Fakedsplit pattern (e.g. "0x00")</param>
    /// <param name="hostfakesplitMod">Hostfakesplit modifier (e.g. "host=www.google.com")</param>
    /// <param name="combineMultidisorder">Whether to combine syndata with multidisorder</param>
    /// <returns>CLI argument string, or empty string if method is unknown</returns>
    public static string BuildMethodPreview(
        string methodId,
        string fooling,
        int fakeRepeats,
        string splitPos,
        int splitSeqovl,
        string fakedsplitPattern,
        string hostfakesplitMod,
        bool combineMultidisorder)
    {
        var sb = new StringBuilder();

        switch (methodId)
        {
            case "fake":
                sb.Append("--dpi-desync=fake ");
                sb.Append($"--dpi-desync-repeats={fakeRepeats} ");
                sb.Append($"--dpi-desync-fooling={fooling} ");
                sb.Append("--dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com ");
                break;
            case "multisplit":
                sb.Append("--dpi-desync=multisplit ");
                sb.Append($"--dpi-desync-split-seqovl={splitSeqovl} ");
                sb.Append($"--dpi-desync-split-pos={splitPos} ");
                break;
            case "fakedsplit":
                sb.Append("--dpi-desync=fake,fakedsplit ");
                sb.Append($"--dpi-desync-repeats={fakeRepeats} ");
                sb.Append($"--dpi-desync-fooling={fooling} ");
                sb.Append($"--dpi-desync-fakedsplit-pattern={fakedsplitPattern} ");
                break;
            case "hostfakesplit":
                sb.Append("--dpi-desync=hostfakesplit ");
                sb.Append($"--dpi-desync-hostfakesplit-mod={hostfakesplitMod} ");
                sb.Append($"--dpi-desync-fooling={fooling} ");
                break;
            case "syndata":
                sb.Append(combineMultidisorder ? "--dpi-desync=syndata,multidisorder " : "--dpi-desync=syndata ");
                sb.Append($"--dpi-desync-fooling={fooling} ");
                if (combineMultidisorder)
                {
                    sb.Append($"--dpi-desync-split-seqovl={splitSeqovl} ");
                    sb.Append($"--dpi-desync-split-pos={splitPos} ");
                }
                break;
            case "multidisorder":
                sb.Append("--dpi-desync=multidisorder ");
                sb.Append($"--dpi-desync-split-seqovl={splitSeqovl} ");
                sb.Append($"--dpi-desync-split-pos={splitPos} ");
                break;
            case "udplen":
                sb.Append("--dpi-desync=udplen ");
                if (fakeRepeats > 0)
                    sb.Append($"--dpi-desync-repeats={fakeRepeats} ");
                break;
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Build complete winws.exe argument string from services, profile, and parameters.
    /// </summary>
    public static string BuildWinwsArgs(
        IReadOnlyList<ServiceConfig> services,
        IspProfile profile,
        StrategyParamsConfig parameters,
        string zapretDir,
        ILogger? logger = null,
        List<string>? customDomains = null,
        List<CustomServiceConfig>? customServices = null)
    {
        var sb = new StringBuilder();
        var binPath = GetBinPath(zapretDir);
        var listsPath = GetListsPath(zapretDir);

        // Collect all TCP ports and ranges from predefined services
        var allTcpPorts = services.SelectMany(s => s.TcpPorts).Distinct().OrderBy(p => p).ToList();
        var allTcpRanges = services
            .Where(s => s.TcpPortRanges != null)
            .SelectMany(s => s.TcpPortRanges!)
            .ToList();

        // Collect all UDP ports and ranges from predefined services
        var allUdpPorts = services.SelectMany(s => s.UdpPorts).Distinct().OrderBy(p => p).ToList();
        var allUdpRanges = services
            .Where(s => s.UdpPortRanges != null)
            .SelectMany(s => s.UdpPortRanges!)
            .ToList();

        // Add custom services ports
        if (customServices != null)
        {
            allTcpPorts.AddRange(customServices.SelectMany(s => s.TcpPorts));
            allUdpPorts.AddRange(customServices.SelectMany(s => s.UdpPorts));
            allTcpPorts = allTcpPorts.Distinct().OrderBy(p => p).ToList();
            allUdpPorts = allUdpPorts.Distinct().OrderBy(p => p).ToList();
        }

        // Custom domains - use port 443 for TCP and UDP
        var hasCustomDomains = (customDomains != null && customDomains.Count > 0) ||
            (customServices != null && customServices.Count > 0);

        if (hasCustomDomains)
        {
            if (!allTcpPorts.Contains(443))
                allTcpPorts.Add(443);
            if (!allUdpPorts.Contains(443))
                allUdpPorts.Add(443);
        }

        // WinDivert filters - TCP (ports + ranges)
        var tcpFilterParts = new List<string>();
        if (allTcpPorts.Count > 0)
        {
            tcpFilterParts.AddRange(allTcpPorts.Select(p => p.ToString()));
        }
        tcpFilterParts.AddRange(allTcpRanges);
        if (tcpFilterParts.Count > 0)
        {
            sb.Append($"--wf-tcp={string.Join(",", tcpFilterParts)} ");
        }

        // WinDivert filters - UDP (ports + ranges)
        var udpFilterParts = new List<string>();
        if (allUdpPorts.Count > 0)
        {
            udpFilterParts.AddRange(allUdpPorts.Select(p => p.ToString()));
        }
        udpFilterParts.AddRange(allUdpRanges);
        if (udpFilterParts.Count > 0)
        {
            sb.Append($"--wf-udp={string.Join(",", udpFilterParts)} ");
        }

        // Build filters per service
        foreach (var service in services)
        {
            // UDP filter for QUIC
            if (service.UdpPorts.Count > 0)
            {
                sb.Append("--new ");
                sb.Append($"--filter-udp={string.Join(",", service.UdpPorts)} ");

                if (!string.IsNullOrEmpty(service.L7Filter))
                {
                    sb.Append($"--filter-l7={service.L7Filter} ");
                }

                // Generate hostlist for UDP
                if (service.Domains.Count > 0)
                {
                    var hostlistPath = GenerateHostlist(service.Domains, service.Id, logger);
                    sb.Append($"--hostlist=\"{hostlistPath}\" ");
                }

                AppendMethodParams(sb, profile, "udp", binPath);
            }

            // UDP range filter (e.g., Discord voice)
            if (service.UdpPortRanges != null && service.UdpPortRanges.Count > 0)
            {
                sb.Append("--new ");
                sb.Append($"--filter-udp={string.Join(",", service.UdpPortRanges)} ");

                if (!string.IsNullOrEmpty(service.L7Filter))
                {
                    sb.Append($"--filter-l7={service.L7Filter} ");
                }

                AppendMethodParams(sb, profile, "udp", binPath);
            }

            // TCP filter
            if (service.TcpPorts.Count > 0)
            {
                sb.Append("--new ");
                sb.Append($"--filter-tcp={string.Join(",", service.TcpPorts)} ");

                // Add hostlist for domain-based filtering
                if (service.Domains.Count > 0)
                {
                    var hostlistPath = GenerateHostlist(service.Domains, service.Id, logger);
                    sb.Append($"--hostlist=\"{hostlistPath}\" ");
                }

                // Add exclude lists
                var excludePath = Path.Combine(listsPath, "list-exclude.txt");
                if (File.Exists(excludePath))
                {
                    sb.Append($"--hostlist-exclude=\"{excludePath}\" ");
                }

                var ipsetExcludePath = Path.Combine(listsPath, "ipset-exclude.txt");
                if (File.Exists(ipsetExcludePath))
                {
                    sb.Append($"--ipset-exclude=\"{ipsetExcludePath}\" ");
                }

                AppendMethodParams(sb, profile, "tcp", binPath);
            }

            // TCP range filter (e.g., Steam)
            if (service.TcpPortRanges != null && service.TcpPortRanges.Count > 0)
            {
                sb.Append("--new ");
                sb.Append($"--filter-tcp={string.Join(",", service.TcpPortRanges)} ");

                // Add hostlist for domain-based filtering
                if (service.Domains.Count > 0)
                {
                    var hostlistPath = GenerateHostlist(service.Domains, service.Id, logger);
                    sb.Append($"--hostlist=\"{hostlistPath}\" ");
                }

                AppendMethodParams(sb, profile, "tcp", binPath);
            }
        }

        // Custom domains filter (port 443 only)
        if (customDomains != null && customDomains.Count > 0)
        {
            // UDP filter for custom domains (QUIC)
            sb.Append("--new ");
            sb.Append("--filter-udp=443 ");
            var customHostlistPath = GenerateHostlist(customDomains, "custom-domains", logger);
            sb.Append($"--hostlist=\"{customHostlistPath}\" ");
            AppendMethodParams(sb, profile, "udp", binPath);

            // TCP filter for custom domains
            sb.Append("--new ");
            sb.Append("--filter-tcp=443 ");
            sb.Append($"--hostlist=\"{customHostlistPath}\" ");

            // Add exclude lists
            var excludePath = Path.Combine(listsPath, "list-exclude.txt");
            if (File.Exists(excludePath))
            {
                sb.Append($"--hostlist-exclude=\"{excludePath}\" ");
            }

            AppendMethodParams(sb, profile, "tcp", binPath);
        }

        // Custom services filters
        if (customServices != null)
        {
            foreach (var customService in customServices)
            {
                // UDP filter
                if (customService.UdpPorts.Count > 0)
                {
                    sb.Append("--new ");
                    sb.Append($"--filter-udp={string.Join(",", customService.UdpPorts)} ");

                    if (customService.Domains.Count > 0)
                    {
                        var hostlistPath = GenerateHostlist(customService.Domains, customService.Id, logger);
                        sb.Append($"--hostlist=\"{hostlistPath}\" ");
                    }

                    AppendMethodParams(sb, profile, "udp", binPath);
                }

                // TCP filter
                if (customService.TcpPorts.Count > 0)
                {
                    sb.Append("--new ");
                    sb.Append($"--filter-tcp={string.Join(",", customService.TcpPorts)} ");

                    if (customService.Domains.Count > 0)
                    {
                        var hostlistPath = GenerateHostlist(customService.Domains, customService.Id, logger);
                        sb.Append($"--hostlist=\"{hostlistPath}\" ");
                    }

                    AppendMethodParams(sb, profile, "tcp", binPath);
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Append DPI desync method parameters to the argument string builder.
    /// </summary>
    public static void AppendMethodParams(StringBuilder sb, IspProfile profile, string protocol, string binPath)
    {
        var method = profile.Method;
        var @params = profile.MethodParams;

        switch (method)
        {
            case "fake":
                sb.Append("--dpi-desync=fake ");

                if (@params.TryGetValue("repeats", out var repeats))
                {
                    sb.Append($"--dpi-desync-repeats={UnwrapValue(repeats)} ");
                }
                else
                {
                    sb.Append("--dpi-desync-repeats=11 ");
                }

                if (@params.TryGetValue("fooling", out var fooling))
                {
                    sb.Append($"--dpi-desync-fooling={UnwrapValue(fooling)} ");
                }
                else
                {
                    sb.Append("--dpi-desync-fooling=badseq ");
                }

                if (protocol == "udp")
                {
                    var quicBin = Path.Combine(binPath, "quic_initial_www_google_com.bin");
                    if (File.Exists(quicBin))
                    {
                        sb.Append($"--dpi-desync-fake-quic=\"{quicBin}\" ");
                    }
                }
                else
                {
                    sb.Append("--dpi-desync-fake-tls=0x00000000 --dpi-desync-fake-tls=! ");

                    if (@params.TryGetValue("fakeTlsMod", out var mod))
                    {
                        sb.Append($"--dpi-desync-fake-tls-mod={UnwrapValue(mod)} ");
                    }
                    else
                    {
                        sb.Append("--dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com ");
                    }
                }
                break;

            case "multisplit":
                sb.Append("--dpi-desync=multisplit ");

                if (@params.TryGetValue("splitSeqovl", out var seqovl))
                {
                    sb.Append($"--dpi-desync-split-seqovl={UnwrapValue(seqovl)} ");
                }
                else
                {
                    sb.Append("--dpi-desync-split-seqovl=652 ");
                }

                if (@params.TryGetValue("splitPos", out var pos))
                {
                    sb.Append($"--dpi-desync-split-pos={UnwrapValue(pos)} ");
                }
                else
                {
                    sb.Append("--dpi-desync-split-pos=2 ");
                }

                var tlsBin = Path.Combine(binPath, "tls_clienthello_www_google_com.bin");
                if (File.Exists(tlsBin))
                {
                    sb.Append($"--dpi-desync-split-seqovl-pattern=\"{tlsBin}\" ");
                }
                break;

            case "fakedsplit":
                sb.Append("--dpi-desync=fake,fakedsplit ");

                if (@params.TryGetValue("repeats", out var fdrp))
                {
                    sb.Append($"--dpi-desync-repeats={UnwrapValue(fdrp)} ");
                }
                else
                {
                    sb.Append("--dpi-desync-repeats=6 ");
                }

                if (@params.TryGetValue("fooling", out var fdfool))
                {
                    sb.Append($"--dpi-desync-fooling={UnwrapValue(fdfool)} ");
                }
                else
                {
                    sb.Append("--dpi-desync-fooling=ts ");
                }

                if (@params.TryGetValue("fakedsplitPattern", out var pattern))
                {
                    sb.Append($"--dpi-desync-fakedsplit-pattern={UnwrapValue(pattern)} ");
                }
                else
                {
                    sb.Append("--dpi-desync-fakedsplit-pattern=0x00 ");
                }
                break;

            case "hostfakesplit":
                sb.Append("--dpi-desync=hostfakesplit ");

                if (@params.TryGetValue("hostfakesplitMod", out var hfMod))
                {
                    sb.Append($"--dpi-desync-hostfakesplit-mod={UnwrapValue(hfMod)} ");
                }
                else
                {
                    sb.Append("--dpi-desync-hostfakesplit-mod=host=www.google.com ");
                }

                if (@params.TryGetValue("fooling", out var hfFooling))
                {
                    sb.Append($"--dpi-desync-fooling={UnwrapValue(hfFooling)} ");
                }
                else
                {
                    sb.Append("--dpi-desync-fooling=ts ");
                }
                break;

            case "syndata":
                if (@params.TryGetValue("combineMultidisorder", out var cmd) && UnwrapValue(cmd) == "true")
                {
                    sb.Append("--dpi-desync=syndata,multidisorder ");
                }
                else
                {
                    sb.Append("--dpi-desync=syndata ");
                }

                if (@params.TryGetValue("fooling", out var synFooling))
                {
                    sb.Append($"--dpi-desync-fooling={UnwrapValue(synFooling)} ");
                }
                else
                {
                    sb.Append("--dpi-desync-fooling=md5sig ");
                }
                break;

            case "multidisorder":
                sb.Append("--dpi-desync=multidisorder ");

                if (@params.TryGetValue("splitSeqovl", out var mdSeqovl))
                {
                    sb.Append($"--dpi-desync-split-seqovl={UnwrapValue(mdSeqovl)} ");
                }
                else
                {
                    sb.Append("--dpi-desync-split-seqovl=681 ");
                }

                if (@params.TryGetValue("splitPos", out var mdPos))
                {
                    sb.Append($"--dpi-desync-split-pos={UnwrapValue(mdPos)} ");
                }
                else
                {
                    sb.Append("--dpi-desync-split-pos=1 ");
                }

                if (@params.TryGetValue("seqovlPattern", out var mdPattern))
                {
                    sb.Append($"--dpi-desync-split-seqovl-pattern=\"{Path.Combine(binPath, UnwrapValue(mdPattern))}\" ");
                }
                else
                {
                    var mdTlsBin = Path.Combine(binPath, "tls_clienthello_www_google_com.bin");
                    if (File.Exists(mdTlsBin))
                    {
                        sb.Append($"--dpi-desync-split-seqovl-pattern=\"{mdTlsBin}\" ");
                    }
                }
                break;

            case "udplen":
                sb.Append("--dpi-desync=udplen ");

                if (@params.TryGetValue("repeats", out var udpRepeats))
                {
                    sb.Append($"--dpi-desync-repeats={UnwrapValue(udpRepeats)} ");
                }
                break;

            default:
                // Default to fake
                sb.Append("--dpi-desync=fake --dpi-desync-repeats=11 --dpi-desync-fooling=badseq ");
                break;
        }
    }

    /// <summary>
    /// Generate a hostlist file for a service's domains and return its path.
    /// </summary>
    public static string GenerateHostlist(IReadOnlyList<string> domains, string serviceId, ILogger? logger = null)
    {
        var cacheDir = Path.Combine(LocalAppData, "Z-UI", "cache", "hostlists");
        if (!Directory.Exists(cacheDir))
        {
            Directory.CreateDirectory(cacheDir);
        }

        var hostlistPath = Path.Combine(cacheDir, $"{serviceId}.txt");
        File.WriteAllLines(hostlistPath, domains);

        logger?.LogDebug("Generated hostlist: {Path}", hostlistPath);
        return hostlistPath;
    }

    /// <summary>
    /// Find the binary packet directory within the zapret folder.
    /// </summary>
    public static string GetBinPath(string zapretDir)
    {
        var candidates = new[]
        {
            Path.Combine(zapretDir, "bin"),
            zapretDir,
        };

        foreach (var dir in candidates)
        {
            if (File.Exists(Path.Combine(dir, "tls_clienthello_www_google_com.bin")))
                return dir;
        }

        return zapretDir;
    }

    /// <summary>
    /// Find the lists directory within the zapret folder.
    /// </summary>
    public static string GetListsPath(string zapretDir)
    {
        var candidates = new[]
        {
            Path.Combine(zapretDir, "lists"),
        };

        foreach (var dir in candidates)
        {
            if (File.Exists(Path.Combine(dir, "list-google.txt")))
                return dir;
        }

        return Path.Combine(zapretDir, "lists");
    }

    /// <summary>
    /// Unwraps a JsonElement to its string representation for CLI arguments.
    /// JsonElement.ToString() returns JSON-format strings (with quotes);
    /// this method returns the actual value without JSON encoding.
    /// </summary>
    public static string UnwrapValue(object? value)
    {
        if (value is null) return string.Empty;
        if (value is string s) return s;
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString() ?? string.Empty,
                JsonValueKind.Number => je.ToString(), // "5", "3.14"
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => je.ToString()
            };
        }
        return value.ToString() ?? string.Empty;
    }
}
