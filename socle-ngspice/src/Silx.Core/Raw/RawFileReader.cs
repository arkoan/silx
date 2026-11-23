using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Silx.Core.Raw;

/// <summary>Lecteur de fichiers .raw ngspice (binaire et ASCII, réel et complexe, un seul plot).</summary>
public static class RawFileReader
{
    public static RawFile Read(string path) => Read(File.ReadAllBytes(path));

    public static RawFile Read(ReadOnlySpan<byte> data)
    {
        string title = "", plot = "";
        bool complex = false;
        int nVars = 0, nPoints = 0;
        var vars = new List<RawVariable>();
        int pos = 0;
        bool binary = false;

        while (true)
        {
            if (pos >= data.Length) throw new InvalidDataException("En-tête .raw incomplet.");
            string line = ReadLine(data, ref pos);
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            string key = line[..colon].Trim();
            string val = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "Title": title = val; break;
                case "Plotname": plot = val; break;
                case "Flags": complex = val.Contains("complex", StringComparison.OrdinalIgnoreCase); break;
                case "No. Variables": nVars = int.Parse(val, CultureInfo.InvariantCulture); break;
                case "No. Points": nPoints = int.Parse(val, CultureInfo.InvariantCulture); break;
                case "Variables":
                    for (int i = 0; i < nVars; i++)
                    {
                        var parts = ReadLine(data, ref pos).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        vars.Add(new RawVariable(int.Parse(parts[0], CultureInfo.InvariantCulture), parts[1], parts.Length > 2 ? parts[2] : ""));
                    }
                    break;
                case "Binary": binary = true; goto done;
                case "Values": goto done;
            }
        }
    done:
        var re = new double[nVars][];
        var im = complex ? new double[nVars][] : null;
        for (int v = 0; v < nVars; v++) { re[v] = new double[nPoints]; if (im != null) im[v] = new double[nPoints]; }

        if (binary)
        {
            int per = complex ? 2 : 1;
            long need = (long)nPoints * nVars * per * 8;
            if (data.Length - pos < need)
                throw new InvalidDataException($"Données binaires tronquées ({data.Length - pos} < {need} octets).");
            for (int p = 0; p < nPoints; p++)
                for (int v = 0; v < nVars; v++)
                {
                    re[v][p] = BinaryPrimitives.ReadDoubleLittleEndian(data[pos..]); pos += 8;
                    if (complex) { im![v][p] = BinaryPrimitives.ReadDoubleLittleEndian(data[pos..]); pos += 8; }
                }
        }
        else
        {
            for (int p = 0; p < nPoints; p++)
                for (int v = 0; v < nVars; v++)
                {
                    string l = ReadLine(data, ref pos).Trim();
                    if (v == 0) l = l[(l.IndexOfAny([' ', '\t']) + 1)..].Trim(); // index de point
                    if (complex)
                    {
                        var c = l.Split(',');
                        re[v][p] = double.Parse(c[0], CultureInfo.InvariantCulture);
                        im![v][p] = double.Parse(c[1], CultureInfo.InvariantCulture);
                    }
                    else re[v][p] = double.Parse(l, CultureInfo.InvariantCulture);
                }
        }

        return new RawFile { Title = title, PlotName = plot, IsComplex = complex, Variables = vars, PointCount = nPoints, Real = re, Imag = im };
    }

    private static string ReadLine(ReadOnlySpan<byte> d, ref int pos)
    {
        int end = d[pos..].IndexOf((byte)'\n');
        string s;
        if (end < 0) { s = Encoding.Latin1.GetString(d[pos..]); pos = d.Length; }
        else { s = Encoding.Latin1.GetString(d.Slice(pos, end)); pos += end + 1; }
        return s.TrimEnd('\r');
    }
}
