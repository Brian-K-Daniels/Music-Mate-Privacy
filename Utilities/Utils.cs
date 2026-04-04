using System.Diagnostics;
using System;
using System.Linq;
using System.Reflection;

namespace musicmate.Utilities
{
    public static class Utils
    {
        [Conditional("DEBUG")]
        public static void Log(object ob)
        {
            string message = "void";
            if(ob is string)
            {
                message = (string)ob;
            }
            else if ( ob is List<double> dblList )
            {
                message = string.Join(", ", dblList);
            }
            Debug.WriteLine($"[MusicMate] {message}");
        }

        /// <summary>
        /// Attempts to disable iOS safe area for the provided page using reflection.
        /// Uses runtime type lookup so this code can compile on non-iOS targets.
        /// </summary>
        public static void DisableIosSafeArea(Microsoft.Maui.Controls.Page page)
        {
            try
            {
                if (page == null) return;

                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                Type? iosPageType = null;
                Type? safeAreaEdgesType = null;

                foreach (var asm in assemblies)
                {
                    Type[] types;
                    try { types = asm.GetTypes(); } catch { continue; }
                    foreach (var t in types)
                    {
                        if (t.FullName == "Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page")
                            iosPageType = t;
                        else if (t.FullName == "Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.SafeAreaEdges")
                            safeAreaEdgesType = t;
                        if (iosPageType != null && safeAreaEdgesType != null)
                            break;
                    }
                    if (iosPageType != null && safeAreaEdgesType != null)
                        break;
                }

                if (iosPageType == null || safeAreaEdgesType == null)
                    return;

                var setMethod = iosPageType.GetMethod("SetSafeAreaEdges", BindingFlags.Public | BindingFlags.Static);
                if (setMethod == null)
                    return;

                var noneValue = Enum.Parse(safeAreaEdgesType, "None");
                setMethod.Invoke(null, new object[] { page, noneValue });
            }
            catch
            {
                // best-effort: ignore failures so non-iOS targets are unaffected
            }
        }
    }
    public static class MarginUtils
    {
        //public static void SetLeftMarginMM(Layout layout, double mm, double top = 0, double right = 0, double bottom = 0)
        //{
        //    double dips = MmToDips(mm);
        //    layout.Padding = new Thickness(dips, top, right, bottom);
        //}  //  2026.04.02 1719  block out

        //public static double MmToDips(double mm)
        //{
        //    // Convert millimetres to device-independent pixels (DIPs)
        //    return mm * (160.0 / 25.4);
        //} //  2026.04.02 1719  block out
    }
}

