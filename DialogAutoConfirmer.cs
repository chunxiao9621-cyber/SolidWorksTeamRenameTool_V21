using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SolidWorksTeamRenameTool
{
    internal sealed class DialogAutoConfirmer : IDisposable
    {
        private const int WM_COMMAND = 0x0111;
        private const int IDOK = 1;
        private const int IDYES = 6;

        private readonly Thread _thread;
        private volatile bool _running;

        private DialogAutoConfirmer()
        {
            _running = true;
            _thread = new Thread(Worker);
            _thread.IsBackground = true;
            _thread.Start();
        }

        public static DialogAutoConfirmer Start()
        {
            return new DialogAutoConfirmer();
        }

        public void Dispose()
        {
            _running = false;
            if (_thread != null && _thread.IsAlive)
            {
                _thread.Join(1000);
            }
        }

        private void Worker()
        {
            while (_running)
            {
                try
                {
                    EnumWindows(EnumWindowCallback, IntPtr.Zero);
                }
                catch
                {
                }

                Thread.Sleep(400);
            }
        }

        private bool EnumWindowCallback(IntPtr hWnd, IntPtr lParam)
        {
            if (!_running || hWnd == IntPtr.Zero || !IsWindowVisible(hWnd))
            {
                return true;
            }

            string className = GetClassNameText(hWnd);
            if (!string.Equals(className, "#32770", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string title = GetWindowTitle(hWnd);
            if (title.IndexOf("SOLIDWORKS", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return true;
            }

            SendMessage(hWnd, WM_COMMAND, new IntPtr(IDOK), IntPtr.Zero);
            SendMessage(hWnd, WM_COMMAND, new IntPtr(IDYES), IntPtr.Zero);
            return true;
        }

        private static string GetWindowTitle(IntPtr hWnd)
        {
            StringBuilder buffer = new StringBuilder(256);
            GetWindowText(hWnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        private static string GetClassNameText(IntPtr hWnd)
        {
            StringBuilder buffer = new StringBuilder(128);
            GetClassName(hWnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
