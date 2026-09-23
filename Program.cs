using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SolidWorksTeamRenameTool
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            object swApp = GetRunningSolidWorks();
            CheckSolidWorksVersion(swApp);
            try
            {
                System.Windows.Forms.Application.Run(new RenameForm(swApp));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "工具运行出错：" + ex.Message,
                    "层级命名工具",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static object GetRunningSolidWorks()
        {
            object app = null;

            try
            {
                app = Marshal.GetActiveObject("SldWorks.Application");
            }
            catch
            {
            }

            if (app == null)
            {
                try
                {
                    app = Marshal.GetActiveObject("SolidWorks.Application");
                }
                catch
                {
                }
            }

            return app;
        }

        private static void CheckSolidWorksVersion(object swApp)
        {
            if (swApp == null)
            {
                return;
            }

            try
            {
                object revision = swApp.GetType().InvokeMember(
                    "RevisionNumber",
                    BindingFlags.GetProperty,
                    null,
                    swApp,
                    null);
                string revisionText = Convert.ToString(revision ?? string.Empty);
                int dotIndex = revisionText.IndexOf('.');
                string majorText = dotIndex < 0 ? revisionText : revisionText.Substring(0, dotIndex);
                int major;
                if (int.TryParse(majorText, out major) && major < 24)
                {
                    MessageBox.Show(
                        "当前 SolidWorks 版本过低，不支持从 FeatureManager 重命名组件文件。\n请使用 SolidWorks 2016 或更高版本。",
                        "层级命名工具",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            catch
            {
            }
        }
    }
}
