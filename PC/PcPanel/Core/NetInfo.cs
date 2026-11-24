using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PcPanel.Core
{
    public sealed class NetInfo
    {
        public string Ip = "";
        public string Mac = "";
        public string Broadcast = "255.255.255.255";
        public string AdapterName = "";

        static readonly string[] VirtualWords = { "virtual", "vmware", "hyper-v", "vpn", "tap-", "tap ", "loopback", "bluetooth", "vethernet", "wireguard", "zerotier", "tailscale", "hamachi", "radmin" };

        /// <summary>Finds the real LAN adapter (Ethernet / Wi-Fi with a gateway).</summary>
        public static NetInfo Get()
        {
            var best = new NetInfo();
            int bestScore = -1;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211 &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.GigabitEthernet) continue;
                    string desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    if (VirtualWords.Any(w => desc.Contains(w))) continue;

                    var props = ni.GetIPProperties();
                    bool hasGw = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var b = ua.Address.GetAddressBytes();
                        if (b[0] == 169 && b[1] == 254) continue;
                        int score = (hasGw ? 10 : 0) + (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 2 : 1);
                        if (score <= bestScore) continue;
                        bestScore = score;
                        best.Ip = ua.Address.ToString();
                        best.AdapterName = ni.Name;
                        var mac = ni.GetPhysicalAddress().GetAddressBytes();
                        best.Mac = string.Join(":", mac.Select(x => x.ToString("X2")));
                        try
                        {
                            var m = ua.IPv4Mask.GetAddressBytes();
                            var bc = new byte[4];
                            for (int i = 0; i < 4; i++) bc[i] = (byte)(b[i] | ~m[i]);
                            best.Broadcast = new IPAddress(bc).ToString();
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { Log.Write("NetInfo: " + ex.Message); }
            return best;
        }
    }
}
