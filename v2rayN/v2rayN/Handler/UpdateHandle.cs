using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using v2rayN.Base;
using v2rayN.Mode;
using v2rayN.Resx;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace v2rayN.Handler
{
    class UpdateHandle
    {
        Action<bool, string> _updateFunc;
        private Config _config;

        public event EventHandler<ResultEventArgs> AbsoluteCompleted;

        public class ResultEventArgs : EventArgs
        {
            public bool Success;
            public string Msg;

            public ResultEventArgs(bool success, string msg)
            {
                this.Success = success;
                this.Msg = msg;
            }
        }

        private readonly string nLatestUrl = Global.NUrl + "/latest";
        private const string nUrl = Global.NUrl + "/download/{0}/v2rayN.zip";
        private readonly string v2flyCoreLatestUrl = Global.v2flyCoreUrl + "/latest";
        private const string v2flyCoreUrl = Global.v2flyCoreUrl + "/download/{0}/v2ray-windows-{1}.zip";
        private readonly string xrayCoreLatestUrl = Global.xrayCoreUrl + "/latest";
        private const string xrayCoreUrl = Global.xrayCoreUrl + "/download/{0}/Xray-windows-{1}.zip";
        private const string geoUrl = "https://github.com/Loyalsoldier/v2ray-rules-dat/releases/latest/download/{0}.dat";

        public void CheckUpdateGuiN(Config config, Action<bool, string> update)
        {
            _config = config;
            _updateFunc = update;
            var url = string.Empty;

            DownloadHandle downloadHandle = null;
            if (downloadHandle == null)
            {
                downloadHandle = new DownloadHandle();

                downloadHandle.UpdateCompleted += (sender2, args) =>
                {
                    if (args.Success)
                    {
                        _updateFunc(false, ResUI.MsgDownloadV2rayCoreSuccessfully);

                        try
                        {
                            string fileName = Utils.GetPath(Utils.GetDownloadFileName(url));
                            fileName = Utils.UrlEncode(fileName);
                            Process process = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = "v2rayUpgrade.exe",
                                    Arguments = "\"" + fileName + "\"",
                                    WorkingDirectory = Utils.StartupPath()
                                }
                            };
                            process.Start();
                            if (process.Id > 0)
                            {
                                _updateFunc(true, "");
                            }
                        }
                        catch (Exception ex)
                        {
                            _updateFunc(false, ex.Message);
                        }
                    }
                    else
                    {
                        _updateFunc(false, args.Msg);
                    }
                };
                downloadHandle.Error += (sender2, args) =>
                {
                    _updateFunc(false, args.GetException().Message);
                };
            }
            AbsoluteCompleted += (sender2, args) =>
            {
                if (args.Success)
                {
                    _updateFunc(false, string.Format(ResUI.MsgParsingSuccessfully, "v2rayN"));

                    url = args.Msg;
                    askToDownload(downloadHandle, url, true);
                }
                else
                {
                    _updateFunc(false, args.Msg);
                }
            };
            _updateFunc(false, string.Format(ResUI.MsgStartUpdating, "v2rayN"));
            CheckUpdateAsync(ECoreType.v2rayN);
        }


        public void CheckUpdateCore(ECoreType type, Config config, Action<bool, string> update)
        {
            _config = config;
            _updateFunc = update;
            var url = string.Empty;

            DownloadHandle downloadHandle = null;
            if (downloadHandle == null)
            {
                downloadHandle = new DownloadHandle();
                downloadHandle.UpdateCompleted += (sender2, args) =>
                {
                    if (args.Success)
                    {
                        _updateFunc(false, ResUI.MsgDownloadV2rayCoreSuccessfully);
                        _updateFunc(false, ResUI.MsgUnpacking);

                        try
                        {
                            _updateFunc(true, url);
                        }
                        catch (Exception ex)
                        {
                            _updateFunc(false, ex.Message);
                        }
                    }
                    else
                    {
                        _updateFunc(false, args.Msg);
                    }
                };
                downloadHandle.Error += (sender2, args) =>
                {
                    _updateFunc(true, args.GetException().Message);
                };
            }

            AbsoluteCompleted += (sender2, args) =>
            {
                if (args.Success)
                {
                    _updateFunc(false, string.Format(ResUI.MsgParsingSuccessfully, "Core"));
                    url = args.Msg;
                    askToDownload(downloadHandle, url, true);
                }
                else
                {
                    _updateFunc(false, args.Msg);
                }
            };
            _updateFunc(false, string.Format(ResUI.MsgStartUpdating, "Core"));
            CheckUpdateAsync(type);
        }


        public void UpdateSubscriptionProcess(Config config, bool blProxy, Action<bool, string> update)
        {
            _config = config;
            _updateFunc = update;

            _updateFunc(false, ResUI.MsgUpdateSubscriptionStart);

            if (config.subItem == null || config.subItem.Count <= 0)
            {
                _updateFunc(false, ResUI.MsgNoValidSubscription);
                return;
            }

            Task.Run(async () =>
            {
                //Turn off system proxy
                bool bSysProxyType = false;
                if (!blProxy && config.sysProxyType == ESysProxyType.ForcedChange)
                {
                    bSysProxyType = true;
                    config.sysProxyType = ESysProxyType.ForcedClear;
                    SysProxyHandle.UpdateSysProxy(config, false);
                    Thread.Sleep(3000);
                }

                foreach (var item in config.subItem)
                {
                    if (item.enabled == false)
                    {
                        continue;
                    }
                    string id = item.id.TrimEx();
                    string url = item.url.TrimEx();
                    string userAgent = item.userAgent.TrimEx();
                    string groupId = item.groupId.TrimEx();
                    string hashCode = $"{item.remarks}->";
                    if (Utils.IsNullOrEmpty(id) || Utils.IsNullOrEmpty(url))
                    {
                        //_updateFunc(false, $"{hashCode}{ResUI.MsgNoValidSubscription}");
                        continue;
                    }

                    _updateFunc(false, $"{hashCode}{ResUI.MsgStartGettingSubscriptions}");
                    var result = await new DownloadHandle().DownloadStringAsync(url, blProxy, userAgent);

                    _updateFunc(false, $"{hashCode}{ResUI.MsgGetSubscriptionSuccessfully}");
                    if (Utils.IsNullOrEmpty(result))
                    {
                        _updateFunc(false, $"{hashCode}{ResUI.MsgSubscriptionDecodingFailed}");
                    }
                    else
                    {
                        int ret = ConfigHandler.AddBatchServers(ref config, result, id, groupId);
                        if (ret > 0)
                        {
                            ResolveDomainNames(config, userAgent, _updateFunc);
                            _updateFunc(false, $"{hashCode}{ResUI.MsgUpdateSubscriptionEnd}");
                        }
                        else
                        {
                            _updateFunc(false, $"{hashCode}{ResUI.MsgFailedImportSubscription}");
                        }
                    }
                    _updateFunc(false, $"-------------------------------------------------------");
                }
                //restore system proxy
                if (bSysProxyType)
                {
                    config.sysProxyType = ESysProxyType.ForcedChange;
                    SysProxyHandle.UpdateSysProxy(config, false);
                }
                _updateFunc(true, $"{ResUI.MsgUpdateSubscriptionEnd}");

            });
        }

        public static bool IsWin7 => Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor == 1;
        public static bool IsWin10 => Environment.OSVersion.Version.Major == 10;

        public string Decode(string str)
        {
            byte[] decbuff = Convert.FromBase64String(str.Replace(",", "=").Replace("-", "+").Replace("/", "_"));
            return Encoding.UTF8.GetString(decbuff);
        }

        public string Encode(string input)
        {
            byte[] encbuff = Encoding.UTF8.GetBytes(input ?? "");
            return Convert.ToBase64String(encbuff).Replace("=", ",").Replace("+", "-").Replace("_", "/");
        }
        /// <summary>
        /// Forcibly resolve the domain name to ip, solve DNS pollution
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        private void ResolveDomainNames(Config config, string userAgent, Action<bool, string> update)
        {
            _updateFunc = update;
            _ = Task.Factory.StartNew(async () =>
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
                                var dnsResult = await proc.StandardOutput.ReadToEndAsync();
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
                                    var dnsResult = await new DownloadHandle().DownloadStringAsync(urlString, true, userAgent, forceProxy: true);
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
                    _updateFunc(false, $"{ex.Message}\n{ex.StackTrace}");
                }
            });
        }


        public void UpdateGeoFile(string geoName, Config config, Action<bool, string> update)
        {
            _config = config;
            _updateFunc = update;
            var url = string.Format(geoUrl, geoName);

            DownloadHandle downloadHandle = null;
            if (downloadHandle == null)
            {
                downloadHandle = new DownloadHandle();

                downloadHandle.UpdateCompleted += (sender2, args) =>
                {
                    if (args.Success)
                    {
                        _updateFunc(false, string.Format(ResUI.MsgDownloadGeoFileSuccessfully, geoName));

                        try
                        {
                            string fileName = Utils.GetPath(Utils.GetDownloadFileName(url));
                            if (File.Exists(fileName))
                            {
                                string targetPath = Utils.GetPath($"{geoName}.dat");
                                if (File.Exists(targetPath))
                                {
                                    File.Delete(targetPath);
                                }
                                File.Move(fileName, targetPath);
                                //_updateFunc(true, "");
                            }
                        }
                        catch (Exception ex)
                        {
                            _updateFunc(false, ex.Message);
                        }
                    }
                    else
                    {
                        _updateFunc(false, args.Msg);
                    }
                };
                downloadHandle.Error += (sender2, args) =>
                {
                    _updateFunc(false, args.GetException().Message);
                };
            }
            askToDownload(downloadHandle, url, false);

        }

        public void RunAvailabilityCheck(Action<bool, string> update)
        {
            Task.Run(() =>
            {
                var time = (new DownloadHandle()).RunAvailabilityCheck(null);

                update(false, string.Format(ResUI.TestMeOutput, time));
            });
        }

        #region private

        private async void CheckUpdateAsync(ECoreType type)
        {
            try
            {
                string url;
                if (type == ECoreType.v2fly)
                {
                    url = v2flyCoreLatestUrl;
                }
                else if (type == ECoreType.Xray)
                {
                    url = xrayCoreLatestUrl;
                }
                else if (type == ECoreType.v2rayN)
                {
                    url = nLatestUrl;
                }
                else
                {
                    throw new ArgumentException("Type");
                }

                var result = await (new DownloadHandle()).UrlRedirectAsync(url, true);
                if (!Utils.IsNullOrEmpty(result))
                {
                    responseHandler(type, result);
                }
                else
                {
                    Utils.SaveLog("StatusCode error: " + url);
                    return;
                }
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
                _updateFunc(false, ex.Message);
            }
        }

        /// <summary>
        /// 获取V2RayCore版本
        /// </summary>
        private string getCoreVersion(ECoreType type)
        {
            try
            {

                var coreInfo = LazyConfig.Instance.GetCoreInfo(type);
                string filePath = string.Empty;
                foreach (string name in coreInfo.coreExes)
                {
                    string vName = string.Format("{0}.exe", name);
                    vName = Utils.GetPath(vName);
                    if (File.Exists(vName))
                    {
                        filePath = vName;
                        break;
                    }
                }

                if (!File.Exists(filePath))
                {
                    string msg = string.Format(ResUI.NotFoundCore, @"");
                    //ShowMsg(true, msg);
                    return "";
                }

                Process p = new Process();
                p.StartInfo.FileName = filePath;
                p.StartInfo.Arguments = "-version";
                p.StartInfo.WorkingDirectory = Utils.StartupPath();
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                p.Start();
                p.WaitForExit(5000);
                string echo = p.StandardOutput.ReadToEnd();
                string version = Regex.Match(echo, $"{coreInfo.match} ([0-9.]+) \\(").Groups[1].Value;
                return version;
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
                _updateFunc(false, ex.Message);
                return "";
            }
        }
        private void responseHandler(ECoreType type, string redirectUrl)
        {
            try
            {
                string version = redirectUrl.Substring(redirectUrl.LastIndexOf("/", StringComparison.Ordinal) + 1);

                string curVersion;
                string message;
                string url;
                if (type == ECoreType.v2fly)
                {
                    curVersion = "v" + getCoreVersion(type);
                    message = string.Format(ResUI.IsLatestCore, curVersion);
                    string osBit = Environment.Is64BitProcess ? "64" : "32";
                    url = string.Format(v2flyCoreUrl, version, osBit);
                }
                else if (type == ECoreType.Xray)
                {
                    curVersion = "v" + getCoreVersion(type);
                    message = string.Format(ResUI.IsLatestCore, curVersion);
                    string osBit = Environment.Is64BitProcess ? "64" : "32";
                    url = string.Format(xrayCoreUrl, version, osBit);
                }
                else if (type == ECoreType.v2rayN)
                {
                    curVersion = FileVersionInfo.GetVersionInfo(Utils.GetExePath()).FileVersion.ToString();
                    message = string.Format(ResUI.IsLatestN, curVersion);
                    url = string.Format(nUrl, version);
                }
                else
                {
                    throw new ArgumentException("Type");
                }

                if (curVersion == version)
                {
                    AbsoluteCompleted?.Invoke(this, new ResultEventArgs(false, message));
                    return;
                }

                AbsoluteCompleted?.Invoke(this, new ResultEventArgs(true, url));
            }
            catch (Exception ex)
            {
                Utils.SaveLog(ex.Message, ex);
                _updateFunc(false, ex.Message);
            }
        }

        private void askToDownload(DownloadHandle downloadHandle, string url, bool blAsk)
        {
            bool blDownload = false;
            if (blAsk)
            {
                if (UI.ShowYesNo(string.Format(ResUI.DownloadYesNo, url)) == DialogResult.Yes)
                {
                    blDownload = true;
                }
            }
            else
            {
                blDownload = true;
            }
            if (blDownload)
            {
                downloadHandle.DownloadFileAsync(url, true, 600);
            }
        }
        #endregion
    }
}
