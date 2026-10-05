using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace BoostParaPc.Services;

/// <summary>Mede a latência de servidores DNS com consultas UDP diretas (3 domínios × 3 repetições, mediana).</summary>
public static class DnsBenchmark
{
    public sealed record Result(string Name, string Primary, string Secondary, double? MedianMs);

    private static readonly string[] Domains = ["www.google.com", "www.microsoft.com", "www.wikipedia.org"];

    public static readonly (string Name, string Primary, string Secondary)[] Public =
    [
        ("Cloudflare", "1.1.1.1", "1.0.0.1"),
        ("Google", "8.8.8.8", "8.8.4.4"),
        ("Quad9", "9.9.9.9", "149.112.112.112"),
    ];

    /// <summary>Monta uma consulta DNS tipo A (RFC 1035), recursão desejada.</summary>
    public static byte[] BuildQuery(string domain, ushort id)
    {
        var packet = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0 };
        foreach (var label in domain.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(label);
            if (bytes.Length is 0 or > 63) throw new ArgumentException("Rótulo DNS inválido.", nameof(domain));
            packet.Add((byte)bytes.Length);
            packet.AddRange(bytes);
        }
        packet.AddRange([0, 0, 1, 0, 1]);   // fim do nome, tipo A, classe IN
        return [.. packet];
    }

    /// <summary>Resposta válida: mesmo id, bit de resposta ligado, código de erro 0 (NOERROR).</summary>
    public static bool IsValidResponse(byte[] response, ushort id) =>
        response.Length >= 12 && response[0] == (byte)(id >> 8) && response[1] == (byte)id
        && (response[2] & 0x80) != 0 && (response[3] & 0x0F) == 0;

    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0) throw new InvalidOperationException("Sem amostras.");
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    public static async Task<double?> MeasureAsync(string server, CancellationToken ct = default)
    {
        var samples = new List<double>();
        var id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        using var udp = new UdpClient();
        udp.Connect(IPAddress.Parse(server), 53);
        foreach (var domain in Domains)
            for (var i = 0; i < 3; i++)
            {
                ct.ThrowIfCancellationRequested();
                id++;
                var query = BuildQuery(domain, id);
                var timer = Stopwatch.StartNew();
                try
                {
                    await udp.SendAsync(query, ct).ConfigureAwait(false);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(2000);
                    var reply = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                    if (IsValidResponse(reply.Buffer, id)) samples.Add(timer.Elapsed.TotalMilliseconds);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* sem resposta em 2 s */ }
                catch (SocketException) { }
            }
        return samples.Count == 0 ? null : Median(samples);
    }

    public static async Task<IReadOnlyList<Result>> RunAsync(string? currentServer, CancellationToken ct = default)
    {
        var candidates = Public.ToList();
        if (currentServer is not null && IPAddress.TryParse(currentServer, out _) && !candidates.Any(c => c.Primary == currentServer))
            candidates.Insert(0, ("Atual", currentServer, ""));
        var results = new List<Result>();
        foreach (var (name, primary, secondary) in candidates)
            results.Add(new Result(name, primary, secondary, await MeasureAsync(primary, ct).ConfigureAwait(false)));
        return results.OrderBy(r => r.MedianMs ?? double.MaxValue).ToList();
    }
}
