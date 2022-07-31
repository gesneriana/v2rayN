using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }
}
