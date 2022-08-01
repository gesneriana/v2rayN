using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using v2rayN.Base;
using v2rayN.Handler;
using v2rayN.Mode;

namespace v2rayN.Tool
{
    public class NetTool
    {
        /// <summary>
        /// 获取网关和网卡ip
        /// </summary>
        /// <returns></returns>
        public static Tuple<string, string> GetGatewayIp()
        {
            var gatewayIp = string.Empty;
            var ip = string.Empty;
            var result = Utils.ExecCmd("chcp 65001 & ipconfig");
            var lines = result.Split('\n');
            var ipList = new List<string>();
            int i = 0;
            foreach (var line in lines)
            {
                if (line.Contains("默认网关") || line.Contains("Default Gateway"))
                {
                    var gw1 = line;
                    var gw2 = "";

                    if (lines.Length > i + 1) { gw2 = lines[i + 1]; }

                    if (gw1.Count((x) => { if (x == '.') { return true; } return false; }) == 3)
                    {
                        var gatewayLineSlice = gw1.Split(':');
                        var gatewayString = gatewayLineSlice.Last();
                        gatewayIp = gatewayString.Trim('\r').Trim();
                    }
                    else if (gw2.Count((x) => { if (x == '.') { return true; } return false; }) == 3)
                    {
                        var gatewayLineSlice = gw2.Split(':');
                        var gatewayString = gatewayLineSlice.Last();
                        gatewayIp = gatewayString.Trim('\r').Trim();
                    }
                }

                if (line.Contains("IPv4 地址") || line.Contains("IPv4 Address"))
                {
                    var ipLineSlice = line.Split(':');
                    var ipString = ipLineSlice.Last();
                    if (ipString.Count((x) => { if (x == '.') { return true; } return false; }) == 3)
                    {
                        ipList.Add(ipString.Trim('\r').Trim());
                    }
                }

                i++;
            }

            if (gatewayIp.Length > 0)
            {
                var gatewaySlice = gatewayIp.Split('.');
                if (gatewaySlice.Length == 4)
                {
                    // 暂不考虑cidr ip范围为16的情况
                    var prefixString = string.Join(".", gatewaySlice.Take(3));

                    foreach (var ipString in ipList)
                    {
                        if (ipString.StartsWith(prefixString))
                        {
                            ip = ipString; // 使用匹配的 网关和网卡ip, 如果有多个网卡, 请考虑手动指定网关ip和网卡ip
                            break;
                        }
                    }
                }
            }


            return Tuple.Create(gatewayIp, ip);
        }

        public static bool IsWin7 => Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor == 1;
        public static bool IsWin10 => Environment.OSVersion.Version.Major == 10;

        public static string Decode(string str)
        {
            byte[] decbuff = Convert.FromBase64String(str.Replace(",", "=").Replace("-", "+").Replace("/", "_"));
            return Encoding.UTF8.GetString(decbuff);
        }

