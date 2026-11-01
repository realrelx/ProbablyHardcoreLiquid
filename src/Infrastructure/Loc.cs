using System;

namespace ProbablyHardcoreLiquid
{
    // 语言适配：默认中文；游戏语言为英语系（locale code 以 "en" 开头）时切换英文。
    // 菜单标签是 Func<String> 委托，游戏渲染设置窗口时才求值，因此语言切换后重开窗口即生效。
    internal static class Loc
    {
        public static bool IsEnglish()
        {
            try
            {
                string code = LocHelper.GetCurrentLocaleCode();
                return !string.IsNullOrEmpty(code)
                    && code.StartsWith("en", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false; // 任何异常 → 默认中文
            }
        }

        public static string T(string zh, string en)
        {
            return IsEnglish() ? en : zh;
        }
    }
}
