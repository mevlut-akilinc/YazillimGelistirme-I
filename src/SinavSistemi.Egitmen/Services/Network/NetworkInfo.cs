using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SinavSistemi.Egitmen.Services.Network;

/// <summary>Öğrencilerin bağlanacağı yerel ağ (LAN/Wi-Fi) IPv4 adresini bulur.</summary>
public static class NetworkInfo
{
    // Sanal adaptörlerin adresi öğrenci bilgisayarlarından erişilemez, listede gösterilmemeli.
    private static readonly string[] VirtualHints =
        { "virtual", "vmware", "hyper-v", "vethernet", "loopback", "bluetooth", "tap-", "wsl" };

    public static string GetLanAddress()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                        && !VirtualHints.Any(h => (n.Name + " " + n.Description).Contains(h, StringComparison.OrdinalIgnoreCase)))
            .Select(n => (Props: n.GetIPProperties(), Nic: n))
            .SelectMany(x => x.Props.UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                            && !IPAddress.IsLoopback(a.Address)
                            && !a.Address.ToString().StartsWith("169.254."))   // Windows'un DHCP bulamayınca verdiği adres
                .Select(a => (a.Address, HasGateway: x.Props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))))
            .ToList();

        // Varsayılan ağ geçidi olan adaptör (hotspot / Wi-Fi) büyük ihtimalle doğru olandır.
        var best = candidates.OrderByDescending(c => c.HasGateway).Select(c => c.Address).FirstOrDefault();
        return best?.ToString() ?? "127.0.0.1";
    }
}