        public static string Encode(string input)
        {
            byte[] encbuff = Encoding.UTF8.GetBytes(input ?? "");
            return Convert.ToBase64String(encbuff).Replace("=", ",").Replace("+", "-").Replace("_", "/");
        }
        /// <summary>
        /// Forcibly resolve the domain name to ip, solve DNS pollution
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        public static void ResolveDomainNames(Config config, string userAgent)
        {
            try
            {
                if (config.subItem != null && config.subItem.Count > 0)
                {
                    foreach (var sub in config.subItem)
                    {
                        if (!sub.forceParsing)
                        {
                            continue;
                        }
                        var vmessItems = config.vmess.Where(x => x.subid.Equals(sub.id));
                        if (!vmessItems.Any())
                        {
                            continue;
                        }
                        Dictionary<string, string> domains = new Dictionary<string, string>();
                        foreach (var vmessItem in vmessItems)
                        {
                            domains[vmessItem.address] = String.Empty;  // Domain name deduplication
                        }

                        var urlStringList = new List<string>();
                        foreach (var domain in domains.Keys.ToList())
                        {
                            var urlString = $"https://dns.google/resolve?name={domain}&type=A";
                            urlStringList.Add(urlString);
                        }

                        // 如果是win7, 那么调用我用golang写的程序请求Google DNS解析域名, Win7 会有TLS握手报错的bug
                        if (IsWin7)
                        {
                            var urlStringParam = string.Join(",", urlStringList);
                            var proc = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = "./Resources/http-tool.exe", // 这个程序的源码仓库是 https://github.com/gesneriana/http-tool  如果不放心请自行编译, 替换Resources中的即可
                                    Arguments = $"\"socks5://127.0.0.1:{config.GetLocalPort(Global.InboundSocks)}\" \"{urlStringParam}\"",
                                    UseShellExecute = false,
                                    RedirectStandardOutput = true,
                                    CreateNoWindow = true
                                }
                            };
                            proc.Start();
                            proc.WaitForExit();
                            var dnsResult = proc.StandardOutput.ReadToEnd();
                            if (string.IsNullOrWhiteSpace(dnsResult))
                            {
                                continue;
                            }
                            var rawDnsResult = Decode(dnsResult);
                            var dnsDataList = JsonConvert.DeserializeObject<List<DNSQuery>>(rawDnsResult);
                            if (dnsDataList == null || dnsDataList.Count == 0)
                            {
                                continue;
                            }
                            foreach (var dnsData in dnsDataList)
                            {
                                if (dnsData.Answer == null)
                                {
                                    continue;
                                }
                                var ans = dnsData.Answer.FirstOrDefault();
                                domains[ans.name.Trim('.')] = ans.data;
                            }
                        }
                        else
                        {
                            foreach (var urlString in urlStringList)
                            {
                                var dnsResult = new DownloadHandle().DownloadStringAsync(urlString, true, userAgent, forceProxy: true).GetAwaiter().GetResult();
                                if (string.IsNullOrWhiteSpace(dnsResult))
                                {
                                    continue;
                                }
                                var dnsData = JsonConvert.DeserializeObject<DNSQuery>(dnsResult);
                                if (dnsData.Answer == null)
                                {
                                    continue;
                                }
                                var ans = dnsData.Answer.FirstOrDefault();
                                domains[ans.name.Trim('.')] = ans.data;
                            }
                        }

                        foreach (var vmessItem in vmessItems)
                        {
                            if (domains.TryGetValue(vmessItem.address, out var ip))
                            {
                                if (string.IsNullOrWhiteSpace(ip))
                                {
                                    continue;
                                }
                                vmessItem.address = ip;  // Forcibly resolve the domain name to ip, solve DNS pollution
                            }
                        }
                    }

                    ConfigHandler.SaveConfig(ref config);
                }
            }
            catch (Exception ex)
            {
                UI.ShowWarning($"{ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 获取机场的ip，加到路由表, 直连
        /// </summary>
        /// <param name="config"></param>
        /// <param name="userAgent"></param>
        public static List<string> GetDirectIpList(Config config)
        {
            List<string> ipList = new List<string>();
            try
            {
                if (config.subItem != null && config.subItem.Count > 0)
                {
                    Dictionary<string, string> domains = new Dictionary<string, string>();
                    foreach (var sub in config.subItem)
                    {
                        var vmessItems = config.vmess.Where(x => x.subid.Equals(sub.id));
                        if (!vmessItems.Any())
                        {
                            continue;
                        }

                        foreach (var vmessItem in vmessItems)
                        {
                            domains[vmessItem.address] = String.Empty;  // Domain name deduplication
                        }

                        var urlStringList = new List<string>();
                        foreach (var domain in domains.Keys.ToList())
                        {
                            var urlString = $"https://dns.google/resolve?name={domain}&type=A";
                            urlStringList.Add(urlString);
                        }

                        // 如果是win7, 那么调用我用golang写的程序请求Google DNS解析域名, Win7 会有TLS握手报错的bug
                        if (IsWin7)
                        {
                            var urlStringParam = string.Join(",", urlStringList);
                            var proc = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = "./Resources/http-tool.exe", // 这个程序的源码仓库是 https://github.com/gesneriana/http-tool  如果不放心请自行编译, 替换Resources中的即可
                                    Arguments = $"\"socks5://127.0.0.1:{config.GetLocalPort(Global.InboundSocks)}\" \"{urlStringParam}\"",
                                    UseShellExecute = false,
                                    RedirectStandardOutput = true,
                                    CreateNoWindow = true
                                }
                            };
                            proc.Start();
                            proc.WaitForExit();
                            var dnsResult = proc.StandardOutput.ReadToEnd();
                            if (string.IsNullOrWhiteSpace(dnsResult))
                            {
                                continue;
                            }
                            var rawDnsResult = Decode(dnsResult);
                            var dnsDataList = JsonConvert.DeserializeObject<List<DNSQuery>>(rawDnsResult);
                            if (dnsDataList == null || dnsDataList.Count == 0)
                            {
                                continue;
                            }
                            foreach (var dnsData in dnsDataList)
                            {
                                if (dnsData.Answer == null)
                                {
                                    continue;
                                }
                                var ans = dnsData.Answer.FirstOrDefault();
                                domains[ans.name.Trim('.')] = ans.data;
                            }
                        }
                        else
                        {
                            foreach (var urlString in urlStringList)
                            {
                                var dnsResult = new DownloadHandle().DownloadStringAsync(urlString, true, sub.userAgent.TrimEx(), forceProxy: true).GetAwaiter().GetResult();
                                if (string.IsNullOrWhiteSpace(dnsResult))
                                {
                                    continue;
                                }
                                var dnsData = JsonConvert.DeserializeObject<DNSQuery>(dnsResult);
                                if (dnsData.Answer == null)
                                {
                                    continue;
                                }
                                var ans = dnsData.Answer.FirstOrDefault();
                                domains[ans.name.Trim('.')] = ans.data;
                            }
                        }
                    }

                    foreach (var item in domains.Values)
                    {
                        if (!string.IsNullOrWhiteSpace(item))
                        {
                            ipList.Add(item);
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                UI.ShowWarning($"{ex.Message}\n{ex.StackTrace}");
            }

            return ipList;
        }

        public static void AddRoute(List<string> ipList, string gateway)
        {
            if (ipList == null || ipList.Count == 0) { return; }
            var cmdList = new List<string>();
            foreach (var ipString in ipList)
            {
                var commandString = $"route add {ipString} {gateway} metric 5";
                cmdList.Add(commandString);
            }
            var cmdString = string.Join(" & ", cmdList);
            Utils.ExecCmd(cmdString); // 添加直连ip到路由表
        }

    }
}
