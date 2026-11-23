using System.Runtime.InteropServices;

namespace Silx.Bench;

/// <summary>P/Invoke minimal vers libngspice (mode non-background). Une seule instance par process.</summary>
static unsafe class LibNgspice
{
    const string Lib = "ngspice";
    delegate int SendChar(IntPtr s, int id, IntPtr ud);
    delegate int SendStat(IntPtr s, int id, IntPtr ud);
    delegate int ControlledExit(int status, [MarshalAs(UnmanagedType.I1)] bool immediate, [MarshalAs(UnmanagedType.I1)] bool quit, int id, IntPtr ud);
    delegate int SendData(IntPtr d, int n, int id, IntPtr ud);
    delegate int SendInitData(IntPtr d, int id, IntPtr ud);
    delegate int BGThreadRunning([MarshalAs(UnmanagedType.I1)] bool running, int id, IntPtr ud);

    [DllImport(Lib)] static extern int ngSpice_Init(SendChar a, SendStat b, ControlledExit c, SendData d, SendInitData e, BGThreadRunning f, IntPtr ud);
    [DllImport(Lib)] static extern int ngSpice_Circ(IntPtr* lines);
    [DllImport(Lib)] static extern int ngSpice_Command(string cmd);
    [DllImport(Lib)] static extern IntPtr ngGet_Vec_Info(string name);

    static readonly SendChar _c = (_, _, _) => 0;
    static readonly SendStat _s = (_, _, _) => 0;
    static readonly ControlledExit _x = (_, _, _, _, _) => 0;
    static readonly SendData _d = (_, _, _, _) => 0;
    static readonly SendInitData _i = (_, _, _) => 0;
    static readonly BGThreadRunning _b = (_, _, _) => 0;

    public static void Init() => ngSpice_Init(_c, _s, _x, _d, _i, _b, IntPtr.Zero);

    /// <summary>Charge la netlist, lance l'analyse, copie le vecteur demandé, puis libère le circuit.</summary>
    public static double[] RunAndGet(string netlist, string vector)
    {
        var lines = netlist.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        var arr = new IntPtr[lines.Length + 1];
        for (int i = 0; i < lines.Length; i++) arr[i] = Marshal.StringToHGlobalAnsi(lines[i]);
        try
        {
            fixed (IntPtr* p = arr) ngSpice_Circ(p);
            ngSpice_Command("run");
            IntPtr vi = ngGet_Vec_Info(vector);
            if (vi == IntPtr.Zero) throw new InvalidOperationException("vecteur introuvable: " + vector);
            // struct vector_info { char* name; int type; short flags; double* real; ngcomplex* comp; int length; }
            double* re = *(double**)(vi + 16);
            int len = *(int*)(vi + 32);
            var res = new double[len];
            for (int i = 0; i < len; i++) res[i] = re[i];
            return res;
        }
        finally
        {
            ngSpice_Command("destroy all");
            for (int i = 0; i < lines.Length; i++) Marshal.FreeHGlobal(arr[i]);
        }
    }
}
